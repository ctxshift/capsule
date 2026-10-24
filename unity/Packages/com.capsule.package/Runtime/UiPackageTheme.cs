using System;
using System.Collections.Generic;
using System.Text;

namespace Capsule.Package
{
    // A host chooses the provider. Only its explicitly declared theme images
    // become public names; no provider paths are exposed to consuming guests.
    public sealed class UiPackageTheme
    {
        readonly List<string> names = new List<string>();
        public UiControlTheme Definition { get; }
        public IReadOnlyList<string> PublicImages => names.AsReadOnly();

        public IReadOnlyList<string> ImageKeysFor(UiPackageLease consumer)
        {
            var keys = new List<string>(names);
            foreach (var asset in consumer.Revision.Manifest.Declarations)
                if (asset.Kind == UiPackageAssetKind.Image)
                {
                    if (!consumer.TryAcquireImageKey(asset.Key, out var key, out var error))
                        throw new InvalidOperationException(error);
                    keys.Add(key);
                }
            return keys.AsReadOnly();
        }

        public UiPackageTheme(UiPackageRevision revision, string themeKey) : this(revision,themeKey,null) { }
        public UiPackageTheme(UiPackageRevision revision, string themeKey, UiThemeLimits? limits)
        {
            if (!revision.TryCopyData(themeKey, out var bytes))
                throw new ArgumentException("Package theme data is not declared.");
            Definition = new UiControlTheme(new UTF8Encoding(false, true).GetString(bytes), limits);
            var sources = new Dictionary<string, UiControlTheme.AssetRecipe>(StringComparer.Ordinal);
            foreach (var recipe in Definition.CopyAssets())
            {
                if (sources.TryGetValue(recipe.ImageKey, out var previous) &&
                    (previous.Path != recipe.Path || previous.Width != recipe.Width || previous.Height != recipe.Height || previous.AlphaAware != recipe.AlphaAware))
                    throw new ArgumentException("Conflicting theme image source.");
                sources[recipe.ImageKey] = recipe;
                bool found = false;
                foreach (var declaration in revision.Manifest.Declarations)
                    if (declaration.Key == recipe.ImageKey && declaration.Path == recipe.Path && declaration.Kind == UiPackageAssetKind.Image)
                        found = true;
                if (!found || names.Contains(recipe.Key) ||
                    !revision.TryCopyImage(recipe.ImageKey, out _, out var image) ||
                    image.Width != recipe.Width || image.Height != recipe.Height ||
                    recipe.X < 0 || recipe.Y < 0 || recipe.RectWidth < 1 || recipe.RectHeight < 1 || recipe.Border < 0 ||
                    2L * recipe.Border > Math.Min(recipe.RectWidth, recipe.RectHeight) ||
                    (long)recipe.X + recipe.RectWidth > image.Width || (long)recipe.Y + recipe.RectHeight > image.Height)
                    throw new ArgumentException("Theme recipe does not match a declared package image.");
                names.Add(recipe.Key);
            }
        }
    }
}
