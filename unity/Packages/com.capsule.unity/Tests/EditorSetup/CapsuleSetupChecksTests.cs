using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Capsule.Unity.Development;

namespace Capsule.Unity.Editor.Tests
{
    public sealed class CapsuleSetupChecksTests
    {
        [Test]
        public void FreshProjectDiagnosticsReadActualMissingResourcesWithoutMutation()
        {
            if (System.Environment.GetEnvironmentVariable("CAPSULE_FRESH_SETUP_PROOF") != "1")
                Assert.Ignore("Requires a dedicated fresh Unity consumer without TMP resources.");
            var before = UnityEditor.PlayerSettings.colorSpace;
            var snapshot = CapsuleSetupChecks.ReadCurrent();
            var checks = CapsuleSetupChecks.Evaluate(snapshot);
            Assert.That(checks.Any(check => check.Code == "tmp.settings"), Is.True);
            Assert.That(checks.Any(check => check.Code == "input.handler"), Is.True);
            Assert.That(checks.Any(check => check.Code == "package.missing" || check.Code == "package.version" ||
                check.Code == "native.importer" || check.Code == "runtime.resource" || check.Code == "compatibility.mismatch" ||
                check.Code == "compatibility.manifest"), Is.False);
            Assert.That(CapsuleSetupChecks.ReadCurrent().TmpSettings, Is.False);
            Assert.That(UnityEditor.PlayerSettings.colorSpace, Is.EqualTo(before));
        }

