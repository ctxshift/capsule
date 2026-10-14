using System;
using System.Collections.Generic;

namespace PortableUi
{
    // Host-only immutable admission. Keys carry no native object or asset path.
    public sealed class UiSurfaceAdmission
    {
        public static readonly UiSurfaceAdmission None = new UiSurfaceAdmission();
        readonly HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
        public int MaxLeases { get; }
        UiSurfaceAdmission() { }
        public UiSurfaceAdmission(IEnumerable<string> surfaceKeys, int maxLeases = 1)
        {
            if (surfaceKeys == null) throw new ArgumentNullException(nameof(surfaceKeys));
            if (maxLeases < 1 || maxLeases > 4) throw new ArgumentOutOfRangeException(nameof(maxLeases));
            foreach (string key in surfaceKeys)
            {
                if (!ValidKey(key) || !keys.Add(key) || keys.Count > 4)
                    throw new ArgumentException("Invalid, duplicate or excessive surface keys.");
            }
            if (keys.Count == 0) throw new ArgumentException("Surface keys required.");
            MaxLeases = maxLeases;
        }
        public bool Allows(string key) => key != null && keys.Contains(key);
        static bool ValidKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 64) return false;
            foreach (char c in key)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') &&
                    c != '.' && c != '_' && c != '-') return false;
            return true;
        }
    }
}
