using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PortableUi.Package
{
    // Selected by trusted host composition, never by fields inside a theme.
    public readonly struct UiThemeLimits
    {
        public readonly int AssetRecipes, TextStyles;
        public static readonly UiThemeLimits Default = new UiThemeLimits(30,16);
        public UiThemeLimits(int assetRecipes,int textStyles)
        {
            if(assetRecipes<1 || assetRecipes>64 || textStyles<1 || textStyles>64)
                throw new ArgumentOutOfRangeException();
            AssetRecipes=assetRecipes;TextStyles=textStyles;
        }
    }
    // Immutable host-owned provider data. Untrusted packages can only derive a
    // private palette/geometry scope; they cannot register a default provider.
    public sealed class UiControlTheme
    {
        readonly JObject value;
        public UiControlTheme(string json) : this(json,null) { }
        public UiControlTheme(string json, UiThemeLimits? limits)
        {
            if (json == null || json.Length > 65536) throw new ArgumentException("Theme data quota.");
            using (var reader = new JsonTextReader(new System.IO.StringReader(json)) { MaxDepth = 8 })
                value = JObject.Load(reader);
            if ((int?)value["schemaVersion"] != 1 || value["id"]?.Type != JTokenType.String)
                throw new ArgumentException("Invalid theme identity.");
            Validate(value, 0);
            foreach (string group in new[] { "font", "components", "window", "button", "range", "select", "focus" })
                if (!(value[group] is JObject)) throw new ArgumentException("Missing theme group: " + group);
            var capacity=limits ?? UiThemeLimits.Default;
            if (!(value["assets"] is JArray assets) || assets.Count > capacity.AssetRecipes || !(value["text"] is JArray text) || text.Count > capacity.TextStyles)
                throw new ArgumentException("Theme collection quota.");
            foreach(var asset in CopyAssets()) { }
            if (value["buttonStyles"] != null)
            {
                if (!(value["buttonStyles"] is JArray buttons) || buttons.Count > 16) throw new ArgumentException("Theme button style quota.");
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var button in buttons)
                {
                    string key = (string)button["key"];
                    if (string.IsNullOrEmpty(key) || !System.Text.RegularExpressions.Regex.IsMatch(key,"^[a-z0-9-]{1,64}$") || !keys.Add(key) || key == String("components","button")) throw new ArgumentException("Theme button style bounds.");
                    ValidateColor(button["selectedTint"]);
                    if ((button["selectedTextureOpacity"]?.Type != JTokenType.Integer && button["selectedTextureOpacity"]?.Type != JTokenType.Float) || (float)button["selectedTextureOpacity"] < 0 || (float)button["selectedTextureOpacity"] > 1) throw new ArgumentException("Theme button texture opacity bounds.");
                }
            }
            foreach(var style in text)
            {
                if(style["smallCaps"]!=null && style["smallCaps"].Type!=JTokenType.Boolean)throw new ArgumentException("Theme smallcaps bounds.");
                if(style["letterSpacing"]!=null && (style["letterSpacing"].Type!=JTokenType.Integer && style["letterSpacing"].Type!=JTokenType.Float || float.IsNaN((float)style["letterSpacing"]) || (float)style["letterSpacing"]<0 || (float)style["letterSpacing"]>8))throw new ArgumentException("Theme letter spacing bounds.");
                if(style["ruleInset"]!=null && (style["ruleInset"].Type!=JTokenType.Integer && style["ruleInset"].Type!=JTokenType.Float || (float)style["ruleInset"]<0 || (float)style["ruleInset"]>256))throw new ArgumentException("Theme window rule inset bounds.");
                if(style["ruleColor"]!=null)
                {
                    if(!(style["ruleColor"] is JArray c) || c.Count!=4)throw new ArgumentException("Theme window rule color bounds.");
                    foreach(var channel in c)if(channel.Type!=JTokenType.Integer || (int)channel<0 || (int)channel>255)throw new ArgumentException("Theme window rule color bounds.");
                }
                if(style["weight"]!=null && (style["weight"].Type!=JTokenType.Integer || ((int)style["weight"]!=400 && (int)style["weight"]!=700)))
                    throw new ArgumentException("Theme text weight bounds.");
            }
            if(value["surfaces"] != null)
            {
                if(!(value["surfaces"] is JArray surfaces) || surfaces.Count > 30) throw new ArgumentException("Theme surface quota.");
                var keys=new HashSet<string>(StringComparer.Ordinal);
                foreach(var surface in surfaces)
                {
                    string key=(string)surface["key"];
                    if(string.IsNullOrEmpty(key) || !System.Text.RegularExpressions.Regex.IsMatch(key,"^[a-z0-9-]{1,64}$") || !keys.Add(key) ||
                        !(surface["color"] is JArray rgba) || rgba.Count!=4) throw new ArgumentException("Theme surface bounds.");
                    foreach(var channel in rgba) if(channel.Type!=JTokenType.Integer || (int)channel<0 || (int)channel>255) throw new ArgumentException("Theme surface bounds.");
                    if (surface["rules"] != null)
                    {
                        if (!(surface["rules"] is JObject rules) || rules.Count > 4) throw new ArgumentException("Theme surface rule quota.");
                        foreach (var property in rules.Properties())
                        {
                            if (Array.IndexOf(new[] { "top", "right", "bottom", "left" }, property.Name) < 0 || !(property.Value is JArray rule) || rule.Count != 5 ||
                                (rule[0].Type != JTokenType.Integer && rule[0].Type != JTokenType.Float) || (float)rule[0] <= 0 || (float)rule[0] > 8)
                                throw new ArgumentException("Theme surface rule bounds.");
                            for (int i=1;i<5;i++) if(rule[i].Type!=JTokenType.Integer || (int)rule[i]<0 || (int)rule[i]>255) throw new ArgumentException("Theme surface rule color bounds.");
                        }
                    }
                    if(surface["shadow"]!=null)
                    {
                        var shadow=surface["shadow"] as JObject;
                        if(shadow==null || (shadow["blur"]?.Type!=JTokenType.Float && shadow["blur"]?.Type!=JTokenType.Integer) ||
                            (float)shadow["blur"]<0 || (float)shadow["blur"]>32 || !(shadow["color"] is JArray tint) || tint.Count!=4)
                            throw new ArgumentException("Theme surface shadow bounds.");
                        foreach(var channel in tint)if(channel.Type!=JTokenType.Integer || (int)channel<0 || (int)channel>255)throw new ArgumentException("Theme surface shadow bounds.");
                    }
                }
            }
        }
        UiControlTheme(JObject source) { value = source; }
        static void ValidateColor(JToken token)
        {
            if (!(token is JArray color) || color.Count != 4) throw new ArgumentException("Theme color bounds.");
            foreach (var channel in color) if (channel.Type != JTokenType.Integer || (int)channel < 0 || (int)channel > 255) throw new ArgumentException("Theme color bounds.");
        }
        public bool TryButtonSelectedTint(string key, out byte[] color)
        {
            if (value["buttonStyles"] is JArray styles) foreach (var style in styles) if ((string)style["key"] == key)
            { var rgba = (JArray)style["selectedTint"]; color = new[] { (byte)rgba[0], (byte)rgba[1], (byte)rgba[2], (byte)rgba[3] }; return true; }
            color = null; return false;
        }
        public float ButtonSelectedTextureOpacity(string key)
        { if(value["buttonStyles"] is JArray styles)foreach(var style in styles)if((string)style["key"]==key)return(float)style["selectedTextureOpacity"];return 0; }
        public readonly struct SurfaceRule
        {
            public readonly string Edge;
            public readonly float Width;
            public readonly byte[] Color;
            public SurfaceRule(string edge, float width, byte[] color) { Edge = edge; Width = width; Color = color; }
        }
        public IEnumerable<SurfaceRule> SurfaceRules(string key)
        {
            if (value["surfaces"] is JArray surfaces) foreach (var surface in surfaces) if ((string)surface["key"] == key && surface["rules"] is JObject rules)
                foreach (var property in rules.Properties())
                { var rule = (JArray)property.Value; yield return new SurfaceRule(property.Name, (float)rule[0], new[] { (byte)rule[1], (byte)rule[2], (byte)rule[3], (byte)rule[4] }); }
        }
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
        public IEnumerable<string> StyleKeys {get{foreach(string key in new[]{"button","buttonLabel","fieldLabel","slider","select","paragraph"})yield return String("components",key);foreach(string key in TextKeys)yield return key;if(value["buttonStyles"] is JArray buttons)foreach(var button in buttons)yield return (string)button["key"];if(value["surfaces"] is JArray surfaces)foreach(var surface in surfaces)yield return (string)surface["key"];}}
        public bool TrySurfaceColor(string key,out byte[] color)
        {
            if(value["surfaces"] is JArray surfaces)foreach(var surface in surfaces)if((string)surface["key"]==key)
            {var rgba=(JArray)surface["color"];color=new[]{(byte)rgba[0],(byte)rgba[1],(byte)rgba[2],(byte)rgba[3]};return true;}
            color=null;return false;
        }
        public float ImageBrightness(string key)
        {foreach(var asset in (JArray)value["assets"])if((string)asset["key"]==key)return (float?)asset["brightness"]??1;return 1;}
        public float ImageOpacity(string key)
        {foreach(var asset in (JArray)value["assets"])if((string)asset["key"]==key)return (float?)asset["opacity"]??1;return 1;}
        public float WindowRuleInset(string key)
        {foreach(var item in (JArray)value["text"])if((string)item["key"]==key)return(float?)item["ruleInset"]??Number("components","windowInset");return Number("components","windowInset");}
        public byte[] WindowRuleColor(string key)
        {foreach(var item in (JArray)value["text"])if((string)item["key"]==key && item["ruleColor"] is JArray c)return new[]{(byte)c[0],(byte)c[1],(byte)c[2],(byte)c[3]};return Color("window","ruleColor");}
        public bool TrySurfaceShadow(string key,out float blur,out byte[] color)
        {
            if(value["surfaces"] is JArray surfaces)foreach(var surface in surfaces)if((string)surface["key"]==key && surface["shadow"] is JObject shadow)
            {blur=(float)shadow["blur"];var rgba=(JArray)shadow["color"];color=new[]{(byte)rgba[0],(byte)rgba[1],(byte)rgba[2],(byte)rgba[3]};return true;}
            blur=0;color=null;return false;
        }
        public int ImageRotation(string key)
        {foreach(var asset in (JArray)value["assets"])if((string)asset["key"]==key)return(int?)asset["rotation"]??0;return 0;}
        public bool ImageStretch(string key)
        {foreach(var asset in (JArray)value["assets"])if((string)asset["key"]==key)return (bool?)asset["stretch"]??false;return false;}
        public bool ImageAlphaAware(string key)
        {foreach(var asset in (JArray)value["assets"])if((string)asset["key"]==key)return (bool)asset["alphaAware"];return false;}
        public float TextNumber(string key,string property) { foreach(var item in (JArray)value["text"])if((string)item["key"]==key)return(float)item[property];throw new ArgumentException("Unknown text style."); }
        public bool TextWrap(string key) { foreach(var item in (JArray)value["text"])if((string)item["key"]==key)return(bool)item["wrap"];throw new ArgumentException("Unknown text style."); }
        public bool TextSmallCaps(string key) {foreach(var item in (JArray)value["text"])if((string)item["key"]==key)return(bool?)item["smallCaps"]??false;return false;}
        public float TextLetterSpacing(string key) {foreach(var item in (JArray)value["text"])if((string)item["key"]==key)return(float?)item["letterSpacing"]??0;return 0;}
        public bool TextBold(string key) { foreach(var item in (JArray)value["text"])if((string)item["key"]==key)return(int?)item["weight"]==700;return false; }
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
                float brightness=(float?)item["brightness"]??1;
                float opacity=(float?)item["opacity"]??1;
                if(float.IsNaN(opacity)||float.IsInfinity(opacity)||opacity<0||opacity>1)throw new ArgumentException("Theme image opacity bounds.");
                if(item["rotation"]!=null && (item["rotation"].Type!=JTokenType.Integer || Array.IndexOf(new[]{0,90,180,270},(int)item["rotation"])<0))throw new ArgumentException("Theme image rotation bounds.");
                if(item["stretch"]!=null && item["stretch"].Type!=JTokenType.Boolean)throw new ArgumentException("Theme image stretch must be boolean.");
                if(float.IsNaN(brightness)||float.IsInfinity(brightness)||brightness<0||brightness>2)throw new ArgumentException("Theme image brightness bounds.");
                string imageKey = item["image"] == null ? key : (string)item["image"];
                if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z0-9-]{1,64}$") ||
                    string.IsNullOrEmpty(imageKey) || !System.Text.RegularExpressions.Regex.IsMatch(imageKey, "^[a-z0-9-]{1,64}$"))
                    throw new ArgumentException("Invalid theme image key.");
                yield return new AssetRecipe{Key=key,ImageKey=imageKey,Path=path,Width=(int)item["width"],Height=(int)item["height"],X=(int)rect[0],Y=(int)rect[1],RectWidth=(int)rect[2],RectHeight=(int)rect[3],Border=(int)item["border"],Edge=(int)item["edge"],AlphaAware=(bool)item["alphaAware"]};
            }
        }
    }
}
