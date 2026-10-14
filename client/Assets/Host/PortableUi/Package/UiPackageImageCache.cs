using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace PortableUi.Package
{
    // Platform adapters decode pixels and own the resulting native resource.
    public interface IUiDecodedImage : IDisposable
    {
        int Width { get; }
        int Height { get; }
        long ResidentBytes { get; }
    }

    public interface IUiImageDecoder
    {
        IUiDecodedImage Decode(byte[] encoded, UiPackageImageDescriptor expected);
    }

    public readonly struct UiPackageImageHandle : IEquatable<UiPackageImageHandle>
    {
        public readonly long Generation;
        public readonly int Slot;
        internal UiPackageImageHandle(long generation, int slot)
        { Generation = generation; Slot = slot; }
        public bool Equals(UiPackageImageHandle other) =>
            Generation == other.Generation && Slot == other.Slot;
        public override bool Equals(object obj) =>
            obj is UiPackageImageHandle other && Equals(other);
        public override int GetHashCode() => Generation.GetHashCode() ^ Slot;
    }

    public sealed class UiPackageImageCache : IDisposable
    {
        sealed class Entry
        {
            public IUiDecodedImage Image;
            public int References;
        }

        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly IUiImageDecoder decoder;
        public const long DefaultMaxResidentBytes = 64L * 1024 * 1024;
        readonly long maxResidentBytes;
        long nextGeneration;
        long residentBytes;
        bool disposed;
        public int CachedImageCount => entries.Count;
        internal bool IsDisposed => disposed;

        public UiPackageImageCache(IUiImageDecoder decoder, long maxResidentBytes = DefaultMaxResidentBytes)
        {
            this.decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
            if (maxResidentBytes < 4) throw new ArgumentOutOfRangeException(nameof(maxResidentBytes));
            this.maxResidentBytes = maxResidentBytes;
        }

        public UiPackageLease Open(UiPackageRevision revision)
        {
            if (disposed) throw new ObjectDisposedException(nameof(UiPackageImageCache));
            return new UiPackageLease(this, revision ?? throw new ArgumentNullException(nameof(revision)),
                ++nextGeneration);
        }

        internal bool TryAcquire(UiPackageRevision revision, string key,
            out string cacheKey, out IUiDecodedImage image, out string error)
        {
            cacheKey = null; image = null; error = null;
            if (disposed) { error = "Image cache disposed."; return false; }
            if (!revision.TryCopyImage(key, out var bytes, out var descriptor))
            { error = "Image key not declared."; return false; }
            using (var sha = SHA256.Create())
                cacheKey = revision.Manifest.Id + "/" + key.ToLowerInvariant() + "/" +
                    BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            if (entries.TryGetValue(cacheKey, out var existing))
            {
                existing.References++;
                image = existing.Image;
                return true;
            }
            // Reserve against the inspected RGBA size before asking a native decoder
            // to allocate. Actual reported resident size is checked again below.
            if (descriptor.DecodedBytes > maxResidentBytes - residentBytes)
            { error = "Decoded image cache quota exceeded."; return false; }
            IUiDecodedImage decoded = null;
            try
            {
                decoded = decoder.Decode(bytes, descriptor);
                if (decoded == null || decoded.Width != descriptor.Width ||
                    decoded.Height != descriptor.Height || decoded.ResidentBytes < 0 ||
                    decoded.ResidentBytes > descriptor.DecodedBytes)
                { error = "Image decoder returned invalid dimensions or memory size."; return false; }
                if (decoded.ResidentBytes > maxResidentBytes - residentBytes)
                { error = "Decoded image cache quota exceeded."; return false; }
                entries.Add(cacheKey, new Entry { Image = decoded, References = 1 });
                residentBytes += decoded.ResidentBytes;
                image = decoded;
                decoded = null;
                return true;
            }
            catch (Exception ex)
            { error = "Image decode failed: " + ex.Message; return false; }
            finally { decoded?.Dispose(); }
        }

        internal void Release(string key)
        {
            if (!entries.TryGetValue(key, out var entry)) return;
            if (--entry.References == 0)
            {
                entries.Remove(key);
                residentBytes -= entry.Image.ResidentBytes;
                entry.Image.Dispose();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var entry in entries.Values) entry.Image.Dispose();
            entries.Clear();
            residentBytes = 0;
        }
    }

    public sealed class UiPackageLease : IDisposable
    {
        readonly UiPackageImageCache cache;
        readonly Dictionary<string, int> slotsByKey =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<int, string> keysBySlot = new Dictionary<int, string>();
        readonly Dictionary<string, int> slotsByAssetKey =
            new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<int, IUiDecodedImage> imagesBySlot =
            new Dictionary<int, IUiDecodedImage>();
        int nextSlot;
        bool disposed;
        public UiPackageRevision Revision { get; }
        public long Generation { get; }

        internal UiPackageLease(UiPackageImageCache cache, UiPackageRevision revision, long generation)
        { this.cache = cache; Revision = revision; Generation = generation; }

        // Candidates validate every declared image, including images not referenced
        // by the initial UI. The host disposes this lease when preparation fails.
        public bool TryPrepareImages(out string error)
        {
            error = null;
            if (disposed || cache.IsDisposed)
            { error = "Package generation disposed."; return false; }
            foreach (var declaration in Revision.Manifest.Declarations)
                if (declaration.Kind == UiPackageAssetKind.Image &&
                    !TryAcquireImage(declaration.Key, out _, out error)) return false;
            return true;
        }

        public bool TryAcquireImage(string key, out UiPackageImageHandle handle, out string error)
        {
            handle = default; error = null;
            if (disposed || cache.IsDisposed)
            { error = "Package generation disposed."; return false; }
            if (key != null && slotsByKey.TryGetValue(key, out int oldSlot))
            { handle = new UiPackageImageHandle(Generation, oldSlot); return true; }
            if (nextSlot >= Revision.Manifest.Declarations.Count)
            { error = "Image handle quota exceeded."; return false; }
            if (!cache.TryAcquire(Revision, key, out var cacheKey, out var image, out error))
                return false;
            int slot = ++nextSlot;
            slotsByKey.Add(key, slot);
            keysBySlot.Add(slot, cacheKey);
            imagesBySlot.Add(slot, image);
            handle = new UiPackageImageHandle(Generation, slot);
            return true;
        }

        public bool TryResolve(UiPackageImageHandle handle, out IUiDecodedImage image)
        {
            image = null;
            return !disposed && !cache.IsDisposed && handle.Generation == Generation &&
                imagesBySlot.TryGetValue(handle.Slot, out image);
        }

        public bool TryCopyData(string key, out byte[] bytes)
        {
            bytes = null;
            return !disposed && !cache.IsDisposed && Revision.TryCopyData(key, out bytes);
        }

        // Wire-friendly opaque token. Its text is an ABI key, not a filesystem path.
        public bool TryAcquireImageKey(string key, out string assetKey, out string error)
        {
            assetKey = null;
            if (!TryAcquireImage(key, out var handle, out error)) return false;
            assetKey = "package_" + handle.Generation.ToString("x16") + "_" +
                handle.Slot.ToString("x8");
            slotsByAssetKey[assetKey] = handle.Slot;
            return true;
        }

        public bool TryResolveImageKey(string assetKey, out IUiDecodedImage image)
        {
            image = null;
            return !disposed && !cache.IsDisposed && assetKey != null &&
                slotsByAssetKey.TryGetValue(assetKey, out int slot) &&
                imagesBySlot.TryGetValue(slot, out image);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var key in keysBySlot.Values) cache.Release(key);
            slotsByKey.Clear();
            keysBySlot.Clear();
            slotsByAssetKey.Clear();
            imagesBySlot.Clear();
        }
    }
}
