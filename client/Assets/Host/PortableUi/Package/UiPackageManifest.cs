using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PortableUi.Package
{
    public enum UiPackageAssetKind { Data, Image }

    public sealed class UiPackageDeclaration
    {
        public UiPackageAssetKind Kind { get; }
        public string Key { get; }
        public string Path { get; }
        internal UiPackageDeclaration(UiPackageAssetKind kind, string key, string path)
        { Kind = kind; Key = key; Path = path; }
    }

    public sealed class UiPackageManifest
    {
        public const string FileName = "addon.json";
        public const int SchemaVersion = 1;
        public const int ApiVersion = 1;
        public const int MaxManifestBytes = 16 * 1024;
        public const int MaxAssets = 30;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        readonly IReadOnlyList<UiPackageDeclaration> declarations;
        public string Id { get; }
        public string EntryPath { get; }
        public IReadOnlyList<UiPackageDeclaration> Declarations => declarations;

        UiPackageManifest(string id, string entry, List<UiPackageDeclaration> assets)
        { Id = id; EntryPath = entry; declarations = assets.AsReadOnly(); }

        public static bool TryParse(byte[] bytes, out UiPackageManifest manifest, out string error)
            => TryParse(bytes,MaxAssets,out manifest,out error);
        public static bool TryParse(byte[] bytes, int maxAssets, out UiPackageManifest manifest, out string error)
        {
            manifest = null; error = null;
            if(maxAssets<1 || maxAssets>64) { error="Host manifest profile bounds.";return false; }
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxManifestBytes)
            { error = "Manifest must be 1..16384 bytes."; return false; }
            string source;
            try { source = Utf8.GetString(bytes); }
            catch (DecoderFallbackException) { error = "Manifest is not strict UTF-8."; return false; }
            if (source.Length > 0 && source[0] == '\uFEFF') source = source.Substring(1);
            if (!StandardJson(source))
            { error = "Manifest must use JSON without comments or trailing commas."; return false; }
            JObject root;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(source))
                {
                    MaxDepth = 8,
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Decimal,
                    SupportMultipleContent = false
                })
                {
                    var token = JToken.ReadFrom(reader, new JsonLoadSettings
                    { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) throw new JsonReaderException("Trailing JSON content.");
                    root = token as JObject;
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is ArgumentException || ex is OverflowException)
            { error = "Invalid manifest JSON: " + ex.Message; return false; }
            if (root == null || !HasOnly(root, "schemaVersion", "apiVersion", "id", "entry", "assets") ||
                !Integer(root, "schemaVersion", SchemaVersion) || !Integer(root, "apiVersion", ApiVersion))
            { error = "Invalid manifest schema, API version or fields."; return false; }
            var id = String(root, "id");
            var entry = String(root, "entry");
            var assetArray = root["assets"] as JArray;
            if (!ValidKey(id) || !ValidPath(entry) ||
                !entry.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                    assetArray == null || assetArray.Count > maxAssets)
            { error = "Invalid identity, entry path or asset count."; return false; }
            var assets = new List<UiPackageDeclaration>(assetArray.Count);
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { FileName, entry };
            foreach (var value in assetArray)
            {
                var item = value as JObject;
                if (item == null || !HasOnly(item, "key", "kind", "path"))
                { error = "Invalid asset declaration fields."; return false; }
                var key = String(item, "key");
                var path = String(item, "path");
                var kindText = String(item, "kind");
                UiPackageAssetKind kind;
                if (kindText == "data") kind = UiPackageAssetKind.Data;
                else if (kindText == "image") kind = UiPackageAssetKind.Image;
                else { error = "Unsupported asset kind."; return false; }
                if (!ValidKey(key) || !ValidPath(path) || !keys.Add(key) || !paths.Add(path))
                { error = "Invalid, duplicate or case-colliding asset declaration."; return false; }
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (kind == UiPackageAssetKind.Data && ext != ".json" && ext != ".csv" &&
                    ext != ".txt" && ext != ".toml" ||
                    kind == UiPackageAssetKind.Image && ext != ".png" && ext != ".jpg" && ext != ".jpeg")
                { error = "Unsupported asset extension."; return false; }
                assets.Add(new UiPackageDeclaration(kind, key, path));
            }
            manifest = new UiPackageManifest(id, entry, assets);
            return true;
        }

        static bool HasOnly(JObject obj, params string[] expected)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in obj.Properties())
            {
                if (!seen.Add(property.Name)) return false;
                bool known = false;
                foreach (var name in expected)
                    if (property.Name == name) { known = true; break; }
                if (!known) return false;
            }
            foreach (var name in expected)
                if (obj.Property(name, StringComparison.Ordinal) == null) return false;
            return true;
        }

        static bool StandardJson(string source)
        {
            bool quoted = false, escaped = false;
            char last = '\0';
            foreach (char c in source)
            {
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (c == '"') { quoted = true; last = '"'; continue; }
                if (c == '/' || c == '\'' || char.IsLetter(c) ||
                    (c == '}' || c == ']') && last == ',') return false;
                if (!char.IsWhiteSpace(c)) last = c;
            }
            return !quoted;
        }

        static string String(JObject obj, string name) =>
            obj[name]?.Type == JTokenType.String ? (string)obj[name] : null;

        static bool Integer(JObject obj, string name, int expected) =>
            obj[name]?.Type == JTokenType.Integer &&
            obj[name].ToString(Formatting.None) == expected.ToString(System.Globalization.CultureInfo.InvariantCulture);

        internal static bool ValidKey(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 96 ||
                value[0] == '.' || value[0] == '-') return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != '.' && c != '_' && c != '-') return false;
            return true;
        }

        internal static bool ValidPath(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 240 ||
                value[0] == '/' || value.IndexOf('\\') >= 0 || value.IndexOf(':') >= 0 ||
                value.IndexOf('\0') >= 0 || Path.IsPathRooted(value)) return false;
            foreach (var segment in value.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." ||
                    segment[segment.Length - 1] == '.' || segment[segment.Length - 1] == ' ')
                    return false;
                string stem = segment.Split('.')[0].ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                    stem.Length == 4 && ((stem.StartsWith("COM", StringComparison.Ordinal) ||
                    stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] >= '1' && stem[3] <= '9'))
                    return false;
                foreach (char c in segment)
                    if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                        !(c >= '0' && c <= '9') && c != '.' && c != '_' && c != '-') return false;
            }
            return true;
        }
    }
}
