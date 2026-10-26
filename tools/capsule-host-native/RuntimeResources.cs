using System;
using System.IO;
using System.Security.Cryptography;
using Capsule.Wasmtime;

namespace Capsule.Host;

// Immutable process-owned engine resources. Addon publications never supply these bytes.
internal sealed class RuntimeResources
{
    static readonly object Gate = new();
    static RuntimeResources instance;
    readonly string directory;
    public readonly byte[] Carrier, Sdk, Compiled;
    public readonly string SdkIdentity;

    RuntimeResources(string directory)
    {
        this.directory = directory;
        Carrier = File.ReadAllBytes(Path.Combine(directory, "CapsuleQuickJs.bytes"));
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(Carrier)), WasmtimeUiGuest.QuickJsSha256, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Pinned runtime source identity mismatch.");
        Sdk = File.ReadAllBytes(Path.Combine(directory, "CapsuleReactSdk.txt"));
        SdkIdentity = Convert.ToHexString(SHA256.HashData(Sdk));
        Compiled = CompiledCarrier.Read(directory, Carrier);
    }

    public static RuntimeResources Load(string directory)
    {
        string selected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        lock (Gate)
        {
            if (instance == null) instance = new RuntimeResources(selected);
            else if (!string.Equals(instance.directory, selected, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Native resource location is pinned for this process.");
            return instance;
        }
    }
}
