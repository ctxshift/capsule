using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

var options = new Dictionary<string, string>();
for (int i = 0; i < args.Length; i += 2)
    if (i + 1 >= args.Length || !args[i].StartsWith("--") || !options.TryAdd(args[i], args[i + 1]))
        throw new ArgumentException("Usage: --bridge <library> --runtime <library> --source <carrier> --output <directory>");
foreach (var name in new[] { "--bridge", "--runtime", "--source", "--output" })
    if (!options.ContainsKey(name)) throw new ArgumentException("Missing " + name);
NativeLibrary.Load(Path.GetFullPath(options["--runtime"]));
var library = NativeLibrary.Load(Path.GetFullPath(options["--bridge"]));
var compile = Marshal.GetDelegateForFunctionPointer<Compile>(NativeLibrary.GetExport(library, "pui_precompile"));
var free = Marshal.GetDelegateForFunctionPointer<Free>(NativeLibrary.GetExport(library, "pui_precompiled_free"));
var error = Marshal.GetDelegateForFunctionPointer<Error>(NativeLibrary.GetExport(library, "pui_bootstrap_error"));
byte[] source = File.ReadAllBytes(options["--source"]);
if (compile(source, source.Length, out var pointer, out int length) != 0)
    throw new InvalidOperationException(Marshal.PtrToStringUTF8(error()));
byte[] compiled = new byte[length];
try { Marshal.Copy(pointer, compiled, 0, length); }
finally { free(pointer); }
Directory.CreateDirectory(options["--output"]);
File.WriteAllBytes(Path.Combine(options["--output"], "CapsuleQuickJs.cwasm"), compiled);
var descriptor = new
{
    schemaVersion = 1,
    target = "x86_64-pc-windows-msvc",
    runtimeVersion = "48.0.2",
    runtimeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(options["--runtime"]))).ToLowerInvariant(),
    sourceSha256 = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(),
    compiledSha256 = Convert.ToHexString(SHA256.HashData(compiled)).ToLowerInvariant()
};
File.WriteAllText(Path.Combine(options["--output"], "CapsuleQuickJs.precompiled.json"), JsonSerializer.Serialize(descriptor, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Precompiled runtime: {compiled.Length} bytes, {descriptor.compiledSha256}");

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int Compile(byte[] source, int length, out IntPtr bytes, out int compiledLength);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void Free(IntPtr bytes);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate IntPtr Error();
