using System.Runtime.InteropServices;

static class CarrierCacheProof
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Bootstrap(byte[] bytes, int length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Create(IntPtr callback, IntPtr data, int limit);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Call(IntPtr guest, [MarshalAs(UnmanagedType.LPStr)] string name, int a, int b, int args, int results, ulong fuel, out int result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Destroy(IntPtr guest);
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run(string bridgePath)
    {
        var library = NativeLibrary.Load(bridgePath);
        T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
        var bootstrap = Export<Bootstrap>("pui_bootstrap");
        var create = Export<Create>("pui_create_with_host_call_limit");
        var call = Export<Call>("pui_call");
        var destroy = Export<Destroy>("pui_destroy");
        // A real Wasm module exporting memory and version(): i32.
        byte[] first = {0,97,115,109,1,0,0,0,1,5,1,96,0,1,127,3,2,1,0,5,3,1,0,1,
            7,20,2,6,109,101,109,111,114,121,2,0,7,118,101,114,115,105,111,110,0,0,
            10,6,1,4,0,65,1,11};
        var second = (byte[])first.Clone(); second[^2] = 2;
        int Version(IntPtr guest)
        {
            Check(guest != IntPtr.Zero, "Guest created");
            Check(call(guest, "version", 0, 0, 0, 1, 10000, out int version) == 0, "Version export executes");
            return version;
        }
        Check(bootstrap(first, first.Length) == 0, "Initial carrier compiled");
        var old = create(IntPtr.Zero, IntPtr.Zero, 64);
        try
        {
            Check(Version(old) == 1, "Initial carrier active");
            Check(bootstrap(second, second.Length) == 0, "Changed carrier compiled");
            Check(bootstrap(second, second.Length) == 0, "Identical carrier accepted");
            Check(bootstrap(new byte[] {1,2,3}, 3) != 0, "Invalid replacement rejected");
            var current = create(IntPtr.Zero, IntPtr.Zero, 64);
            try { Check(Version(current) == 2, "New guests use changed carrier after rejected replacement"); }
            finally { destroy(current); }
            Check(Version(old) == 1, "Existing guest survives module replacement");
        }
        finally { destroy(old); }
        Console.WriteLine("PASS carrier cache: changed module, identical reload, rejected replacement, existing guest lifetime");
    }
}
