#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Capsule.Package;
using Capsule.Wasmtime;
using Capsule.Yoga;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Capsule.Unity.Development
{
    // Explicitly enabled local authoring bridge. React component state starts fresh
    // on each admitted generation; gameplay observations are re-sent by the host.
    public sealed class CapsuleDevelopmentHost : MonoBehaviour
    {
        const int PackageBytes = 900000, ArtBytes = 4194304, MaxArtBytes = 16777216;
        static readonly HttpClient Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(4) };
        public string Endpoint = "http://127.0.0.1:5173";
        public TextAsset Carrier;
        public TextAsset Sdk;
        public TMP_FontAsset Font;
        public RectTransform CanvasRoot;
        public bool CreateCanvasAndEventSystem = true;
        public bool EnableDemoProvider;
        public float PollSeconds = .05f;
        public string LastError { get; private set; }
        public string ActiveRevision { get; private set; }
        public long Generation => session?.ActiveGeneration ?? 0;
        public UiSnapshot LastSnapshot { get; private set; }
        public int DemoRequestCount { get; private set; }
        public double FirstActivationMilliseconds { get; private set; }
        public double LastPreparationMilliseconds { get; private set; }
        public event Action<ushort, byte[]> ActionReceived;

        UiAsyncHostSession session;
        CapsuleNativeTarget active, candidate;
        UiThemeSnapshot candidatePolicy;
        uint candidateOwner, activeOwner;
        string candidateHash, pendingHash, failedHash;
        Task<Publication> fetch;
        CancellationTokenSource fetchCancellation;
        readonly Dictionary<(ushort Topic, ushort Schema), byte[]> observations = new Dictionary<(ushort, ushort), byte[]>();
        float nextPoll;
        GameObject ownedCanvas, ownedEventSystem;
        EventSystem previousEventSystem;
        Uri baseUri;
        bool stopping;
        long enabledAt, preparationAt;
        static double MillisecondsSince(long started) =>
            (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency;

        sealed class Publication
        {
            internal string Hash, AddonId, Bundle, Sdk, Error;
            internal UiControlTheme Theme;
            internal YogaTextCatalog Metrics;
            internal List<(UiControlTheme.AssetRecipe Recipe, byte[] Data)> Images;
        }

        public bool EnqueueObservation(ushort topic, ushort schema, byte[] payload)
        {
            if (!EnableDemoProvider || topic != 7 || schema != 1 || payload == null || payload.Length != 2) return false;
            var key = (topic, schema);
            if (!observations.ContainsKey(key) && observations.Count >= 64) return false;
            long retained = payload.Length;
            foreach (var item in observations) if (item.Key != key) retained += item.Value.Length;
            if (retained > UiHostBridgeContract.MaxObservationBytes) return false;
            var copy = (byte[])payload.Clone();
            observations[key] = copy;
            return session == null || session.ActiveGeneration == 0 ||
                session.EnqueueObservation(session.ActiveGeneration, topic, schema, (byte[])copy.Clone());
        }

        void OnEnable()
        {
            stopping = false; LastError = null; fetch = null; failedHash = null;
            enabledAt = System.Diagnostics.Stopwatch.GetTimestamp(); FirstActivationMilliseconds = 0;
            if (!TryEndpoint(Endpoint, out baseUri, out var error)) { LastError = error; enabled = false; return; }
            if (!Carrier || !Sdk || !Font) { LastError = "Carrier, SDK and TMP font are required."; enabled = false; return; }
            if (!CapsuleDevelopmentCompatibility.Validate(Carrier, Sdk, out error))
            { LastError = error; enabled = false; return; }
            try
            {
                if (!CanvasRoot) CreateSurface();
                if (!EventSystem.current)
                {
                    if (!CreateCanvasAndEventSystem) throw new InvalidOperationException("An EventSystem is required for development controls.");
                    CreateEvents();
                }
                session = new UiAsyncHostSession();
                nextPoll = 0;
            }
            catch (Exception failure) { LastError = failure.Message; enabled = false; }
        }

        void CreateSurface()
        {
            if (!CreateCanvasAndEventSystem) throw new InvalidOperationException("Assign a CanvasRoot or enable owned surface creation.");
            ownedCanvas = new GameObject("Capsule development canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            CanvasRoot = (RectTransform)ownedCanvas.transform;
            var canvas = ownedCanvas.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 200;
        }

        void CreateEvents()
        {
            previousEventSystem = EventSystem.current;
            ownedEventSystem = new GameObject("Capsule development events", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        void Update()
        {
            if (session == null || stopping) return;
            if (fetch != null && fetch.IsCompleted)
            {
                var done = fetch; fetch = null;
                fetchCancellation?.Dispose(); fetchCancellation = null;
                try
                {
                    var publication = done.GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(publication.Error)) LastError = publication.Error;
                    else if (publication.Hash != ActiveRevision && publication.Hash != pendingHash && publication.Hash != failedHash) Prepare(publication);
                    else if (publication.Hash != failedHash) LastError = null;
                }
                catch (Exception failure) { LastError = failure.Message; }
            }
            if (fetch == null && Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + Mathf.Clamp(PollSeconds, .025f, 10f);
                string endpoint = baseUri.AbsoluteUri;
                // Task work receives only copied strings and bytes, never Unity objects.
                byte[] sdkBytes = (byte[])Sdk.bytes.Clone();
                fetchCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                fetch = Fetch(endpoint, sdkBytes, ActiveRevision, pendingHash, failedHash, fetchCancellation.Token);
            }
            if (session.TryGetCompletion(out var completion)) Apply(completion);
            if (session.IsRetired) { LastError = session.RetirementReason; enabled = false; }
        }

        void Prepare(Publication publication)
        {
            preparationAt = System.Diagnostics.Stopwatch.GetTimestamp();
            CapsuleDevelopmentWidgetFactory factory = null;
            CapsuleNativeTarget target = null;
            try
            {
                // Keep every accepted revision's images alive with its native target.
                factory = new CapsuleDevelopmentWidgetFactory(publication.Theme, Font);
                foreach (var image in publication.Images) factory.AddImage(image.Recipe, image.Data);
                target = new CapsuleNativeTarget(CanvasRoot, factory);
                var styles = new List<string>(publication.Theme.StyleKeys);
                var assets = new List<string>(); foreach (var image in publication.Images) assets.Add(image.Recipe.Key);
                candidatePolicy = new UiThemeSnapshot(styles, assets);
                var registry = new UiAddonRegistry();
                var grant = EnableDemoProvider
                    ? new UiCapabilityGrant(new[] { new UiObservationCapability(7, 1) }, new ushort[] { 7 }, null)
                    : new UiCapabilityGrant(null, null, null);
                var registration = registry.Register(publication.AddonId, UiAddonTrust.Community, grant);
                var activation = registry.Activate(registration);
                candidateOwner = activation.OwnerNonce;
                var carrier = (byte[])Carrier.bytes.Clone();
                var sdk = Encoding.UTF8.GetBytes(publication.Sdk);
                var bundle = Encoding.UTF8.GetBytes(publication.Bundle);
                var policy = candidatePolicy;
                var metrics = publication.Metrics;
                long targetGeneration = session.RequestLoad((output, observations, actions) => new YogaUiGuest(activation.OwnerNonce,
                    policy, metrics, output, layout => new WasmtimeUiGuest(carrier, sdk, bundle, activation,
                        policy, layout, observations, actions, scheduling: true)));
                target.InputSink = (handle, kind, payload) => session != null &&
                    session.EnqueueEvent(targetGeneration, handle, kind, payload);
                candidate?.Dispose(); candidate = target;
                candidateHash = pendingHash = publication.Hash;
                LastError = null;
            }
            catch (Exception failure)
            {
                target?.Dispose(); if (target == null) factory?.Dispose();
                failedHash = publication.Hash; LastError = failure.Message;
            }
        }

        void Apply(UiAsyncCompletion completion)
        {
            var sourceSnapshot = completion.Candidate ? null : LastSnapshot;
            var target = completion.Candidate ? candidate : active;
            var policy = completion.Candidate ? candidatePolicy : activePolicy;
            uint owner = completion.Candidate ? candidateOwner : activeOwner;
            var result = session.TryApply(completion, (UiAsyncCompletion value, out string error) =>
            {
                error = null;
                if (target == null || value.Snapshot == null ||
                    !UiSnapshotValidator.Validate(value.Snapshot, owner, policy, out error)) return false;
                foreach (var node in value.Snapshot.Nodes)
                    if (node.Kind != UiNodeKind.Box && node.Kind != UiNodeKind.ClipBox && node.Kind != UiNodeKind.Button &&
                        node.Kind != UiNodeKind.Text && node.Kind != UiNodeKind.Image)
                    { error = "Unsupported development node: " + node.Kind; return false; }
                return target.TryCommit(value.Snapshot, out error);
            }, value =>
            {
                if (value.Candidate)
                {
                    var old = active; active = candidate; candidate = null;
                    activeOwner = candidateOwner; activePolicy = candidatePolicy;
                    ActiveRevision = candidateHash; pendingHash = candidateHash = null;
                    active.Activate(); old?.SuspendInput(); old?.Dispose();
                    LastPreparationMilliseconds = MillisecondsSince(preparationAt);
                    if (FirstActivationMilliseconds == 0) FirstActivationMilliseconds = MillisecondsSince(enabledAt);
                }
                LastSnapshot = value.Snapshot;
            }, out var error);
            if (result == UiAsyncApplyResult.Rejected)
            {
                LastError = error;
                if (completion.Candidate) { failedHash = candidateHash; candidate?.Dispose(); candidate = null; pendingHash = null; }
            }
            if (result != UiAsyncApplyResult.Accepted) return;
            if (completion.Candidate)
            {
                if (observations.Count == 0 && EnableDemoProvider) EnqueueObservation(7, 1, DemoPayload());
                else foreach (var item in observations)
                    session.EnqueueObservation(session.ActiveGeneration, item.Key.Topic, item.Key.Schema, (byte[])item.Value.Clone());
            }
            foreach (var action in completion.Actions)
            {
                var bytes = action.CopyPayload();
                if (completion.Generation != session.ActiveGeneration ||
                    !ValidDemoAction(sourceSnapshot ?? LastSnapshot, activeOwner, action.Handle, action.Capability, bytes))
                { LastError = "Development action source or payload rejected."; continue; }
                try { ActionReceived?.Invoke(action.Capability, (byte[])bytes.Clone()); }
                catch (Exception failure) { LastError = "Development action provider failed: " + failure.Message; }
                if (EnableDemoProvider && action.Capability == 7)
                {
                    if (DemoRequestCount < int.MaxValue) DemoRequestCount++;
                    EnqueueObservation(7, 1, DemoPayload());
                }
            }
        }
        byte[] DemoPayload()
        {
            int count = Mathf.Min(DemoRequestCount, ushort.MaxValue);
            return new[] { (byte)count, (byte)(count >> 8) };
        }
        internal static bool ValidDemoAction(UiSnapshot snapshot, uint owner, ulong handle, ushort capability, byte[] payload)
        {
            if (snapshot == null || owner == 0 || (uint)(handle >> 32) != owner ||
                capability != 7 || payload == null || payload.Length != 2) return false;
            foreach (var node in snapshot.Nodes)
                if (node.Handle == handle) return node.Kind == UiNodeKind.Button && node.Visible &&
                    (node.Events & UiEventMask.Activate) != 0;
            return false;
        }
        UiThemeSnapshot activePolicy;

        void OnDisable()
        {
            stopping = true;
            if (fetchCancellation != null)
            {
                var cancellation = fetchCancellation;
                var pending = fetch;
                cancellation.Cancel();
                if (pending == null) cancellation.Dispose();
                else pending.ContinueWith(task => { var ignored = task.Exception; cancellation.Dispose(); },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                fetchCancellation = null;
            }
            session?.Dispose(); session = null;
            candidate?.Dispose(); candidate = null; fetch = null;
            active?.Dispose(); active = null;
            if (ownedEventSystem)
            {
                ownedEventSystem.SetActive(false);
                if (previousEventSystem && previousEventSystem.isActiveAndEnabled) EventSystem.current = previousEventSystem;
                DestroyOwned(ownedEventSystem);
            }
            ownedEventSystem = null;
            previousEventSystem = null;
            if (ownedCanvas) { DestroyOwned(ownedCanvas); CanvasRoot = null; }
            ownedCanvas = null;
            pendingHash = null; LastSnapshot = null; ActiveRevision = null;
        }

        static void DestroyOwned(GameObject obj)
        { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }

        public static bool TryEndpoint(string value, out Uri uri, out string error)
        {
            uri = null; error = null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttp ||
                parsed.UserInfo.Length != 0 || parsed.Query.Length != 0 || parsed.Fragment.Length != 0 ||
                parsed.AbsolutePath != "/" || !(parsed.Host == "localhost" || parsed.Host == "127.0.0.1" || parsed.Host.Trim('[', ']') == "::1"))
            { error = "Development endpoint must be an explicit loopback HTTP origin."; return false; }
            uri = parsed; return true;
        }

        static async Task<byte[]> ReadBounded(Uri address, int maximum, CancellationToken cancellation)
        {
            using (var response = await Http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
            using (cancellation.Register(() => response.Dispose()))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > maximum) throw new InvalidOperationException("Development response quota exceeded.");
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[16384]; int length;
                    while ((length = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) != 0)
                    { if (output.Length + length > maximum) throw new InvalidOperationException("Development response quota exceeded."); output.Write(buffer, 0, length); }
                    return output.ToArray();
                }
            }
        }

        static async Task<Publication> Fetch(string endpoint, byte[] embeddedSdk, string activeHash, string candidateHash, string failedHash, CancellationToken cancellation)
        {
            var origin = new Uri(endpoint);
            var json = JObject.Parse(Encoding.UTF8.GetString(await ReadBounded(new Uri(origin, "/capsule/dev/v1/package"), PackageBytes, cancellation).ConfigureAwait(false)));
            if ((int?)json["schemaVersion"] != 1 || (int?)json["apiVersion"] != 1 ||
                !(json["requires"] is JObject requires) || requires.Count != 0 ||
                (json["features"] != null && (!(json["features"] is JArray features) || features.Count != 0)))
                throw new InvalidOperationException("Unsupported development package contract or required features.");
            string hash = (string)json["hash"], sdkHash = (string)json["sdkSha256"];
            if (!Hex64(hash) || !Hex64(sdkHash)) throw new InvalidOperationException("Invalid development publication hash.");
            string sdk = (string)json["sdk"], bundle = (string)json["bundle"], addonId = (string)json["addonId"];
            if (sdk == null || bundle == null || string.IsNullOrEmpty(addonId) || addonId.Length > 96 ||
                Encoding.UTF8.GetByteCount(sdk) > 524288 || Encoding.UTF8.GetByteCount(bundle) > 65536)
                throw new InvalidOperationException("Development source quota or identity invalid.");
            if (!string.Equals(Sha(Encoding.UTF8.GetBytes(sdk)), sdkHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Sha(embeddedSdk), sdkHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Development SDK differs from the embedded SDK.");
            var publication = new Publication { Hash = hash, Sdk = sdk, Bundle = bundle, AddonId = addonId,
                Error = (bool?)json["compiler"]?["success"] == false ? (string)json["compiler"]?["error"] ?? "Compilation failed." : null };
            if (publication.Error != null || hash == activeHash || hash == candidateHash || hash == failedHash)
                return publication;
            publication.Theme = new UiControlTheme(json["theme"]?.ToString() ?? throw new InvalidOperationException("Theme missing."));
            var rawMetrics = json["metrics"] as JObject ?? throw new InvalidOperationException("Font metrics missing.");
            var advances = new Dictionary<int, float>();
            foreach (var item in rawMetrics["advances"] as JArray ?? throw new InvalidOperationException("Glyph metrics missing."))
                advances.Add((int)item["code"], (float)item["value"]);
            var pairs = new Dictionary<ulong, float>();
            foreach (var item in rawMetrics["pairs"] as JArray ?? new JArray())
                pairs.Add(YogaFontMetrics.PairKey((int)item["first"], (int)item["second"]), (float)item["value"]);
            var fonts = new Dictionary<string, YogaFontMetrics>(StringComparer.Ordinal);
            foreach (string key in publication.Theme.TextKeys)
                fonts.Add(key, new YogaFontMetrics(advances, pairs, publication.Theme.TextNumber(key, "lineHeight")));
            publication.Metrics = new YogaTextCatalog(fonts);
            publication.Images = new List<(UiControlTheme.AssetRecipe, byte[])>();
            int total = 0;
            foreach (var recipe in publication.Theme.CopyAssets())
            {
                string key = Uri.EscapeDataString(recipe.ImageKey);
                var data = await ReadBounded(new Uri(origin, "/portable-ui/art/" + hash + "/" + key), ArtBytes, cancellation).ConfigureAwait(false);
                total += data.Length; if (total > MaxArtBytes) throw new InvalidOperationException("Theme art quota exceeded.");
                publication.Images.Add((recipe, data));
            }
            return publication;
        }
        static bool Hex64(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
        static string Sha(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
    }
}
#endif
