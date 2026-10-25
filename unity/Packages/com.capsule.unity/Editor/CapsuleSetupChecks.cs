using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.PackageManager;
using UnityEngine;
using Capsule.Unity.Development;

namespace Capsule.Unity.Editor
{
    public enum CapsuleSetupFix { None, ImportTmpEssentials, OpenPlayerSettings, SetLinearColorSpace }

    public sealed class CapsuleSetupCheck
    {
        public readonly string Code;
        public readonly string Message;
        public readonly CapsuleSetupFix Fix;

        public CapsuleSetupCheck(string code, string message, CapsuleSetupFix fix = CapsuleSetupFix.None)
        { Code = code; Message = message; Fix = fix; }
    }

    public sealed class CapsuleSetupSnapshot
    {
        public readonly Dictionary<string, string> Versions = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, bool> NativeImporters = new Dictionary<string, bool>(StringComparer.Ordinal);
        public readonly Dictionary<string, bool> Resources = new Dictionary<string, bool>(StringComparer.Ordinal);
        public readonly Dictionary<string, bool> CompatibleBytes = new Dictionary<string, bool>(StringComparer.Ordinal);
        public bool CompatibilityManifestAvailable = true;
        public bool TmpSettings;
        public bool RunInBackground;
        public bool? NewInputEnabled;
        public ColorSpace ColorSpace;
    }

    public static class CapsuleSetupChecks
    {
        public const string CarrierPath = CapsuleDevelopmentCompatibility.CarrierPath;
        public const string ReactSdkPath = CapsuleDevelopmentCompatibility.ReactSdkPath;
        public const string YogaReactSdkPath = "Packages/com.capsule.wasmtime/Runtime/Resources/CapsuleYogaReactSdk.txt";
        public const string JsSdkPath = "Packages/com.capsule.wasmtime/Runtime/Resources/CapsuleJsSdk.txt";
        private const string NativeRoot = "Packages/";

        private static readonly KeyValuePair<string, string>[] RequiredPackages = {
            new KeyValuePair<string, string>("com.capsule.core", "0.1.0"),
            new KeyValuePair<string, string>("com.capsule.package", "0.1.0"),
            new KeyValuePair<string, string>("com.capsule.wasmtime", "0.1.0"),
            new KeyValuePair<string, string>("com.capsule.yoga", "0.1.0"),
            new KeyValuePair<string, string>("com.capsule.unity", "0.1.0"),
            new KeyValuePair<string, string>("com.unity.inputsystem", "1.19.0"),
            new KeyValuePair<string, string>("com.unity.ugui", "2.6.0"),
            new KeyValuePair<string, string>("com.unity.nuget.newtonsoft-json", "3.2.2")
        };

        public static readonly string[] OptionalFeaturePackages = {
            "com.capsule.features.rpg", "com.capsule.features.mmo", "com.capsule.features.skill-system"
        };

        private static readonly string[] NativePaths = {
            NativeRoot + "com.capsule.wasmtime/Runtime/Plugins/x86_64/wasmtime.dll",
            NativeRoot + "com.capsule.wasmtime/Runtime/Plugins/x86_64/portable_ui_wasmtime.dll",
            NativeRoot + "com.capsule.yoga/Runtime/Plugins/x86_64/portable_ui_yoga.dll"
        };

        private static readonly string[] ResourcePaths = { CarrierPath, ReactSdkPath, YogaReactSdkPath, JsSdkPath };

