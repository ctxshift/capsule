using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Capsule.Unity.Editor
{
    public sealed class CapsuleSetupWindow : EditorWindow
    {
        private CapsuleWorkshopSettings settings;
        private System.Collections.Generic.List<CapsuleSetupCheck> checks;
        private Vector2 scroll;
        private string actionError = "";
        private string optionalSummary = "";
        [SerializeField] private TMP_FontAsset font;

        [MenuItem("Capsule/Setup")]
        public static void Open() => GetWindow<CapsuleSetupWindow>("Capsule Setup");

        private static string SettingsKey()
        {
            var bytes = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(Path.Combine(Application.dataPath, ".."))));
            return "Capsule.Setup." + BitConverter.ToString(bytes, 0, 8).Replace("-", "");
        }

        private void OnEnable()
        {
            var saved = EditorPrefs.GetString(SettingsKey(), "");
            settings = string.IsNullOrEmpty(saved) ? new CapsuleWorkshopSettings() : JsonUtility.FromJson<CapsuleWorkshopSettings>(saved);
            if (settings == null) settings = new CapsuleWorkshopSettings();
            if (!string.IsNullOrEmpty(saved) && !saved.Contains("StarterDemoProvider")) settings.StarterDemoProvider = true;
            Refresh();
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
            Save();
        }

        private void Save() => EditorPrefs.SetString(SettingsKey(), JsonUtility.ToJson(settings));

        private void Refresh()
        {
            try
            {
                var snapshot = CapsuleSetupChecks.ReadCurrent();
                checks = CapsuleSetupChecks.Evaluate(snapshot);
                var optional = new System.Collections.Generic.List<string>();
                foreach (var name in CapsuleSetupChecks.OptionalFeaturePackages)
                    optional.Add(name + (snapshot.Versions.TryGetValue(name, out var version) ? " " + version : " (not installed)"));
                optionalSummary = string.Join(", ", optional);
                actionError = "";
            }
            catch (Exception error) { checks = new System.Collections.Generic.List<CapsuleSetupCheck>(); actionError = error.Message; }
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Package and project checks", EditorStyles.boldLabel);
            if (GUILayout.Button("Refresh checks")) Refresh();
            if (checks == null) Refresh();
            if (checks.Count == 0) EditorGUILayout.HelpBox("All setup checks passed.", MessageType.Info);
            if (!string.IsNullOrEmpty(CapsuleTmpImportMonitor.Status))
                EditorGUILayout.HelpBox(CapsuleTmpImportMonitor.Status, CapsuleTmpImportMonitor.InFlight ? MessageType.Warning : MessageType.Info);
            EditorGUILayout.HelpBox("Optional feature packages: " + optionalSummary, MessageType.None);
            foreach (var check in checks)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(check.Code, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(check.Message, EditorStyles.wordWrappedLabel);
                if (check.Fix != CapsuleSetupFix.None && GUILayout.Button(FixLabel(check.Fix)))
                {
                    try { if (CapsuleSetupChecks.TryApply(check.Fix)) Refresh(); }
                    catch (Exception error) { actionError = error.Message; }
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("External React project", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            settings.Bun = EditorGUILayout.TextField("Bun executable", settings.Bun);
            settings.ToolingEntry = EditorGUILayout.TextField("Tooling entry override", settings.ToolingEntry);
            settings.Project = EditorGUILayout.TextField("Project folder", settings.Project);
            settings.Packages = EditorGUILayout.TextField("Local package archives", settings.Packages);
            settings.ProjectId = EditorGUILayout.TextField("Project ID", settings.ProjectId);
            settings.Port = EditorGUILayout.IntField("Development port", settings.Port);
            settings.Endpoint = EditorGUILayout.TextField("Host endpoint", settings.Endpoint);
            if (EditorGUI.EndChangeCheck()) Save();
            EditorGUILayout.HelpBox("Leave tooling entry empty for the published workshop CLI. The project and package folders must be outside this Unity project. Settings stay in EditorPrefs.", MessageType.None);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create React project")) Run("init");
            if (GUILayout.Button("Connect / inspect")) Run("inspect");
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Install project dependencies")) Run("install");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Start development server")) Run("dev");
            using (new EditorGUI.DisabledScope(!CapsuleWorkshopProcess.DevRunning))
                if (GUILayout.Button("Stop owned server")) CapsuleWorkshopProcess.Stop();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Process", CapsuleWorkshopProcess.Status);
            if (!string.IsNullOrEmpty(CapsuleWorkshopProcess.ReadyEndpoint) &&
                GUILayout.Button("Use ready endpoint for host"))
            { settings.Endpoint = CapsuleWorkshopProcess.ReadyEndpoint; Save(); }
            if (!string.IsNullOrEmpty(CapsuleWorkshopProcess.LastOutput))
                EditorGUILayout.SelectableLabel(CapsuleWorkshopProcess.LastOutput, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(35));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Development host", EditorStyles.boldLabel);
            font = (TMP_FontAsset)EditorGUILayout.ObjectField("Font", font, typeof(TMP_FontAsset), false);
            EditorGUI.BeginChangeCheck();
            settings.StarterDemoProvider = EditorGUILayout.ToggleLeft("Starter demo provider (request 7 / observation 7)", settings.StarterDemoProvider);
            if (EditorGUI.EndChangeCheck()) Save();
            EditorGUILayout.HelpBox("Create an opt-in host in the active scene. It remains disabled until you enable the component. No scene is saved automatically.", MessageType.None);
            if (GUILayout.Button("Create disabled development host")) CreateHost();
            if (!string.IsNullOrEmpty(actionError)) EditorGUILayout.HelpBox(actionError, MessageType.Error);
            EditorGUILayout.EndScrollView();
        }

        private static string FixLabel(CapsuleSetupFix fix)
        {
            switch (fix)
            {
                case CapsuleSetupFix.ImportTmpEssentials: return "Import TMP Essentials";
                case CapsuleSetupFix.OpenPlayerSettings: return "Open Player Settings";
                case CapsuleSetupFix.SetLinearColorSpace: return "Set Linear color space";
                default: return "Fix";
            }
        }

        private void Run(string command)
        {
            try { CapsuleWorkshopProcess.Start(settings, command); actionError = ""; }
            catch (Exception error) { actionError = error.Message; }
        }

        private void CreateHost()
        {
            try
            {
                var host = CapsuleSetupHost.Create(settings.Endpoint, font, settings.StarterDemoProvider);
                Selection.activeGameObject = host.gameObject;
                actionError = "";
            }
            catch (Exception error) { actionError = error.Message; }
        }
    }
}
