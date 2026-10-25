using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;

namespace Capsule.Unity.Editor
{
    [Serializable]
    public sealed class CapsuleWorkshopSettings
    {
        public string Bun = "bun";
        public string ToolingEntry = "";
        public string Project = "";
        public string Packages = "";
        public string ProjectId = "example.capsule";
        public string Endpoint = "http://127.0.0.1:5173";
        public int Port = 5173;
        public bool StarterDemoProvider = true;
    }

    public static class CapsuleWorkshopProcess
    {
        private static readonly Queue<string> Output = new Queue<string>();
        private static readonly object Gate = new object();
        private static Process owned;
        private static bool dev;
        private static bool stopping;
        private static double stopDeadline;
        private static double startedAt;
        private static string status = "Idle";
        private static string lastOutput = "";
        private static string readyEndpoint = "";

        public static string Status => status;
        public static string LastOutput => lastOutput;
        public static string ReadyEndpoint => readyEndpoint;
        public static bool Running => owned != null && !owned.HasExited;
        public static bool DevRunning => Running && dev;

        static CapsuleWorkshopProcess()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += StopForReload;
        }

        public static string BuildArguments(CapsuleWorkshopSettings settings, string command)
        {
            var parts = new List<string>();
            if (command == "install") parts.Add("install");
            else
            {
                if (string.IsNullOrWhiteSpace(settings.ToolingEntry))
                    parts.AddRange(new[] { "x", "--bun", "-p", "@capsule-sdk/workshop@0.1.0", "capsule-workshop" });
                else parts.Add(settings.ToolingEntry);
                parts.Add(command);
                parts.Add("--project"); parts.Add(settings.Project);
                if (command == "init")
                {
                    parts.Add("--id"); parts.Add(settings.ProjectId);
                    if (!string.IsNullOrWhiteSpace(settings.Packages)) { parts.Add("--packages"); parts.Add(settings.Packages); }
                }
                if (command == "dev") parts.Add("--watch-stdin");
                if (command == "init" || command == "dev") { parts.Add("--port"); parts.Add(settings.Port.ToString()); }
            }
            return string.Join(" ", parts.ConvertAll(Quote));
        }

        private static string Quote(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            // ProcessStartInfo does not invoke a shell; this preserves Windows argv boundaries.
            var result = new StringBuilder("\"");
            var slashes = 0;
            foreach (var character in value)
            {
                if (character == '\\') { slashes++; continue; }
                if (character == '"')
                {
                    result.Append('\\', slashes * 2 + 1).Append('"');
                    slashes = 0;
                    continue;
                }
                result.Append('\\', slashes).Append(character);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        public static void Start(CapsuleWorkshopSettings settings, string command)
        {
            if (Running) throw new InvalidOperationException("Stop the owned workshop command before starting another.");
            if (owned != null) { owned.Dispose(); owned = null; }
            if (string.IsNullOrWhiteSpace(settings.Bun)) throw new ArgumentException("Bun executable is required.");
            if (!Path.IsPathRooted(settings.Project)) throw new ArgumentException("Choose an absolute external project folder.");
            var unityRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            var external = Path.GetFullPath(settings.Project);
            var prefix = unityRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (external.Equals(unityRoot, StringComparison.OrdinalIgnoreCase) || external.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a project folder outside the Unity project.");
            if (command != "init" && !Directory.Exists(external)) throw new DirectoryNotFoundException("Create or choose the external project first.");
            if (!string.IsNullOrWhiteSpace(settings.ToolingEntry) && !File.Exists(settings.ToolingEntry))
                throw new FileNotFoundException("Tooling entry override is missing.", settings.ToolingEntry);
            if (!string.IsNullOrWhiteSpace(settings.ToolingEntry) && !Path.IsPathRooted(settings.ToolingEntry))
                throw new ArgumentException("Choose an absolute tooling entry override.");
            if (command == "init" && !string.IsNullOrWhiteSpace(settings.Packages) && !Directory.Exists(settings.Packages))
                throw new DirectoryNotFoundException("Local package archive folder is missing.");
            if (command == "init" && !string.IsNullOrWhiteSpace(settings.Packages) && !Path.IsPathRooted(settings.Packages))
                throw new ArgumentException("Choose an absolute local package archive folder.");
            if (settings.Port < 1 || settings.Port > 65535) throw new ArgumentOutOfRangeException(nameof(settings.Port));
            if (command != "init" && command != "inspect" && command != "dev" && command != "install")
                throw new ArgumentException("Unknown workshop command.");

            var start = new ProcessStartInfo(settings.Bun, BuildArguments(settings, command)) {
                WorkingDirectory = Directory.Exists(external) ? external : unityRoot,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, args) => Enqueue(args.Data);
            process.ErrorDataReceived += (_, args) => Enqueue(args.Data);
            try { if (!process.Start()) throw new InvalidOperationException("Bun did not start."); }
            catch { process.Dispose(); throw; }
            owned = process;
            dev = command == "dev";
            stopping = false;
            startedAt = EditorApplication.timeSinceStartup;
            status = "Running " + command;
            lastOutput = "";
            readyEndpoint = "";
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        private static void Enqueue(string line)
        { if (line != null) lock (Gate) Output.Enqueue(line); }

        private static void Update()
        {
            lock (Gate)
                while (Output.Count > 0)
                {
                    lastOutput = Output.Dequeue();
                    if (lastOutput.Contains("\"event\":\"ready\"") || lastOutput.Contains("\"event\": \"ready\""))
                    {
                        status = "Development server ready";
                        try { readyEndpoint = UnityEngine.JsonUtility.FromJson<ReadyLine>(lastOutput).endpoint ?? ""; }
                        catch (ArgumentException) { readyEndpoint = ""; }
                    }
                    else if (lastOutput.Contains("\"schemaVersion\"")) status = "Workshop response received";
                }
            if (owned == null) return;
            if (owned.HasExited)
            {
                status = stopping ? "Stopped" : "Exited " + owned.ExitCode;
                owned.Dispose(); owned = null; dev = false; stopping = false;
            }
            else if (stopping && EditorApplication.timeSinceStartup >= stopDeadline)
            {
                // The handle came only from Start; no process-name or port-wide kill is used.
                try { owned.Kill(); } catch (InvalidOperationException) { }
                stopDeadline = double.PositiveInfinity;
            }
            else if (!dev && EditorApplication.timeSinceStartup - startedAt > 120)
            {
                status = "Workshop command exceeded two minutes";
                try { owned.Kill(); } catch (InvalidOperationException) { }
            }
        }

        public static void Stop()
        {
            if (!Running || !dev || stopping) return;
            stopping = true;
            status = "Stopping owned development server";
            stopDeadline = EditorApplication.timeSinceStartup + 3;
            try { owned.StandardInput.Close(); }
            catch (InvalidOperationException) { }
        }

        private static void StopForReload()
        {
            if (owned == null) return;
            try { if (!owned.HasExited) { owned.StandardInput.Close(); if (!owned.WaitForExit(300)) owned.Kill(); } }
            finally { owned.Dispose(); owned = null; }
        }

        [Serializable]
        private sealed class ReadyLine { public string endpoint; }
    }
}
