#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Capsule.Unity.Development;
using Capsule.Package;
using Capsule.Unity;
using NUnit.Framework;
using System;
using System.Collections;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Capsule.Tests
{
    public sealed class CapsuleDevelopmentHostTests
    {
        [TestCase("http://127.0.0.1:5173", true)]
        [TestCase("http://localhost:5173", true)]
        [TestCase("http://[::1]:5173", true)]
        [TestCase("https://localhost:5173", false)]
        [TestCase("http://example.test:5173", false)]
        [TestCase("http://127.0.0.1:5173/path", false)]
        [TestCase("http://localhost:5173/?source=other", false)]
        [TestCase("http://user@localhost:5173", false)]
        public void OnlyExplicitLoopbackOriginsAreAdmitted(string endpoint, bool accepted)
        {
            Assert.That(CapsuleDevelopmentHost.TryEndpoint(endpoint, out _, out _), Is.EqualTo(accepted));
        }

        [Test]
        public void DisabledHostCreatesNoSurfaceOrSession()
        {
            var root = new GameObject("Development host test");
            root.SetActive(false);
            try
            {
                var host = root.AddComponent<CapsuleDevelopmentHost>();
                Assert.That(host.Endpoint, Is.EqualTo("http://127.0.0.1:5173"));
                Assert.That(host.Generation, Is.Zero);
                Assert.That(host.ActiveRevision, Is.Null);
                Assert.That(host.CanvasRoot, Is.Null);
                Assert.That(host.EnqueueObservation(7, 1, new byte[2]), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void ShippedResourcesPassAndSubstitutionsFailCompatibility()
        {
            var carrier = AssetDatabase.LoadAssetAtPath<TextAsset>(CapsuleDevelopmentCompatibility.CarrierPath);
            var sdk = AssetDatabase.LoadAssetAtPath<TextAsset>(CapsuleDevelopmentCompatibility.ReactSdkPath);
            Assert.That(carrier, Is.Not.Null);
            Assert.That(sdk, Is.Not.Null);
            Assert.That(CapsuleDevelopmentCompatibility.Validate(carrier, sdk, out var validError), Is.True, validError);
            var wrong = new TextAsset("wrong resource");
            try
            {
                Assert.That(CapsuleDevelopmentCompatibility.Validate(wrong, sdk, out var carrierError), Is.False);
                Assert.That(carrierError, Does.Contain("carrier"));
                Assert.That(CapsuleDevelopmentCompatibility.Validate(carrier, wrong, out var sdkError), Is.False);
                Assert.That(sdkError, Does.Contain("SDK"));
            }
            finally { UnityEngine.Object.DestroyImmediate(wrong); }
        }

        [UnityTest]
        public IEnumerator NeutralTargetRendersButtonsTextAndImage()
        {
            const string themeJson = "{\"schemaVersion\":1,\"id\":\"neutral\",\"font\":{\"family\":\"fixture\"}," +
                "\"components\":{\"button\":\"button\",\"buttonLabel\":\"paragraph\"},\"window\":{}," +
                "\"button\":{\"background\":[20,60,80,255]},\"range\":{},\"select\":{},\"focus\":{}," +
                "\"assets\":[{\"key\":\"neutral\",\"path\":\"assets/neutral.png\",\"width\":2,\"height\":2," +
                "\"rect\":[0,0,2,2],\"border\":0,\"edge\":0,\"alphaAware\":false}]," +
                "\"text\":[{\"key\":\"paragraph\",\"size\":16,\"lineHeight\":24," +
                "\"color\":[240,240,240,255],\"wrap\":false}],\"surfaces\":[{\"key\":\"panel\",\"color\":[10,30,40,255]}]}";
            var root = new GameObject("Native development target", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var font = TMP_Settings.defaultFontAsset;
            Assert.That(font, Is.Not.Null, "TMP Essentials font is required for the native render test.");
            var theme = new UiControlTheme(themeJson);
            var factory = new CapsuleDevelopmentWidgetFactory(theme, font);
            var pixels = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            pixels.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red }); pixels.Apply();
            factory.AddImage(new UiControlTheme.AssetRecipe { Key = "neutral", Width = 2, Height = 2, RectWidth = 2, RectHeight = 2 }, pixels.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(pixels);
            var target = new CapsuleNativeTarget((RectTransform)root.transform, factory);
            try
            {
                var nodes = new[] {
                    new Capsule.UiNode(1, 0, Capsule.UiNodeKind.Box, new Capsule.UiRect(0, 0, 240*64, 160*64), "panel", "", "", 0, true, Capsule.UiEventMask.None, 0),
                    new Capsule.UiNode(2, 1, Capsule.UiNodeKind.Button, new Capsule.UiRect(8*64, 8*64, 100*64, 30*64), "button", "", "", 0, true, Capsule.UiEventMask.Activate, 1),
                    new Capsule.UiNode(3, 2, Capsule.UiNodeKind.Text, new Capsule.UiRect(0, 0, 100*64, 30*64), "paragraph", "", "Increment", 0, true, Capsule.UiEventMask.None, 2),
                    new Capsule.UiNode(4, 1, Capsule.UiNodeKind.Image, new Capsule.UiRect(8*64, 48*64, 32*64, 32*64), "", "neutral", "", 0, true, Capsule.UiEventMask.None, 3)
                };
                Assert.That(target.TryCommit(new Capsule.UiSnapshot(1, nodes), out var error), Is.True, error);
                target.Activate();
                Assert.That(target.TryGetNativeWidget(2, out var button), Is.True);
                Assert.That(button.GetComponent<Button>(), Is.Not.Null);
                Assert.That((Color32)button.GetComponent<Image>().color, Is.EqualTo(new Color32(20, 60, 80, 255)));
                Assert.That(target.TryGetNativeWidget(3, out var label), Is.True);
                Assert.That(label.GetComponent<TMP_Text>().text, Is.EqualTo("Increment"));
                Assert.That(target.TryGetNativeWidget(4, out var image), Is.True);
                Assert.That(image.GetComponent<Image>(), Is.Not.Null);
                Assert.That(image.GetComponent<Image>().sprite, Is.Not.Null);
                yield return null;
            }
            finally { target.Dispose(); UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void DemoActionRequiresCurrentButtonProvenanceAndTwoBytes()
        {
            const uint owner = 23;
            ulong button = ((ulong)owner << 32) | 1;
            var snapshot = new Capsule.UiSnapshot(1, new[] {
                new Capsule.UiNode(button, 0, Capsule.UiNodeKind.Button, new Capsule.UiRect(0, 0, 64, 64),
                    "", "", "", 0, true, Capsule.UiEventMask.Activate, 0) });
            Assert.That(CapsuleDevelopmentHost.ValidDemoAction(snapshot, owner, button, 7, new byte[2]), Is.True);
            Assert.That(CapsuleDevelopmentHost.ValidDemoAction(snapshot, owner + 1, button, 7, new byte[2]), Is.False);
            Assert.That(CapsuleDevelopmentHost.ValidDemoAction(snapshot, owner, button, 8, new byte[2]), Is.False);
            Assert.That(CapsuleDevelopmentHost.ValidDemoAction(snapshot, owner, button, 7, new byte[1]), Is.False);
            Assert.That(CapsuleDevelopmentHost.ValidDemoAction(snapshot, owner, button + 1, 7, new byte[2]), Is.False);
            var text = new Capsule.UiSnapshot(1, new[] {
                new Capsule.UiNode(button, 0, Capsule.UiNodeKind.Text, new Capsule.UiRect(0, 0, 64, 64),
                    "", "", "", 0, true, Capsule.UiEventMask.Activate, 0) });
            Assert.That(CapsuleDevelopmentHost.ValidDemoAction(text, owner, button, 7, new byte[2]), Is.False);
        }
    }
}
#endif
