using System;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Capsule.Unity.Development
{
    // Hashes are generated from the shipped package payloads. The editor uses
    // the same manifest for native DLL readiness without embedding local paths.
    public static class CapsuleDevelopmentCompatibility
    {
        public const string CarrierPath = "Packages/com.capsule.wasmtime/Runtime/Carrier/CapsuleQuickJs.bytes";
        public const string ReactSdkPath = "Packages/com.capsule.wasmtime/Runtime/Resources/CapsuleReactSdk.txt";
        const string ManifestResource = "CapsuleDevelopmentCompatibility";

        public static bool TryExpectedHash(string relativePackageAssetPath, out string sha256)
        {
            sha256 = null;
            if (string.IsNullOrEmpty(relativePackageAssetPath)) return false;
            var document = Resources.Load<TextAsset>(ManifestResource);
            if (!document) return false;
            try
            {
                var data = JObject.Parse(document.text);
                if ((int?)data["schemaVersion"] != 1 || !(data["sha256"] is JObject hashes)) return false;
                string value = (string)hashes[relativePackageAssetPath];
                if (!Hex64(value)) return false;
                sha256 = value;
                return true;
            }
            catch { return false; }
        }

        public static bool Validate(TextAsset carrier, TextAsset sdk, out string error)
        {
            error = null;
            if (!carrier || !sdk) { error = "Carrier and React SDK assets are required."; return false; }
            if (!TryExpectedHash(CarrierPath, out var carrierHash) || !TryExpectedHash(ReactSdkPath, out var sdkHash))
            { error = "Capsule development compatibility manifest is missing or invalid."; return false; }
            if (!string.Equals(Hash(carrier.bytes), carrierHash, StringComparison.OrdinalIgnoreCase))
            { error = "Assigned carrier differs from the shipped Capsule carrier."; return false; }
            if (!string.Equals(Hash(sdk.bytes), sdkHash, StringComparison.OrdinalIgnoreCase))
            { error = "Assigned React SDK differs from the shipped Capsule SDK."; return false; }
            return true;
        }

        static bool Hex64(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
        static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
    }
}
