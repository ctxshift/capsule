using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Capsule;
using Capsule.Unity.Development;
using Capsule.Unity.Editor;
using Capsule.Yoga;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace Capsule.Tests
{
    // This is an opt-in graphics-enabled connected-Editor proof. Compiler-only,
    // browser, and CPU-only runs cannot satisfy its framebuffer assertions.
    public sealed class CapsuleDevelopmentLiveReloadTests
    {
        const double FixtureSeconds = 150;
        static Action cleanupProof;
        static void CleanupProof() { var cleanup = cleanupProof; cleanupProof = null; cleanup?.Invoke(); }
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        static string Required(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value)) Assert.Ignore(name + " is required for the live development proof.");
            return Path.GetFullPath(value);
        }

        static string Under(string root, string relative)
        {
            if (Path.IsPathRooted(relative) || relative.Split('/', '\\').Any(part => part == ".." || part == "."))
                throw new ArgumentException("External project path is not relative.");
            string result = Path.GetFullPath(Path.Combine(root, relative));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("External project path escapes the project.");
            return result;
        }

        static IEnumerator Until(Func<bool> ready, double seconds, string detail, Stopwatch fixture)
        {
            var wait = Stopwatch.StartNew();
            while (!ready())
            {
                if (wait.Elapsed.TotalSeconds > seconds || fixture.Elapsed.TotalSeconds > FixtureSeconds)
                    Assert.Fail(detail + " (workshop=" + CapsuleWorkshopProcess.Status + ", output=" + CapsuleWorkshopProcess.LastOutput + ")");
                yield return null;
            }
        }

        static bool Text(CapsuleDevelopmentHost host, string value) => host && host.CanvasRoot &&
            host.CanvasRoot.GetComponentsInChildren<TMP_Text>(true).Any(label => label.text == value && label.gameObject.activeInHierarchy);

        static UiNode Panel(CapsuleDevelopmentHost host) => host.LastSnapshot?.Nodes.FirstOrDefault(node =>
            node.Kind == UiNodeKind.Box && node.StyleKey == "panel");

        static Image PanelImage(CapsuleDevelopmentHost host) => host.CanvasRoot.GetComponentsInChildren<Image>(true)
            .FirstOrDefault(image => !image.sprite && image.rectTransform.rect.width >= 350 && image.gameObject.activeInHierarchy);

        static Image ArtImage(CapsuleDevelopmentHost host) => host.CanvasRoot.GetComponentsInChildren<Image>(true)
            .FirstOrDefault(image => image.sprite && image.gameObject.activeInHierarchy);

        static Vector2 Center(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null,
            rect.TransformPoint(rect.rect.center));

        static Vector2 PanelSample(CapsuleDevelopmentHost host)
        {
            var rect = PanelImage(host).rectTransform;
            return RectTransformUtility.WorldToScreenPoint(null,
                rect.TransformPoint(new Vector3(rect.rect.xMax - 12, rect.rect.yMin + 12)));
        }

        static Color32 ReadCapturedPixel(string folder, string name, Vector2 point)
        {
            Assert.That(point.x, Is.InRange(0, Screen.width - 1), "Rendered sample must be on screen.");
            Assert.That(point.y, Is.InRange(0, Screen.height - 1), "Rendered sample must be on screen.");
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(ImageConversion.LoadImage(image, File.ReadAllBytes(Path.Combine(folder, name + ".png"))), Is.True);
                return image.GetPixel(Mathf.Clamp(Mathf.RoundToInt(point.x * image.width / Screen.width), 0, image.width - 1),
                    Mathf.Clamp(Mathf.RoundToInt(point.y * image.height / Screen.height), 0, image.height - 1));
            }
            finally { UnityEngine.Object.Destroy(image); }
        }

        static IEnumerator Capture(string folder, string name)
        {
            var path = Path.Combine(folder, name + ".png");
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            var timeout = Stopwatch.StartNew();
            while (!File.Exists(path))
            {
                Assert.That(timeout.Elapsed.TotalSeconds, Is.LessThan(8), "Screenshot was not written.");
                yield return null;
            }
            // Read the completed capture rather than the Editor's current render target.
            yield return null;
        }

        static IEnumerator Click(CapsuleDevelopmentHost host, Mouse mouse, Stopwatch fixture)
        {
            var button = host.CanvasRoot.GetComponentsInChildren<Button>(true).Single(control => control.gameObject.activeInHierarchy);
            var point = Center((RectTransform)button.transform);
            Assert.That(point.x, Is.InRange(0, Screen.width));
            Assert.That(point.y, Is.InRange(0, Screen.height));
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.That(hits.Any(hit => hit.gameObject == button.gameObject), Is.True, "Native button must be pointer-raycastable.");
            int previous = host.DemoRequestCount;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            yield return null;
            yield return Until(() => host.DemoRequestCount == previous + 1 && Text(host, "Host " + (previous + 1)),
                8, "Native pointer action or host observation did not arrive", fixture);
        }

        static IEnumerator Revision(CapsuleDevelopmentHost host, string previous, string expectedText,
            int processId, CapsuleDevelopmentHost hostId, Stopwatch fixture)
        {
            yield return Until(() => host && host.ActiveRevision != null && host.ActiveRevision != previous,
                15, "New native revision was not rendered", fixture);
            Assert.That(Text(host, expectedText), Is.True, "Text must be present in the first replacement frame.");
            Assert.That(Panel(host), Is.Not.Null);
            Assert.That(ArtImage(host), Is.Not.Null);
            Assert.That(host.CanvasRoot.GetComponentsInChildren<Button>().Count(button => button.gameObject.activeInHierarchy), Is.EqualTo(1));
            Assert.That(Process.GetCurrentProcess().Id, Is.EqualTo(processId));
            Assert.That(host, Is.SameAs(hostId));
            Assert.That(host.LastError, Is.Null);
        }

        [UnityTest]
        public IEnumerator ActualNativeUIReloadsAndRetainsLastGoodThroughErrors()
        {
            Required("CAPSULE_PROOF_UI");
            if (!Application.isPlaying) yield return new EnterPlayMode();
            yield return RunProof();
        }

        static IEnumerator RunProof()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Fail("Rendered proof requires a graphics device.");
            if (CapsuleWorkshopProcess.Running) Assert.Fail("A workshop process already runs; fixture may stop only its own process.");
            string project = Required("CAPSULE_PROOF_UI");
            string tooling = Required("CAPSULE_PROOF_TOOLING");
            string bun = Required("CAPSULE_PROOF_BUN");
            Debug.Log("Capsule proof: environment loaded");
            string output = Environment.GetEnvironmentVariable("CAPSULE_PROOF_OUTPUT");
            output = string.IsNullOrWhiteSpace(output) ? Path.Combine(Path.GetTempPath(), "capsule-live-proof-" + Guid.NewGuid().ToString("N")) : Path.GetFullPath(output);
            string assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Assert.That(output.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase), Is.False, "Proof output must stay outside Assets.");
            Directory.CreateDirectory(output);
            Debug.Log("Capsule proof: output ready");
            var config = JObject.Parse(File.ReadAllText(Under(project, "capsule.project.json")));
            string sourcePath = Under(project, (string)config["source"]);
            string themePath = Under(project, (string)config["theme"]);
            var theme = JObject.Parse(File.ReadAllText(themePath));
            Debug.Log("Capsule proof: project and theme loaded");
            var artRecipe = (JObject)((JArray)theme["assets"])[0];
            string artPath = Under(project, (string)artRecipe["path"]);
            byte[] sourceOriginal = File.ReadAllBytes(sourcePath), themeOriginal = File.ReadAllBytes(themePath), artOriginal = File.ReadAllBytes(artPath);
            string source = Utf8.GetString(sourceOriginal);
            Assert.That(source, Does.Contain("Capsule UI"));
            Assert.That(source, Does.Contain("width={360} height={240}"));
            Assert.That((int)artRecipe["width"], Is.EqualTo(3));
            Assert.That((int)artRecipe["height"], Is.EqualTo(3));
            var settings = new CapsuleWorkshopSettings {
                Bun = bun, ToolingEntry = tooling, Project = project, Port = (int)config["port"],
                Endpoint = "http://127.0.0.1:" + (int)config["port"], StarterDemoProvider = true
            };
            var fixture = Stopwatch.StartNew();
            var report = new JObject { ["schemaVersion"] = 1, ["unityPid"] = Process.GetCurrentProcess().Id,
                ["stages"] = new JArray() };
            var stages = (JArray)report["stages"];
            GameObject fixtureObject = null;
            Mouse mouse = null;
            bool ownedServer = false;
            int yogaBefore = NativeYoga.LiveRoots;
            EventSystem priorEvents = EventSystem.current;
            bool previousBackground = Application.runInBackground;
            Application.runInBackground = true;
            var previousInput = InputSystem.settings;
            var fixtureInput = UnityEngine.Object.Instantiate(previousInput);
            fixtureInput.hideFlags = HideFlags.HideAndDontSave;
            fixtureInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            fixtureInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = fixtureInput;
            cleanupProof = () =>
            {
                File.WriteAllBytes(sourcePath, sourceOriginal);
                File.WriteAllBytes(themePath, themeOriginal);
                File.WriteAllBytes(artPath, artOriginal);
                if (fixtureObject) { var host = fixtureObject.GetComponent<CapsuleDevelopmentHost>(); if (host) host.enabled = false; UnityEngine.Object.Destroy(fixtureObject); }
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (ownedServer) CapsuleWorkshopProcess.Stop();
                File.WriteAllText(Path.Combine(output, "report.json"), report.ToString(), Utf8);
                if (priorEvents && priorEvents.isActiveAndEnabled) EventSystem.current = priorEvents;
                Application.runInBackground = previousBackground;
                InputSystem.settings = previousInput;
                UnityEngine.Object.Destroy(fixtureInput);
            };
            try
            {
                Debug.Log("Capsule proof: starting workshop");
                CapsuleWorkshopProcess.Start(settings, "dev"); ownedServer = true;
                yield return Until(() => CapsuleWorkshopProcess.DevRunning && !string.IsNullOrEmpty(CapsuleWorkshopProcess.ReadyEndpoint),
                    15, "Owned development server did not become ready", fixture);
                fixtureObject = new GameObject("Capsule native live proof"); fixtureObject.SetActive(false);
                var host = fixtureObject.AddComponent<CapsuleDevelopmentHost>();
                host.enabled = false; host.Endpoint = settings.Endpoint;
                host.Carrier = AssetDatabase.LoadAssetAtPath<TextAsset>(CapsuleDevelopmentCompatibility.CarrierPath);
                host.Sdk = AssetDatabase.LoadAssetAtPath<TextAsset>(CapsuleDevelopmentCompatibility.ReactSdkPath);
                host.Font = TMP_Settings.defaultFontAsset; host.EnableDemoProvider = true;
                Assert.That(CapsuleDevelopmentCompatibility.Validate(host.Carrier, host.Sdk, out var compatibilityError), Is.True, compatibilityError);
                Assert.That(host.Font, Is.Not.Null);
                fixtureObject.SetActive(true); host.enabled = true;
                var hostId = host; int pid = Process.GetCurrentProcess().Id;
                mouse = InputSystem.AddDevice<Mouse>("CapsuleProofMouse");
                yield return Until(() => {
                    report["hostLastError"] = host.LastError;
                    report["hostEnabled"] = host.enabled;
                    report["hostActiveRevision"] = host.ActiveRevision;
                    return host.ActiveRevision != null && Text(host, "Capsule UI") && Panel(host) != null && ArtImage(host);
                },
                    15, "Initial native UI was not rendered", fixture);
                yield return new WaitForEndOfFrame();
                yield return Capture(output, "01-initial");
                report["firstActivationMilliseconds"] = host.FirstActivationMilliseconds;
                report["activationTimingBoundary"] = "component enable to complete snapshot activation; screenshot capture excluded";
                var originalArtPixel = ReadCapturedPixel(output, "01-initial", Center(ArtImage(host).rectTransform));
                stages.Add(new JObject { ["step"] = "initial", ["revision"] = host.ActiveRevision, ["prepareMilliseconds"] = host.LastPreparationMilliseconds });
                yield return Click(host, mouse, fixture);

                string previous = host.ActiveRevision;
                var editTimer = Stopwatch.StartNew();
                string validSource = source.Replace("Capsule UI", "Capsule UI Live A").Replace("width={360} height={240}", "width={420} height={260}");
                File.WriteAllText(sourcePath, validSource, Utf8);
                yield return Revision(host, previous, "Capsule UI Live A", pid, hostId, fixture);
                Assert.That(Panel(host).Rect.Width, Is.EqualTo(420 * 64));
                Assert.That(Panel(host).Rect.Height, Is.EqualTo(260 * 64));
                yield return new WaitForEndOfFrame();
                yield return Capture(output, "02-text-layout");
                var originalPanelPixel = ReadCapturedPixel(output, "02-text-layout", PanelSample(host));
                stages.Add(new JObject { ["step"] = "text-layout", ["revision"] = host.ActiveRevision,
                    ["editToRenderedMilliseconds"] = editTimer.Elapsed.TotalMilliseconds, ["prepareMilliseconds"] = host.LastPreparationMilliseconds });

                previous = host.ActiveRevision;
                ((JObject)theme["surfaces"].First)["color"] = new JArray(120, 25, 50, 255);
                File.WriteAllText(themePath, theme.ToString(), Utf8);
                yield return Revision(host, previous, "Capsule UI Live A", pid, hostId, fixture);
                Assert.That((Color32)PanelImage(host).color, Is.EqualTo(new Color32(120, 25, 50, 255)));
                yield return new WaitForEndOfFrame();
                yield return Capture(output, "03-theme");
                Assert.That(ReadCapturedPixel(output, "03-theme", PanelSample(host)), Is.Not.EqualTo(originalPanelPixel), "Rendered panel pixel did not change.");
                stages.Add(new JObject { ["step"] = "theme", ["revision"] = host.ActiveRevision });

                previous = host.ActiveRevision;
                var replacementArt = new Texture2D(3, 3, TextureFormat.RGBA32, false);
                try
                {
                    replacementArt.SetPixels(Enumerable.Repeat(Color.magenta, 9).ToArray()); replacementArt.Apply();
                    File.WriteAllBytes(artPath, replacementArt.EncodeToPNG());
                }
                finally { UnityEngine.Object.Destroy(replacementArt); }
                yield return Revision(host, previous, "Capsule UI Live A", pid, hostId, fixture);
                yield return new WaitForEndOfFrame();
                Assert.That(ArtImage(host).sprite.rect.width, Is.EqualTo(3));
                Assert.That(ArtImage(host).sprite.rect.height, Is.EqualTo(3));
                yield return Capture(output, "04-art");
                var changedArtPixel = ReadCapturedPixel(output, "04-art", Center(ArtImage(host).rectTransform));
                Assert.That(changedArtPixel, Is.Not.EqualTo(originalArtPixel), "Rendered image pixel did not change.");
                stages.Add(new JObject { ["step"] = "art", ["revision"] = host.ActiveRevision });
                yield return Click(host, mouse, fixture);

                previous = host.ActiveRevision;
                File.WriteAllText(sourcePath, "<invalid TSX", Utf8);
                yield return Until(() => !string.IsNullOrEmpty(host.LastError), 15, "Invalid TSX did not surface a diagnostic", fixture);
                Assert.That(host.ActiveRevision, Is.EqualTo(previous)); Assert.That(Text(host, "Capsule UI Live A"), Is.True);
                stages.Add(new JObject { ["step"] = "invalid-tsx", ["retainedRevision"] = host.ActiveRevision, ["diagnosticSeen"] = true });
                yield return Click(host, mouse, fixture);
                File.WriteAllText(sourcePath, validSource.Replace("Capsule UI Live A", "Capsule UI Recovered"), Utf8);
                yield return Revision(host, previous, "Capsule UI Recovered", pid, hostId, fixture);
                yield return new WaitForEndOfFrame(); yield return Capture(output, "05-tsx-recovered");
                stages.Add(new JObject { ["step"] = "tsx-recovered", ["revision"] = host.ActiveRevision });

                previous = host.ActiveRevision;
                var repairedTheme = (JObject)theme.DeepClone();
                theme["schemaVersion"] = 2;
                File.WriteAllText(themePath, theme.ToString(), Utf8);
                yield return Until(() => !string.IsNullOrEmpty(host.LastError), 15, "Invalid theme did not surface a diagnostic", fixture);
                Assert.That(host.ActiveRevision, Is.EqualTo(previous)); Assert.That(Text(host, "Capsule UI Recovered"), Is.True);
                stages.Add(new JObject { ["step"] = "invalid-theme", ["retainedRevision"] = host.ActiveRevision, ["diagnosticSeen"] = true });
                yield return Click(host, mouse, fixture);
                theme = repairedTheme; ((JObject)theme["surfaces"].First)["color"] = new JArray(20, 85, 105, 255);
                File.WriteAllText(themePath, theme.ToString(), Utf8);
                yield return Revision(host, previous, "Capsule UI Recovered", pid, hostId, fixture);
                stages.Add(new JObject { ["step"] = "theme-recovered", ["revision"] = host.ActiveRevision });

                previous = host.ActiveRevision;
                CapsuleWorkshopProcess.Stop();
                yield return Until(() => !CapsuleWorkshopProcess.Running, 8, "Owned server did not stop", fixture);
                yield return Until(() => !string.IsNullOrEmpty(host.LastError), 10, "Network loss did not produce a diagnostic", fixture);
                Assert.That(host.ActiveRevision, Is.EqualTo(previous));
                stages.Add(new JObject { ["step"] = "disconnected", ["retainedRevision"] = host.ActiveRevision, ["diagnosticSeen"] = true });
                yield return Click(host, mouse, fixture);
                CapsuleWorkshopProcess.Start(settings, "dev");
                yield return Until(() => CapsuleWorkshopProcess.DevRunning && !string.IsNullOrEmpty(CapsuleWorkshopProcess.ReadyEndpoint),
                    15, "Owned server did not restart", fixture);
                File.WriteAllText(sourcePath, validSource.Replace("Capsule UI Live A", "Capsule UI Live B"), Utf8);
                yield return Revision(host, previous, "Capsule UI Live B", pid, hostId, fixture);
                yield return Click(host, mouse, fixture);
                yield return new WaitForEndOfFrame(); yield return Capture(output, "06-reconnected");
                stages.Add(new JObject { ["step"] = "reconnected", ["revision"] = host.ActiveRevision });
                yield return Until(() => new[] { "01-initial", "02-text-layout", "03-theme", "04-art", "05-tsx-recovered", "06-reconnected" }
                    .All(name => File.Exists(Path.Combine(output, name + ".png"))), 8,
                    "Native screenshots were not written", fixture);
                report["requests"] = host.DemoRequestCount;
                report["durationSeconds"] = fixture.Elapsed.TotalSeconds;
                Assert.That(fixture.Elapsed.TotalSeconds, Is.LessThan(FixtureSeconds));
            }
            finally
            {
                CleanupProof();
            }
            yield return Until(() => !CapsuleWorkshopProcess.Running && NativeYoga.LiveRoots == yogaBefore,
                8, "Owned server or Yoga roots survived cleanup", fixture);
            Assert.That(Process.GetCurrentProcess().Id, Is.EqualTo((int)report["unityPid"]));
        }

        [UnityTearDown]
        public IEnumerator LeaveOwnedPlayMode()
        {
            CleanupProof();
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        [Test]
        public void SetupHostAndRepeatedReadinessPreserveOtherScenesAndSettings()
        {
            if (Application.isPlaying) Assert.Ignore("Run this setup preservation check in EditMode.");
            var previous = SceneManager.GetActiveScene();
            bool wasDirty = previous.isDirty;
            var colorSpace = PlayerSettings.colorSpace;
            var font = TMP_Settings.defaultFontAsset;
            Assert.That(font, Is.Not.Null);
            GameObject created = null;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            try
            {
                var first = CapsuleSetupChecks.Evaluate(CapsuleSetupChecks.ReadCurrent());
                var second = CapsuleSetupChecks.Evaluate(CapsuleSetupChecks.ReadCurrent());
                Assert.That(second.Select(check => check.Code), Is.EquivalentTo(first.Select(check => check.Code)));
                Assert.That(PlayerSettings.colorSpace, Is.EqualTo(colorSpace));
                Assert.That(previous.isDirty, Is.EqualTo(wasDirty));
                var host = CapsuleSetupHost.Create("http://127.0.0.1:5173", font, true);
                created = host.gameObject;
                Assert.That(host.enabled, Is.False);
                Assert.That(host.gameObject.activeSelf, Is.True);
                Assert.That(CapsuleDevelopmentCompatibility.Validate(host.Carrier, host.Sdk, out var error), Is.True, error);
                Assert.That(PlayerSettings.colorSpace, Is.EqualTo(colorSpace));
            }
            finally
            {
                Undo.RevertAllDownToGroup(undoGroup);
                if (created) UnityEngine.Object.DestroyImmediate(created);
            }
        }
    }
}
