using System;
using System.Collections.Generic;
using System.IO;

namespace Capsule.Package
{
    // Reads one directory revision. No archive extraction, implicit file access or path exposure to guests.
    public static class UiDirectoryPackageLoader
    {
        public const int MaxScriptBytes = 64 * 1024;
        public const int MaxScriptModules = 8;
        public const int MaxScriptTotalBytes = 256 * 1024;
        public const int MaxDataBytes = 64 * 1024;
        public const int MaxImageBytes = 2 * 1024 * 1024;
        public const int MaxTotalBytes = 4 * 1024 * 1024;
        public const int MaxDecodedImageBytes = 32 * 1024 * 1024;

        public static bool TryLoad(string directory, out UiPackageRevision revision, out string error)
            => TryLoad(directory, UiPackageLimits.Community, out revision, out error);

        public static bool TryLoad(string directory, UiPackageLimits limits,
            out UiPackageRevision revision, out string error)
        {
            revision = null; error = null;
            if (limits == null) { error = "Host asset limits missing."; return false; }
            try
            {
                if (string.IsNullOrWhiteSpace(directory))
                { error = "Package directory missing."; return false; }
                string root = Path.GetFullPath(directory);
                var rootInfo = new DirectoryInfo(root);
                if (!rootInfo.Exists || Reparse(rootInfo))
                { error = "Package root missing or reparse point."; return false; }
                if (!TryRead(root, UiPackageManifest.FileName,
                    UiPackageManifest.MaxManifestBytes, out var manifestBytes, out error))
                    return false;
                if (!UiPackageManifest.TryParse(manifestBytes, limits.MaxDeclarations, out var manifest, out error))
                    return false;
                var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { UiPackageManifest.FileName, manifest.EntryPath };
                foreach (var asset in manifest.Declarations) declared.Add(asset.Path);
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var exact = new HashSet<string>(declared, StringComparer.Ordinal);
                var pending = new Stack<string>();
                int directoryCount = 0;
                int discovered = 0;
                pending.Push(root);
                while (pending.Count > 0)
                {
                    string current = pending.Pop();
                    if (++directoryCount > (long)limits.MaxDeclarations + 2)
                    { error = "Package directory count quota exceeded."; return false; }
                    foreach (string child in Directory.EnumerateFileSystemEntries(current))
                    {
                        if (++discovered > (long)limits.MaxDeclarations * 2 + 4)
                        { error = "Package entry count quota exceeded."; return false; }
                        FileAttributes attributes = File.GetAttributes(child);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        { error = "Package contains a reparse point."; return false; }
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (pending.Count + directoryCount >= (long)limits.MaxDeclarations + 2)
                            { error = "Package directory count quota exceeded."; return false; }
                            pending.Push(child); continue;
                        }
                        string relative = child.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
                        if (!UiPackageManifest.ValidPath(relative) || !exact.Contains(relative) ||
                            !found.Add(relative) || found.Count > (long)limits.MaxDeclarations + 2)
                        { error = "Undeclared, invalid or case-colliding package file."; return false; }
                    }
                }
                if (found.Count != declared.Count)
                { error = "Declared package file missing."; return false; }
                int total = manifestBytes.Length;
                if (!TryRead(root, manifest.EntryPath, MaxScriptBytes, out var script, out error))
                    return false;
                total = checked(total + script.Length);
                if (total > limits.MaxTotalBytes)
                { error = "Encoded package quota exceeded."; return false; }
                var data = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                var modules = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                int scriptTotal = script.Length;
                var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                var descriptors = new Dictionary<string, UiPackageImageDescriptor>(
                    StringComparer.OrdinalIgnoreCase);
                int decodedTotal = 0;
                foreach (var asset in manifest.Declarations)
                {
                    int limit = asset.Kind == UiPackageAssetKind.Image ? limits.MaxEncodedImageBytes : MaxDataBytes;
                    if (!TryRead(root, asset.Path, limit, out var bytes, out error)) return false;
                    total = checked(total + bytes.Length);
                    if (total > limits.MaxTotalBytes)
                    { error = "Encoded package quota exceeded."; return false; }
                    if (asset.Kind == UiPackageAssetKind.Script)
                    {
                        scriptTotal = checked(scriptTotal + bytes.Length);
                        if (modules.Count >= MaxScriptModules || scriptTotal > MaxScriptTotalBytes)
                        { error = "Script module count or aggregate source quota exceeded."; return false; }
                        modules.Add(asset.Key, bytes);
                    }
                    else if (asset.Kind == UiPackageAssetKind.Data)
                        data.Add(asset.Key, bytes);
                    else
                    {
                        if (!UiPackageImageDescriptor.TryInspect(bytes, asset.Path, limits, out var descriptor))
                        { error = "Invalid image header or dimensions."; return false; }
                        decodedTotal = checked(decodedTotal + descriptor.DecodedBytes);
                        if (decodedTotal > limits.MaxDecodedImageBytes)
                        { error = "Decoded image quota exceeded."; return false; }
                        images.Add(asset.Key, bytes);
                        descriptors.Add(asset.Key, descriptor);
                    }
                }
                revision = new UiPackageRevision(manifest, manifestBytes, script,
                    data, images, descriptors, modules);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                ex is ArgumentException || ex is OverflowException)
            { error = "Cannot read package: " + ex.Message; return false; }
        }

        static bool Reparse(FileSystemInfo entry) =>
            (entry.Attributes & FileAttributes.ReparsePoint) != 0;

        static bool TryRead(string root, string relative, int maxBytes,
            out byte[] bytes, out string error)
        {
            bytes = null; error = null;
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            { error = "Package path escapes root."; return false; }
            string cursor = root;
            foreach (var segment in relative.Split('/'))
            {
                cursor = Path.Combine(cursor, segment);
                FileSystemInfo info = cursor == full ? (FileSystemInfo)new FileInfo(cursor) :
                    new DirectoryInfo(cursor);
                if (!info.Exists || Reparse(info))
                { error = "Package path missing or reparse point."; return false; }
            }
            var file = new FileInfo(full);
            if (file.Length < 0 || file.Length > maxBytes)
            { error = "Package file byte quota exceeded."; return false; }
            using (var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long openedLength = stream.Length;
                if (openedLength < 0 || openedLength > maxBytes)
                { error = "Opened package file byte quota exceeded."; return false; }
                bytes = new byte[(int)openedLength];
                int at = 0;
                while (at < bytes.Length)
                {
                    int read = stream.Read(bytes, at, bytes.Length - at);
                    if (read <= 0) { error = "Package file changed during read."; return false; }
                    at += read;
                }
                if (stream.ReadByte() >= 0)
                { error = "Package file grew during read."; return false; }
            }
            return true;
        }
    }
}
