using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Capsule.Release.Tests
{
    public sealed class CapsuleReleaseProbe
    {
        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        [Test]
        public void AllPackedAssembliesImportAndExposeContracts()
        {
            var types = new[] {
                typeof(Capsule.UiSession), typeof(Capsule.Package.UiPackageManifest),
                typeof(Capsule.Wasmtime.WasmtimeUiGuest), typeof(Capsule.Yoga.NativeYoga),
                typeof(Capsule.Unity.CapsuleSurfaceView), typeof(Rpg.Features.TQuestPrerequisites),
                typeof(Mmo.Features.TChatSend), typeof(SkillSystem.Features.TTrainingIntent)
            };
            var assemblies = new[] { "Capsule.Core", "Capsule.Package", "Capsule.Wasmtime", "Capsule.Yoga",
                "Capsule.Unity", "Rpg.Features", "Mmo.Features", "SkillSystem.Features" };
            for (var i = 0; i < types.Length; i++)
                Assert.AreEqual(assemblies[i], types[i].Assembly.GetName().Name);
        }

__PACKAGE_CASES__
        public void PackagesResolveFromEmbeddedBundle(string name, string version)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + name + "/package.json");
            Assert.IsNotNull(package);
            Assert.AreEqual(version, package.version);
            Assert.AreEqual(PackageSource.Embedded, package.source);
            Assert.AreEqual(Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/", name)), Path.GetFullPath(package.resolvedPath));
        }

__RESOURCE_CASES__
        public void ImportedResourcesMatchReleasedBytes(string path, string hash)
        {
            var resource = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.IsNotNull(resource, path);
            Assert.AreEqual(hash, Hash(resource.bytes), path);
        }

__PLUGIN_CASES__
        public void NativePluginsPreserveBytesAndWindowsImportSettings(string path, string hash)
        {
            var plugin = AssetImporter.GetAtPath(path) as PluginImporter;
            Assert.IsNotNull(plugin, path);
            Assert.AreEqual(hash, Hash(File.ReadAllBytes(path)), path);
            Assert.IsTrue(plugin.GetCompatibleWithEditor(), path);
            Assert.AreEqual("Windows", plugin.GetEditorData("OS"), path);
            Assert.AreEqual("x86_64", plugin.GetEditorData("CPU"), path);
            Assert.IsTrue(plugin.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64), path);
        }

__GUID_CASES__
        public void ImportedAssetsPreserveReleasedGuids(string path, string guid)
        {
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path), path);
        }
    }
}