        public static CapsuleSetupSnapshot ReadCurrent()
        {
            var snapshot = new CapsuleSetupSnapshot();
            foreach (var package in RequiredPackages)
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForPackageName(package.Key);
                if (info != null) snapshot.Versions[package.Key] = info.version;
            }
            foreach (var name in OptionalFeaturePackages)
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForPackageName(name);
                if (info != null) snapshot.Versions[name] = info.version;
            }
            foreach (var path in NativePaths)
            {
                var importer = AssetImporter.GetAtPath(path) as PluginImporter;
                snapshot.NativeImporters[path] = importer != null &&
                    !importer.GetCompatibleWithAnyPlatform() && importer.GetCompatibleWithEditor() &&
                    importer.GetEditorData("OS") == "Windows" && importer.GetEditorData("CPU") == "x86_64" &&
                    importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64);
            }
            foreach (var path in ResourcePaths)
                snapshot.Resources[path] = AssetDatabase.LoadAssetAtPath<TextAsset>(path) != null;
            foreach (var path in new[] { CarrierPath, ReactSdkPath })
                ReadCompatibility(snapshot, path, AssetDatabase.LoadAssetAtPath<TextAsset>(path)?.bytes);
            foreach (var path in NativePaths)
            {
                var physical = PhysicalPackagePath(path);
                ReadCompatibility(snapshot, path, physical != null && File.Exists(physical) ? File.ReadAllBytes(physical) : null);
            }
            snapshot.TmpSettings = HasTmpSettings();
            snapshot.RunInBackground = PlayerSettings.runInBackground;
            snapshot.NewInputEnabled = ReadNewInputEnabled();
            snapshot.ColorSpace = PlayerSettings.colorSpace;
            return snapshot;
        }

        private static string PhysicalPackagePath(string assetPath)
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            if (info == null || string.IsNullOrEmpty(info.resolvedPath)) return null;
            var prefix = "Packages/" + info.name + "/";
            if (!assetPath.StartsWith(prefix, StringComparison.Ordinal)) return null;
            return Path.Combine(info.resolvedPath, assetPath.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar));
        }

        private static void ReadCompatibility(CapsuleSetupSnapshot snapshot, string path, byte[] bytes)
        {
            if (!CapsuleDevelopmentCompatibility.TryExpectedHash(path, out var expected))
            { snapshot.CompatibilityManifestAvailable = false; return; }
            if (bytes == null) return; // A missing asset is reported separately.
            using (var sha = SHA256.Create())
            {
                var actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                snapshot.CompatibleBytes[path] = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool? ReadNewInputEnabled()
        {
            // Read the actual serialized value; Input System's convenience getter treats a missing property as enabled.
            try
            {
                var profileType = typeof(BuildProfile);
                var global = profileType.GetField("s_GlobalPlayerSettings", BindingFlags.Static | BindingFlags.NonPublic);
                var player = global?.GetValue(null) as PlayerSettings;
                var active = BuildProfile.GetActiveBuildProfile();
                if (active != null)
                {
                    var overrideField = profileType.GetField("m_PlayerSettings", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (overrideField == null) return null;
                    player = overrideField.GetValue(active) as PlayerSettings ?? player;
                }
                if (player == null) return null;
                var serialized = new SerializedObject(player);
                var property = serialized.FindProperty("activeInputHandler");
                if (property == null) return null;
                return property.intValue == 1 ? true : property.intValue == 2 ? true :
                    property.intValue == 0 ? false : (bool?)null;
            }
            catch { return null; }
        }

        internal static bool HasTmpSettings()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_Settings", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (path.EndsWith("/Resources/TMP Settings.asset", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static List<CapsuleSetupCheck> Evaluate(CapsuleSetupSnapshot snapshot)
        {
            var issues = new List<CapsuleSetupCheck>();
            foreach (var package in RequiredPackages)
            {
                if (!snapshot.Versions.TryGetValue(package.Key, out var actual))
                    issues.Add(new CapsuleSetupCheck("package.missing", package.Key + " is missing. Install the declared package version."));
                else if (actual != package.Value)
                    issues.Add(new CapsuleSetupCheck("package.version", package.Key + " is " + actual + "; expected " + package.Value + "."));
            }
            foreach (var path in NativePaths)
                if (!snapshot.NativeImporters.TryGetValue(path, out var valid) || !valid)
                    issues.Add(new CapsuleSetupCheck("native.importer", path + " must import for Windows x64 Editor and Player. Reinstall the package archive."));
            foreach (var path in ResourcePaths)
                if (!snapshot.Resources.TryGetValue(path, out var present) || !present)
                    issues.Add(new CapsuleSetupCheck("runtime.resource", path + " is missing. Reinstall the package archive."));
            if (!snapshot.CompatibilityManifestAvailable)
                issues.Add(new CapsuleSetupCheck("compatibility.manifest", "The shipped development compatibility manifest is missing or invalid. Reinstall the Capsule Unity package archive."));
            foreach (var pair in snapshot.CompatibleBytes)
                if (!pair.Value)
                    issues.Add(new CapsuleSetupCheck("compatibility.mismatch", pair.Key + " differs from the shipped compatibility manifest. Reinstall the affected package archive."));
            if (!snapshot.TmpSettings)
                issues.Add(new CapsuleSetupCheck("tmp.settings", "TMP Essential Resources are missing.", CapsuleSetupFix.ImportTmpEssentials));
            if (!snapshot.RunInBackground)
                issues.Add(new CapsuleSetupCheck("background.run", "Enable Run In Background in Player Settings so the game continues updating while you edit React in another window.", CapsuleSetupFix.OpenPlayerSettings));
            if (snapshot.NewInputEnabled != true)
                issues.Add(new CapsuleSetupCheck("input.handler", snapshot.NewInputEnabled == null
                    ? "Active Input Handling could not be read; inspect Player Settings."
                    : "Enable Input System or Both in Player Settings, then restart the Editor.", CapsuleSetupFix.OpenPlayerSettings));
            if (snapshot.ColorSpace != ColorSpace.Linear)
                issues.Add(new CapsuleSetupCheck("color.space", "Use Linear color space for Capsule presentation.", CapsuleSetupFix.SetLinearColorSpace));
            return issues;
        }

        public static bool TryApply(CapsuleSetupFix fix)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return false;
            switch (fix)
            {
                case CapsuleSetupFix.ImportTmpEssentials:
                    if (HasTmpSettings()) return false;
                    if (CapsuleTmpImportMonitor.InFlight) return false;
                    if (!EditorUtility.DisplayDialog("Import TMP Essentials", "Import TMP Essential Resources into this project? Scenes will not be saved or modified.", "Import", "Cancel")) return false;
                    CapsuleTmpImportMonitor.ImportEssentials();
                    return true;
                case CapsuleSetupFix.OpenPlayerSettings:
                    SettingsService.OpenProjectSettings("Project/Player");
                    return true;
                case CapsuleSetupFix.SetLinearColorSpace:
                    if (!EditorUtility.DisplayDialog("Set Linear Color Space", "Change project color space to Linear? Unity may reimport shaders and assets.", "Change", "Cancel")) return false;
                    PlayerSettings.colorSpace = ColorSpace.Linear;
                    return true;
                default: return false;
            }
        }
    }
}
