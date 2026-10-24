using Capsule.Package;

static partial class Check
{
    static void ChangeStampCases()
    {
        string root = Path.Combine(Path.GetTempPath(), "capsule-stamp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root);
            var limits = UiPackageLimits.Community;
            True(UiDirectoryChangeStamp.TryCapture(root, limits, out var initial, out _), "initial metadata capture");
            using (var locked = new FileStream(Path.Combine(root, "main.js"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                True(UiDirectoryChangeStamp.TryCapture(root, limits, out var same, out _) && same == initial,
                    "idle metadata capture does not read locked package payloads");
            }
            string script = Path.Combine(root, "main.js");
            File.SetLastWriteTimeUtc(script, File.GetLastWriteTimeUtc(script).AddSeconds(3));
            True(UiDirectoryChangeStamp.TryCapture(root, limits, out var edited, out _) && edited != initial, "edit triggers reload hint");
            File.WriteAllText(Path.Combine(root, "undeclared.txt"), "invalid");
            True(UiDirectoryChangeStamp.TryCapture(root, limits, out var added, out _) && added != edited, "undeclared addition triggers full validation");
            True(!UiDirectoryPackageLoader.TryLoad(root, out _, out _), "change hint does not bypass full admission");
            File.Delete(script);
            True(UiDirectoryChangeStamp.TryCapture(root, limits, out var removed, out _) && removed != added, "removal triggers reload hint");
            for (int i=0;i<limits.MaxDeclarations * 2+5;i++) File.WriteAllText(Path.Combine(root, "extra"+i), "");
            True(!UiDirectoryChangeStamp.TryCapture(root, limits, out _, out _), "metadata enumeration remains bounded");
        }
        finally { Directory.Delete(root, true); }
    }
}
