using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PortableUi.Package
{
    // A cheap reload hint, never package admission or content identity. A changed
    // hint still goes through the complete loader before any bytes reach a guest.
    public static class UiDirectoryChangeStamp
    {
        public static bool TryCapture(string directory, UiPackageLimits limits, out string stamp, out string error)
        {
            stamp = null; error = null;
            try
            {
                string root = Path.GetFullPath(directory);
                var pending = new Stack<string>();
                var entries = new List<string>();
                pending.Push(root);
                int directories = 0, count = 0;
                while (pending.Count > 0)
                {
                    string current = pending.Pop();
                    if (++directories > (long)limits.MaxDeclarations + 2) throw new IOException("Package directory count quota exceeded.");
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Package contains a reparse point.");
                    foreach (string child in Directory.EnumerateFileSystemEntries(current))
                    {
                        if (++count > (long)limits.MaxDeclarations * 2 + 4) throw new IOException("Package entry count quota exceeded.");
                        var info = new FileInfo(child);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Package contains a reparse point.");
                        string relative = child.Substring(root.Length);
                        if ((info.Attributes & FileAttributes.Directory) != 0)
                        { entries.Add("D:" + relative); pending.Push(child); }
                        else entries.Add("F:" + relative + ":" + info.Length + ":" + info.LastWriteTimeUtc.Ticks + ":" + info.CreationTimeUtc.Ticks);
                    }
                }
                entries.Sort(StringComparer.Ordinal);
                var text = new StringBuilder();
                foreach (string entry in entries) text.Append(entry.Length).Append(':').Append(entry);
                stamp = text.ToString();
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            { error = "Cannot inspect package: " + exception.Message; return false; }
        }
    }
}