        [Test]
        public void MissingPackageAndRuntimeInputsHaveActionableDiagnosticsWithoutMutatingSettings()
        {
            var snapshot = new CapsuleSetupSnapshot { ColorSpace = ColorSpace.Gamma, NewInputEnabled = false };
            var checks = CapsuleSetupChecks.Evaluate(snapshot);
            Assert.That(checks.Any(check => check.Code == "package.missing" && check.Message.Contains("com.capsule.core")), Is.True);
            Assert.That(checks.Any(check => check.Message.Contains("com.capsule.features.rpg")), Is.False);
            Assert.That(checks.Count(check => check.Code == "native.importer"), Is.EqualTo(3));
            Assert.That(checks.Count(check => check.Code == "runtime.resource"), Is.EqualTo(4));
            Assert.That(checks.Single(check => check.Code == "tmp.settings").Fix, Is.EqualTo(CapsuleSetupFix.ImportTmpEssentials));
            Assert.That(checks.Single(check => check.Code == "input.handler").Fix, Is.EqualTo(CapsuleSetupFix.OpenPlayerSettings));
            Assert.That(checks.Single(check => check.Code == "color.space").Fix, Is.EqualTo(CapsuleSetupFix.SetLinearColorSpace));
            Assert.That(snapshot.ColorSpace, Is.EqualTo(ColorSpace.Gamma));
            Assert.That(snapshot.NewInputEnabled, Is.False);
            Assert.That(checks.Single(check => check.Code == "background.run").Fix, Is.EqualTo(CapsuleSetupFix.OpenPlayerSettings));
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator FreshProjectImportsOnlyTmpEssentialsThroughPlugin()
        {
            if (System.Environment.GetEnvironmentVariable("CAPSULE_FRESH_SETUP_PROOF") != "1")
                Assert.Ignore("Requires a dedicated fresh Unity consumer.");
            var before = UnityEditor.PlayerSettings.colorSpace;
            Assert.That(CapsuleSetupChecks.ReadCurrent().TmpSettings, Is.False);
            CapsuleTmpImportMonitor.ImportEssentials();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (CapsuleTmpImportMonitor.InFlight)
            {
                Assert.That(timer.Elapsed.TotalSeconds, Is.LessThan(90), CapsuleTmpImportMonitor.Status);
                yield return null;
            }
            Assert.That(CapsuleSetupChecks.ReadCurrent().TmpSettings, Is.True, CapsuleTmpImportMonitor.Status);
            Assert.That(UnityEditor.AssetDatabase.IsValidFolder("Assets/TextMesh Pro/Examples & Extras"), Is.False);
            Assert.That(UnityEditor.PlayerSettings.colorSpace, Is.EqualTo(before));
        }

        [Test]
        public void VersionMismatchIsDistinctFromMissingPackage()
        {
            var snapshot = new CapsuleSetupSnapshot();
            snapshot.Versions["com.capsule.core"] = "0.2.0";
            var checks = CapsuleSetupChecks.Evaluate(snapshot);
            Assert.That(checks.Any(check => check.Code == "package.version" && check.Message.Contains("com.capsule.core")), Is.True);
            Assert.That(checks.Any(check => check.Code == "package.missing" && check.Message.Contains("com.capsule.core")), Is.False);
        }

        [Test]
        public void UnknownInputSettingBlocksReadinessInsteadOfAssumingItIsEnabled()
        {
            var snapshot = new CapsuleSetupSnapshot { NewInputEnabled = null };
            var input = CapsuleSetupChecks.Evaluate(snapshot).Single(check => check.Code == "input.handler");
            Assert.That(input.Message, Does.Contain("could not be read"));
        }

        [Test]
        public void PresentCarrierAndNativeLibraryWithWrongBytesReportMismatch()
        {
            var snapshot = new CapsuleSetupSnapshot();
            snapshot.Resources[CapsuleSetupChecks.CarrierPath] = true;
            snapshot.NativeImporters["Packages/com.capsule.yoga/Runtime/Plugins/x86_64/portable_ui_yoga.dll"] = true;
            snapshot.CompatibleBytes[CapsuleSetupChecks.CarrierPath] = false;
            snapshot.CompatibleBytes["Packages/com.capsule.yoga/Runtime/Plugins/x86_64/portable_ui_yoga.dll"] = false;
            var checks = CapsuleSetupChecks.Evaluate(snapshot);
            Assert.That(checks.Count(check => check.Code == "compatibility.mismatch"), Is.EqualTo(2));
            Assert.That(checks.Any(check => check.Code == "runtime.resource" && check.Message.Contains(CapsuleSetupChecks.CarrierPath)), Is.False);
            Assert.That(checks.Any(check => check.Code == "native.importer" && check.Message.Contains("portable_ui_yoga.dll")), Is.False);
        }

        [Test]
        public void InvalidShippedCompatibilityManifestIsASeparateDiagnostic()
        {
            var snapshot = new CapsuleSetupSnapshot { CompatibilityManifestAvailable = false };
            Assert.That(CapsuleSetupChecks.Evaluate(snapshot).Any(check => check.Code == "compatibility.manifest"), Is.True);
        }

        [Test]
        public void DevCommandOwnsStandardInputAndLocalEntrySkipsPublishedResolution()
        {
            var settings = new CapsuleWorkshopSettings { Project = Path.Combine(Path.GetTempPath(), "sample project"), Port = 5180 };
            var published = CapsuleWorkshopProcess.BuildArguments(settings, "dev");
            Assert.That(published, Does.Contain("@capsule-sdk/workshop@0.1.0"));
            Assert.That(published, Does.Contain("--watch-stdin"));
            Assert.That(published, Does.Contain("sample project"));
            settings.ToolingEntry = Path.Combine(Path.GetTempPath(), "project-cli.ts");
            var local = CapsuleWorkshopProcess.BuildArguments(settings, "dev");
            Assert.That(local, Does.Contain("project-cli.ts"));
            Assert.That(local, Does.Not.Contain("@capsule-sdk/workshop"));
        }

        [Test]
        public void HostSetupUsesExactLoopbackEndpointRuleBeforeSceneMutation()
        {
            Assert.That(new CapsuleWorkshopSettings().StarterDemoProvider, Is.True);
            Assert.That(CapsuleDevelopmentHost.TryEndpoint("https://localhost:5173", out _, out _), Is.False);
            Assert.That(CapsuleDevelopmentHost.TryEndpoint("http://example.com:5173", out _, out _), Is.False);
            Assert.That(() => CapsuleSetupHost.Create("https://localhost:5173", null, true),
                Throws.TypeOf<System.ArgumentException>());
        }
    }
}
