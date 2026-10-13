// Development-only validation of an external candidate, never the game carrier.
#if UNITY_EDITOR || DEVELOPMENT_BUILD || !UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PortableUi.Wasmtime.Diagnostics
{
    public static class SnapshotLifecycleProbe
    {
        const string Library = "portable_ui_snapshot_probe";
        const string CandidateHash = "F97FBCA285AD00B61903A30FE69579FCCE2EC5041100D12405930C0810394065";
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Callback(IntPtr data, int op, long a, long b, long c, long d, IntPtr memory, ulong size);
        static readonly Callback HostCallback = Dispatch;
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryW(string path);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int pui_bootstrap(byte[] bytes, int size);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr pui_bootstrap_error();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr pui_create(Callback callback, IntPtr data);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int pui_call(IntPtr guest, [MarshalAs(UnmanagedType.LPStr)] string name, int a, int b, int args, int results, ulong fuel, out int value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr pui_memory(IntPtr guest, out ulong size);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr pui_error(IntPtr guest);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern ulong pui_fuel(IntPtr guest);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void pui_destroy(IntPtr guest);
        [Serializable] public sealed class Report
        {
            public string runtime, carrierSha256;
            public int checks, created, destroyed;
            public string[] failures;
            public bool passed;
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        sealed class Instance : IDisposable
        {
            internal IntPtr Guest;
            internal GCHandle Handle;
            internal byte[] Packet;
            internal ulong Next = 1;
            internal long Now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            internal bool Trapped;
            readonly Report report;
            public Instance(Report report)
            {
                this.report = report;
                Handle = GCHandle.Alloc(this);
                Guest = pui_create(HostCallback, GCHandle.ToIntPtr(Handle));
                if (Guest == IntPtr.Zero) { Handle.Free(); throw new Exception(Marshal.PtrToStringAnsi(pui_bootstrap_error())); }
                report.created++;
                try { Require(Call("portable_ui_restore", 0, 0, 0, 1, 2000000) == 0, "restore"); }
                catch { Dispose(); throw; }
            }
            internal int Call(string name, int a, int b, int args, int results, ulong fuel)
            {
                Require(Guest != IntPtr.Zero, "disposed instance rejected");
                int status = pui_call(Guest, name, a, b, args, results, fuel, out int result);
                Require(pui_fuel(Guest) <= fuel, "finite export fuel");
                if (status != 0) { Trapped = true; throw new Exception(Marshal.PtrToStringAnsi(pui_error(Guest))); }
                return result;
            }
            internal bool Eval(string source, ulong fuel = 20000000)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(source + "\0");
                Require(bytes.Length <= 65537, "addon source quota");
                int pointer = Call("portable_ui_alloc", bytes.Length, 0, 1, 1, 200000);
                Require(pointer > 0, "allocation");
                try {
                    IntPtr memory = pui_memory(Guest, out ulong size);
                    Require((ulong)pointer + (ulong)bytes.Length <= size, "memory bound");
                    Marshal.Copy(bytes, 0, IntPtr.Add(memory, pointer), bytes.Length);
                    return Call("portable_ui_eval", pointer, bytes.Length - 1, 2, 1, fuel) == 0;
                } finally { if (!Trapped) Call("portable_ui_free", pointer, 0, 1, 0, 200000); }
            }
            internal void Update() { Require(Call("portable_ui_update_v2", 0, 0, 0, 1, 20000000) >= 0, "update"); }
            internal string Text => Packet == null ? "" : Encoding.UTF8.GetString(Packet);
            public void Dispose()
            {
                if (Guest == IntPtr.Zero) return;
                try { if (!Trapped) Require(Eval("globalThis.portableUiDispose?.();", 5000000), "SDK disposal"); }
                finally { pui_destroy(Guest); Guest = IntPtr.Zero; Handle.Free(); report.destroyed++; }
            }
        }
#if UNITY_5_3_OR_NEWER
        [AOT.MonoPInvokeCallback(typeof(Callback))]
#endif
        static int Dispatch(IntPtr data, int op, long a, long b, long c, long d, IntPtr memory, ulong size)
        {
            try {
                var instance = (Instance)GCHandle.FromIntPtr(data).Target;
                IntPtr Address(long offset, long length) {
                    Require(offset >= 0 && length >= 0 && (ulong)offset + (ulong)length <= size, "callback bounds");
                    return IntPtr.Add(memory, checked((int)offset));
                }
                if (op == 0) { Marshal.WriteInt64(Address(a, 8), (long)instance.Next++); return 0; }
                if (op == 1) { Require(b <= 65536, "packet quota"); instance.Packet = new byte[(int)b]; Marshal.Copy(Address(a, b), instance.Packet, 0, (int)b); return 0; }
                if (op == 2 || op == 3) return 0;
                if (op == 7) return 0;
                if (op == 8) { Marshal.WriteInt32(Address(a, 4), 0); Marshal.WriteInt32(Address(b, 4), 0); return 0; }
                if (op == 9) { Marshal.WriteInt64(Address(c, 8), instance.Now * 1000000); return 0; }
                return -1;
            } catch { return -1; }
        }
        public static Report Run(string nativeDirectory, string carrierPath, string fixturePath, string output)
        {
            var report = new Report(); var failures = new List<string>();
            void Check(bool value, string name) { report.checks++; if (!value) failures.Add(name); }
            try {
                byte[] carrier = File.ReadAllBytes(carrierPath);
                using (var sha = SHA256.Create()) report.carrierSha256 = BitConverter.ToString(sha.ComputeHash(carrier)).Replace("-", "");
                Require(report.carrierSha256 == CandidateHash, "pinned snapshot");
                Require(LoadLibraryW(Path.GetFullPath(Path.Combine(nativeDirectory, "wasmtime.dll"))) != IntPtr.Zero, "load Wasmtime");
                Require(LoadLibraryW(Path.GetFullPath(Path.Combine(nativeDirectory, Library + ".dll"))) != IntPtr.Zero, "load candidate bridge");
                Require(pui_bootstrap(carrier, carrier.Length) == 0, "bootstrap");
                string fixture = File.ReadAllText(fixturePath);
                using (var alpha = new Instance(report)) using (var beta = new Instance(report)) {
                    Require(alpha.Eval("globalThis.probeName='alpha';" + fixture), "alpha TSX"); alpha.Update();
                    Require(beta.Eval("globalThis.probeName='beta';" + fixture), "beta TSX"); beta.Update();
                    Check(alpha.Text.Contains("alpha:0") && beta.Text.Contains("beta:0"), "independent initial React trees");
                    Require(alpha.Eval("probeSet(7);globalThis.onlyAlpha=7;Object.prototype.onlyAlpha=7;__portableReact.React.onlyAlpha=7;"), "mutate alpha"); alpha.Update();
                    Check(alpha.Text.Contains("alpha:7"), "React state update");
                    Check(beta.Eval("if(globalThis.onlyAlpha || Object.prototype.onlyAlpha || __portableReact.React.onlyAlpha)throw Error('leak');"), "globals/prototypes/React object isolated");
                    beta.Update(); Check(beta.Text.Contains("beta:0"), "beta state unchanged");
                    Check(beta.Eval("if(Date.now()!==" + beta.Now + ")throw Error('clock');"), "clock reads live host time");
                    beta.Now += 1000;
                    Check(beta.Eval("if(Date.now()!==" + beta.Now + ")throw Error('clock');"), "clock advances after restore");
                    const string randomProbe = "portableUi.submit(new Uint8Array(new Float64Array([Math.random(),Math.random(),Math.random(),Math.random()]).buffer));";
                    Require(alpha.Eval(randomProbe) && beta.Eval(randomProbe), "random capture");
                    Check(Convert.ToBase64String(alpha.Packet) != Convert.ToBase64String(beta.Packet), "fresh independent random sequence after restore");
                    Check(beta.Eval("globalThis.timerFired=0;setTimeout(()=>timerFired++,100);"), "timer queued");
                    beta.Update(); Check(beta.Eval("if(timerFired!==0)throw Error('early timer');"), "timer not early");
                    beta.Now += 101; beta.Update(); Check(beta.Eval("if(timerFired!==1)throw Error('missing timer');"), "timer fires at host deadline");
                    using (var rejected = new Instance(report)) Check(!rejected.Eval("const = syntax error"), "bad reload rejected");
                    Check(alpha.Eval("if(onlyAlpha!==7)throw Error('lost old state');"), "bad reload preserves old instance");
                    using (var trapped = new Instance(report)) {
                        bool rejected = false; try { trapped.Eval("while(true){}"); } catch { rejected = trapped.Trapped; }
                        Check(rejected, "runaway still traps at unchanged 20M");
                    }
                    Check(beta.Eval("if(probeCleanup!==0)throw Error('early cleanup');portableUiDispose();if(probeCleanup!==1)throw Error('cleanup missing');"), "React effect cleanup");
                    alpha.Dispose(); alpha.Dispose(); Check(alpha.Guest == IntPtr.Zero, "idempotent host disposal");
                    Check(beta.Eval("if(globalThis.onlyAlpha)throw Error('leak');"), "survivor works after sibling disposal");
                    for (int i = 0; i < 12; i++) using (var replacement = new Instance(report)) {
                        Check(replacement.Eval("if(globalThis.onlyAlpha || Object.prototype.onlyAlpha || globalThis.probeSet)throw Error('dirty snapshot');globalThis.probeName='reload';" + fixture), "fresh reload " + i);
                        replacement.Update(); Check(replacement.Text.Contains("reload:0"), "reload initial React state " + i);
                    }
                }
            } catch (Exception error) { failures.Add(error.ToString()); }
            Check(report.created == report.destroyed, "all native instances disposed");
            report.failures = failures.ToArray(); report.passed = failures.Count == 0;
#if ENABLE_IL2CPP
            report.runtime = "Unity IL2CPP";
#elif UNITY_5_3_OR_NEWER
            report.runtime = "Unity Mono";
#else
            report.runtime = ".NET standalone";
#endif
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
#if UNITY_5_3_OR_NEWER
            File.WriteAllText(output, UnityEngine.JsonUtility.ToJson(report, true));
#else
            File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
#endif
            return report;
        }
#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void PlayerProbe()
        {
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-snapshot-lifecycle");
            if (index < 0 || index + 4 >= args.Length) return;
            var result = Run(args[index + 1], args[index + 2], args[index + 3], args[index + 4]);
            UnityEngine.Application.Quit(result.passed ? 0 : 1);
        }
#endif
    }
}
#endif
