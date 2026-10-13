using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PortableUi.Package
{
    // Immutable host-owned provider data. Untrusted packages can only derive a
    // private palette/geometry scope; they cannot register a default provider.
    public sealed class UiControlTheme
    {
        readonly JObject value;
        public UiControlTheme(string json)
        {
            if (json == null || json.Length > 65536) throw new ArgumentException("Theme data quota.");
            using (var reader = new JsonTextReader(new System.IO.StringReader(json)) { MaxDepth = 8 })
                value = JObject.Load(reader);
            if ((int?)value["schemaVersion"] != 1 || value["id"]?.Type != JTokenType.String)
                throw new ArgumentException("Invalid theme identity.");
            Validate(value, 0);
            foreach (string group in new[] { "font", "components", "window", "button", "range", "select", "focus" })
                if (!(value[group] is JObject)) throw new ArgumentException("Missing theme group: " + group);
            if (!(value["assets"] is JArray assets) || assets.Count > 30 || !(value["text"] is JArray text) || text.Count > 16)
                throw new ArgumentException("Theme collection quota.");
        }
        UiControlTheme(JObject source) { value = source; }
        static void Validate(JToken token, int depth)
        {
            if (depth > 8) throw new ArgumentException("Theme depth quota.");
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            { double n = (double)token; if (double.IsNaN(n) || double.IsInfinity(n) || Math.Abs(n) > 8192) throw new ArgumentException("Theme number bounds."); }
            else if (token.Type == JTokenType.String)
            { string s = (string)token; if (s.Length > 128 || s.IndexOfAny(new[] {'\r','\n','\\','\0'}) >= 0) throw new ArgumentException("Theme string bounds."); }
            foreach (var child in token.Children()) Validate(child, depth + 1);
        }
        public UiControlTheme WithOverrides(string json)
        {
            if (json == null || json.Length > 16384) throw new ArgumentException("Scoped theme quota.");
            JObject overrides;
            using (var reader = new JsonTextReader(new System.IO.StringReader(json)) { MaxDepth = 6 }) overrides = JObject.Load(reader);
            Validate(overrides, 0);
            var copy = (JObject)value.DeepClone();
            foreach (var property in overrides.Properties())
            {
                if (Array.IndexOf(new[] { "window", "button", "range", "select", "focus" }, property.Name) < 0 || !(property.Value is JObject group))
                    throw new ArgumentException("Unknown scoped theme group: " + property.Name);
                foreach (var item in group.Properties())
                {
                    var original = copy[property.Name][item.Name];
                    if (original == null || original.Type == JTokenType.String || original.Type != item.Value.Type && !IsNumber(original, item.Value))
                        throw new ArgumentException("Unsupported scoped theme token: " + item.Name);
                    if (original is JArray color && (color.Count != 4 || !(item.Value is JArray channels) || channels.Count != 4))
                        throw new ArgumentException("Scoped color bounds.");
                    if (item.Value is JArray rgba) foreach (var channel in rgba) if (channel.Type != JTokenType.Integer || (int)channel < 0 || (int)channel > 255) throw new ArgumentException("Scoped color bounds.");
                    if(IsNumber(original,item.Value))
                    {
                        double n=(double)item.Value;
                        bool signed=property.Name=="focus"&&item.Name=="inset"||property.Name=="window"&&item.Name=="shadowOffset";
                        if(Math.Abs(n)>4096||!signed&&n<0||item.Name.EndsWith("Opacity",StringComparison.Ordinal)&&n>1)throw new ArgumentException("Scoped geometry bounds.");
                    }
                    copy[property.Name][item.Name] = item.Value.DeepClone();
                }
            }
            return new UiControlTheme(copy);
        }
        static bool IsNumber(JToken a, JToken b) => (a.Type == JTokenType.Integer || a.Type == JTokenType.Float) && (b.Type == JTokenType.Integer || b.Type == JTokenType.Float);
        public float Number(string group, string key) => (float)(value[group]?[key] ?? throw new ArgumentException("Missing numeric theme token: " + group + "." + key));
        public string String(string group, string key) => (string)(value[group]?[key] ?? throw new ArgumentException("Missing string theme token: " + group + "." + key));
        public byte[] Color(string group, string key)
        {
            if (!(value[group]?[key] is JArray rgba) || rgba.Count != 4) throw new ArgumentException("Missing theme color.");
            var result = new byte[4]; for (int i=0;i<4;++i) { int c=(int)rgba[i]; if(c<0||c>255)throw new ArgumentException("Theme color bounds.");result[i]=(byte)c; } return result;
        }
        public IEnumerable<string> TextKeys { get { foreach (var item in (JArray)value["text"]) yield return (string)item["key"]; } }
        public IEnumerable<string> StyleKeys {get{foreach(string key in new[]{"button","buttonLabel","fieldLabel","slider","select","paragraph"})yield return String("components",key);foreach(string key in TextKeys)yield return key;}}
        public float TextNumber(string key,string property) { foreach(var item in (JArray)value["text"])if((string)item["key"]==key)return(float)item[property];throw new ArgumentException("Unknown text style."); }
        public bool TextWrap(string key) { foreach(var item in (JArray)value["text"])if((string)item["key"]==key)return(bool)item["wrap"];throw new ArgumentException("Unknown text style."); }
        public byte[] TextColor(string key) { foreach(var item in (JArray)value["text"])if((string)item["key"]==key){var c=(JArray)item["color"];return new[]{(byte)c[0],(byte)c[1],(byte)c[2],(byte)c[3]};}throw new ArgumentException("Unknown text style."); }
        public float AssetNumber(string key,string property) {foreach(var item in (JArray)value["assets"])if((string)item["key"]==key)return(float)item[property];throw new ArgumentException("Unknown theme asset.");}
        public bool AssetAlphaAware(string key) {foreach(var item in (JArray)value["assets"])if((string)item["key"]==key)return(bool)item["alphaAware"];throw new ArgumentException("Unknown theme asset.");}
        public sealed class AssetRecipe
        { public string Key,ImageKey,Path; public int Width,Height,X,Y,RectWidth,RectHeight,Border,Edge; public bool AlphaAware; }
        public IEnumerable<AssetRecipe> CopyAssets()
        {
            foreach(var item in (JArray)value["assets"])
            {
                string path=(string)item["path"],key=(string)item["key"];
                if(string.IsNullOrEmpty(path)||System.IO.Path.IsPathRooted(path)||path.Contains("..")||path.Contains("\\")||string.IsNullOrEmpty(key)||key.IndexOfAny(new[]{'/','\\','.'})>=0)throw new ArgumentException("Invalid theme asset path.");
                var rect=(JArray)item["rect"];
                string imageKey = item["image"] == null ? key : (string)item["image"];
                if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z0-9-]{1,64}$") ||
                    string.IsNullOrEmpty(imageKey) || !System.Text.RegularExpressions.Regex.IsMatch(imageKey, "^[a-z0-9-]{1,64}$"))
                    throw new ArgumentException("Invalid theme image key.");
                yield return new AssetRecipe{Key=key,ImageKey=imageKey,Path=path,Width=(int)item["width"],Height=(int)item["height"],X=(int)rect[0],Y=(int)rect[1],RectWidth=(int)rect[2],RectHeight=(int)rect[3],Border=(int)item["border"],Edge=(int)item["edge"],AlphaAware=(bool)item["alphaAware"]};
            }
        }
    }
}
