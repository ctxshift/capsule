using System;
using System.Collections.Generic;

namespace Capsule
{
    // Copy key strings on the main thread; no asset/native object is retained.
    public sealed class UiThemeSnapshot : IUiTheme
    {
        readonly HashSet<string> styles, assets;
        public UiThemeSnapshot(IEnumerable<string> styleKeys, IEnumerable<string> assetKeys)
        {
            styles = new HashSet<string>(styleKeys, StringComparer.Ordinal);
            assets = new HashSet<string>(assetKeys, StringComparer.Ordinal);
        }
        public bool AllowsStyle(string key) => string.IsNullOrEmpty(key) || styles.Contains(key);
        public bool AllowsAsset(string key) => string.IsNullOrEmpty(key) || assets.Contains(key);
    }
}
