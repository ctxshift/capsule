using System;
using System.IO;
using System.Security.Cryptography;
using Capsule.Wasmtime;
using Newtonsoft.Json.Linq;

namespace Capsule.Host;

public static class CompiledCarrier
{
    public static byte[] Read(string resources, byte[] source)
    {
        string descriptor = Path.Combine(resources, "CapsuleQuickJs.precompiled.json");
        string artifact = Path.Combine(resources, "CapsuleQuickJs.cwasm");
        if (!File.Exists(descriptor) && !File.Exists(artifact)) return null;
        if (!File.Exists(descriptor) || !File.Exists(artifact)) throw new ArgumentException("Precompiled runtime artifact is incomplete.");
        if (new FileInfo(descriptor).Length > 8192 || new FileInfo(artifact).Length < 1 || new FileInfo(artifact).Length > 16777216) throw new ArgumentException("Precompiled runtime artifact quota.");
        var value = JObject.Parse(File.ReadAllText(descriptor));
        string sourceHash = Convert.ToHexString(SHA256.HashData(source));
        if ((int?)value["schemaVersion"] != 1 || (string)value["target"] != "x86_64-pc-windows-msvc" || (string)value["runtimeVersion"] != "48.0.2" ||
            (string)value["runtimeSha256"] != "19b3880fbb92f1826c58ac9f5e2a472692b5fc3d012a1a7a41983ccf77544607" ||
            !string.Equals((string)value["sourceSha256"], WasmtimeUiGuest.QuickJsSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(sourceHash, WasmtimeUiGuest.QuickJsSha256, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Precompiled runtime source or target mismatch.");
        byte[] bytes = File.ReadAllBytes(artifact);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), (string)value["compiledSha256"], StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Precompiled runtime checksum mismatch.");
        return bytes;
    }
}
