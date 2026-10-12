using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace PortableUi.Package
{
    // All bytes are private copies. A revision can be retained while a replacement is validated.
    public sealed class UiPackageRevision
    {
        readonly byte[] script;
        readonly Dictionary<string, byte[]> data;
        readonly Dictionary<string, byte[]> images;
        readonly Dictionary<string, UiPackageImageDescriptor> descriptors;
        public UiPackageManifest Manifest { get; }
        public string ContentHash { get; }
        public string Identity => Manifest.Id + ":" + ContentHash;

        internal UiPackageRevision(UiPackageManifest manifest, byte[] manifestBytes,
            byte[] entry, Dictionary<string, byte[]> dataBytes,
            Dictionary<string, byte[]> imageBytes,
            Dictionary<string, UiPackageImageDescriptor> imageDescriptors)
        {
            Manifest = manifest;
            script = (byte[])entry.Clone();
            data = Clone(dataBytes);
            images = Clone(imageBytes);
            descriptors = new Dictionary<string, UiPackageImageDescriptor>(imageDescriptors,
                StringComparer.OrdinalIgnoreCase);
            using (var sha = SHA256.Create())
            {
                var ordered = new List<KeyValuePair<string, byte[]>>
                {
                    new KeyValuePair<string, byte[]>(UiPackageManifest.FileName, manifestBytes),
                    new KeyValuePair<string, byte[]>(manifest.EntryPath, script)
                };
                foreach (var asset in manifest.Declarations)
                {
                    byte[] bytes = asset.Kind == UiPackageAssetKind.Data ?
                        data[asset.Key] : images[asset.Key];
                    ordered.Add(new KeyValuePair<string, byte[]>(asset.Path, bytes));
                }
                ordered.Sort((a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
                foreach (var pair in ordered)
                {
                    byte[] name = Encoding.UTF8.GetBytes(pair.Key);
                    HashPart(sha, name);
                    HashPart(sha, pair.Value);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                ContentHash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        static void HashPart(HashAlgorithm sha, byte[] bytes)
        {
            byte[] length = BitConverter.GetBytes(bytes.Length);
            sha.TransformBlock(length, 0, length.Length, length, 0);
            sha.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
        }

        static Dictionary<string, byte[]> Clone(Dictionary<string, byte[]> source)
        {
            var copy = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in source) copy.Add(pair.Key, (byte[])pair.Value.Clone());
            return copy;
        }

        public byte[] CopyScript() => (byte[])script.Clone();
        public bool TryCopyData(string key, out byte[] bytes)
        {
            bytes = null;
            if (key == null || !data.TryGetValue(key, out var stored)) return false;
            bytes = (byte[])stored.Clone();
            return true;
        }
        public bool TryCopyImage(string key, out byte[] bytes, out UiPackageImageDescriptor descriptor)
        {
            bytes = null; descriptor = null;
            if (key == null || !images.TryGetValue(key, out var stored)) return false;
            bytes = (byte[])stored.Clone();
            descriptor = descriptors[key];
            return true;
        }
    }
}
