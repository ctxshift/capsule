using System;
using TMPro;
using UnityEditor;

namespace Capsule.Unity.Editor
{
    [InitializeOnLoad]
    public static class CapsuleTmpImportMonitor
    {
        private const string Prefix = "Capsule.Setup.TmpEssentials.";
        private const long DeadlineTicks = 90L * TimeSpan.TicksPerSecond;

        public static bool InFlight => SessionState.GetBool(Prefix + "InFlight", false);
        public static string Status => SessionState.GetString(Prefix + "Status", "");

        static CapsuleTmpImportMonitor()
        {
            AssetDatabase.importPackageCompleted += Completed;
            AssetDatabase.importPackageFailed += Failed;
            AssetDatabase.importPackageCancelled += Cancelled;
            EditorApplication.update += CheckDeadline;
        }

        public static void ImportEssentials()
        {
            if (InFlight) throw new InvalidOperationException("TMP Essentials import is already in progress or awaiting completion.");
            SessionState.SetBool(Prefix + "InFlight", true);
            SessionState.SetBool(Prefix + "TimedOut", false);
            SessionState.SetString(Prefix + "Status", "Importing TMP Essential Resources…");
            SessionState.SetString(Prefix + "StartedUtcTicks", DateTime.UtcNow.Ticks.ToString());
            try
            {
                // Unity's importer is synchronous and non-cancellable. The deadline bounds monitoring, not the import itself.
                TMP_PackageResourceImporter.ImportResources(true, false, false);
                if (InFlight && CapsuleSetupChecks.HasTmpSettings())
                    Finish("TMP Essential Resources are present. Refresh setup checks.");
            }
            catch (Exception error)
            {
                Finish("TMP Essentials import failed: " + error.Message);
                throw;
            }
        }

        private static bool IsEssentials(string package)
        { return package != null && package.IndexOf("TMP Essential Resources", StringComparison.OrdinalIgnoreCase) >= 0; }

        private static void Completed(string package)
        { if (InFlight && IsEssentials(package)) Finish("TMP Essential Resources import completed. Refresh setup checks."); }

        private static void Failed(string package, string error)
        { if (InFlight && IsEssentials(package)) Finish("TMP Essentials import failed: " + error); }

        private static void Cancelled(string package)
        { if (InFlight && IsEssentials(package)) Finish("TMP Essentials import was cancelled."); }

        private static void CheckDeadline()
        {
            if (!InFlight || SessionState.GetBool(Prefix + "TimedOut", false)) return;
            if (!long.TryParse(SessionState.GetString(Prefix + "StartedUtcTicks", ""), out var started)) return;
            if (DateTime.UtcNow.Ticks - started < DeadlineTicks) return;
            SessionState.SetBool(Prefix + "TimedOut", true);
            SessionState.SetString(Prefix + "Status", "TMP import completion was not observed within 90 seconds. The import may still be running; another import is refused until Unity reports completion, failure, or cancellation.");
        }

        private static void Finish(string status)
        {
            SessionState.SetBool(Prefix + "InFlight", false);
            SessionState.SetBool(Prefix + "TimedOut", false);
            SessionState.SetString(Prefix + "Status", status);
            SessionState.EraseString(Prefix + "StartedUtcTicks");
        }
    }
}
