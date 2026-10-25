using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using Capsule.Unity.Development;

namespace Capsule.Unity.Editor
{
    /// <summary>Creates the same opt-in host used by the Setup window and Editor automation.</summary>
    public static class CapsuleSetupHost
    {
        public static CapsuleDevelopmentHost Create(string endpoint, TMP_FontAsset font, bool enableDemoProvider)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Leave Play mode and wait for compilation before creating a host.");
            if (!CapsuleDevelopmentHost.TryEndpoint(endpoint, out _, out var error))
                throw new ArgumentException(error, nameof(endpoint));
            if (font == null) throw new ArgumentNullException(nameof(font), "Choose an imported TMP font asset first.");
            var carrier = AssetDatabase.LoadAssetAtPath<TextAsset>(CapsuleSetupChecks.CarrierPath);
            var sdk = AssetDatabase.LoadAssetAtPath<TextAsset>(CapsuleSetupChecks.ReactSdkPath);
            if (carrier == null || sdk == null)
                throw new InvalidOperationException("Install the carrier and React SDK package resources first.");

            var go = new GameObject("Capsule Development Host");
            go.SetActive(false);
            Undo.RegisterCreatedObjectUndo(go, "Create Capsule Development Host");
            var host = Undo.AddComponent<CapsuleDevelopmentHost>(go);
            Undo.RecordObject(host, "Configure Capsule Development Host");
            host.Endpoint = endpoint;
            host.Carrier = carrier;
            host.Sdk = sdk;
            host.Font = font;
            host.EnableDemoProvider = enableDemoProvider;
            host.enabled = false;
            Undo.RecordObject(go, "Activate Capsule Development Host object");
            go.SetActive(true);
            return host;
        }
    }
}
