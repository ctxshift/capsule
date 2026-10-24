using Newtonsoft.Json.Linq;
using Capsule.Package;
static partial class Program
{
    static void ThemeLimitCases()
    {
        var theme = new JObject { ["schemaVersion"] = 1, ["id"] = "neutral", ["assets"] = new JArray(), ["text"] = new JArray() };
        foreach (var key in new[] { "font", "components", "window", "button", "range", "select", "focus" }) theme[key] = new JObject();
        var surfaces = new JArray(); theme["surfaces"] = surfaces;
        for (int i = 0; i < 31; i++) surfaces.Add(new JObject { ["key"] = "surface-" + i, ["color"] = new JArray(0, 0, 0, 255) });
        bool rejected = false;
        try { _ = new UiControlTheme(theme.ToString()); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Default theme still rejects the thirty-first surface.");
        _ = new UiControlTheme(theme.ToString(), new UiThemeLimits(64,64,64));
        for (int i = 31; i < 65; i++) surfaces.Add(new JObject { ["key"] = "surface-" + i, ["color"] = new JArray(0, 0, 0, 255) });
        rejected = false;
        try { _ = new UiControlTheme(theme.ToString(), new UiThemeLimits(64,64,64)); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Extended surface allowance remains bounded at sixty-four.");
    }
}
