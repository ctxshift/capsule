using System.Diagnostics;
using System.Text;
using Capsule;
using Capsule.Wasmtime;
using Capsule.Yoga;

static class LifecycleProof
{
    static int checks;
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); checks++; }
    sealed class Theme : IUiTheme
    {
        public bool AllowsStyle(string key) => key is "panel" or "label" or "button";
        public bool AllowsAsset(string key) => key == "";
    }
    sealed class Host : IDisposable
    {
        readonly byte[] carrier, sdk, bundle;
        readonly UiAddonRegistry registry;
        readonly UiAddonRegistration registration;
        readonly Theme theme = new Theme();
        readonly YogaTextCatalog metrics;
        uint candidateOwner;
        public readonly UiAsyncHostSession Session;
        public UiSnapshot Snapshot;
        public uint Owner;
        public int Requests, LastWorkerThread;
        public byte[] LastRequest;
        public Host(UiAsyncDispatcher dispatcher, UiAddonRegistry registry, string id, byte[] carrier, byte[] sdk, byte[] bundle)
        {
            this.registry = registry; this.carrier = carrier; this.sdk = sdk; this.bundle = bundle;
            registration = registry.Register(id, UiAddonTrust.Community,
                new UiCapabilityGrant(new[] { new UiObservationCapability(7, 1) }, new ushort[] { 7 }, null));
            Session = new UiAsyncHostSession(dispatcher);
            var advances = Enumerable.Range(32, 95).ToDictionary(code => code, _ => 8f);
            metrics = new YogaTextCatalog(new Dictionary<string, YogaFontMetrics> {
                ["label"] = new YogaFontMetrics(advances, new Dictionary<ulong, float>(), 16) });
        }
        public long Load(bool fail = false)
        {
            var activation = registry.Activate(registration); candidateOwner = activation.OwnerNonce;
            byte[] source = fail ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bundle) + "\nthrow new Error('candidate-rejected');") : bundle;
            return Session.RequestLoad((output, observations, actions) => new YogaUiGuest(activation.OwnerNonce,
                theme, metrics, output, layout => new WasmtimeUiGuest(carrier, sdk, source, activation,
                    theme, layout, observations, actions, scheduling: true)));
        }
        public UiAsyncCompletion Completion()
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(10))
            {
                if (Session.TryGetCompletion(out var result)) return result;
                if (Session.Worker.IsStopped) throw new Exception(Session.Worker.TerminalError ?? "Worker stopped before publication");
                Thread.Sleep(1);
            }
            throw new TimeoutException("Native publication deadline: " + Session.Worker.DiagnosticState);
        }
        public UiAsyncApplyResult Apply(UiAsyncCompletion completion)
        {
            LastWorkerThread = completion.WorkerThreadId;
            uint expectedOwner = completion.Candidate ? candidateOwner : Owner;
            var result = Session.TryApply(completion, (UiAsyncCompletion value, out string error) =>
            {
                error = null;
                return value.Snapshot == null || UiSnapshotValidator.Validate(value.Snapshot, expectedOwner, theme, out error);
            }, value =>
            {
                if (value.Snapshot != null) Snapshot = value.Snapshot;
                if (value.Candidate) Owner = candidateOwner;
            }, out var failure);
            if (result == UiAsyncApplyResult.Accepted)
            {
                foreach (var action in completion.Actions)
                {
                    // Mock game boundary: current generation, capability, source and payload
                    // are checked here; a request never grants gameplay authority.
                    Check(completion.Generation == Session.ActiveGeneration && action.Capability == 7 &&
                        Snapshot.Nodes.Any(node => node.Handle == action.Handle && node.Kind == UiNodeKind.Button) &&
                        action.CopyPayload().Length == 2, "Mock host validates request provenance");
                    Requests++; LastRequest = action.CopyPayload();
                }
                Check(completion.Fuel <= WasmtimeUiGuest.AddonExecutionFuel, "Publication call remains within existing fuel limit");
            }
            Console.WriteLine($"publication generation={completion.Generation} candidate={completion.Candidate} result={result} nodes={completion.Snapshot?.Nodes.Count ?? 0} layoutCalls={completion.LayoutCalculations} fuel={completion.Fuel} error={failure}");
            return result;
        }
        public void Accept() => Check(Apply(Completion()) == UiAsyncApplyResult.Accepted, "Native publication accepted");
        public bool HasText(string text) => Snapshot.Nodes.Any(node => node.Text == text);
        public ulong Button => Snapshot.Nodes.Single(node => node.Kind == UiNodeKind.Button).Handle;
        public void Observe(ushort value)
        {
            Check(Session.EnqueueObservation(Session.ActiveGeneration, 7, 1, new[] { (byte)value, (byte)(value >> 8) }), "Observation queued");
            Accept();
        }
        public void Click()
        {
            Check(Session.EnqueueEvent(Session.ActiveGeneration, Button, UiEventMask.Activate, Array.Empty<byte>()), "Native semantic activation queued");
            Accept();
        }
        public void Dispose() => Session.Dispose();
    }
    public static void Run(string carrierPath, string sdkPath, string bundlePath)
    {
        var carrier = File.ReadAllBytes(carrierPath); var sdk = File.ReadAllBytes(sdkPath); var bundle = File.ReadAllBytes(bundlePath);
        Check(WasmtimeUiGuest.AddonExecutionFuel == 200000000 && UiAsyncWorker.QueueCapacity == 128 && UiSession.MaxEventPayloadBytes == 256,
            "Existing finite limits preserved");
        Check(NativeYoga.LiveRoots == 0, "Native Yoga starts empty");
        using var dispatcher = new UiAsyncDispatcher(2);
        var registry = new UiAddonRegistry();
        using var first = new Host(dispatcher, registry, "example.first", carrier, sdk, bundle);
        using var second = new Host(dispatcher, registry, "example.second", carrier, sdk, bundle);
        first.Load(); var initial = first.Completion(); Check(initial.LayoutCalculations > 0, "Real native Yoga calculated React layout: " + initial.Error);
        Check(first.Apply(initial) == UiAsyncApplyResult.Accepted, "First addon accepted");
        second.Load(); second.Accept();
        var firstImports = first.Session.Worker.ReadHostCalls(out bool firstImportsAvailable);
        var secondImports = second.Session.Worker.ReadHostCalls(out bool secondImportsAvailable);
        Check(firstImportsAvailable && secondImportsAvailable && firstImports.Calls > 0 && firstImports.BytesRead > 0,
            "Yoga forwards native callback diagnostics through locked worker snapshot");
        Check(first.Owner != second.Owner && NativeYoga.LiveRoots == 2, "Two isolated native addon roots");
        Check(initial.WorkerThreadId == second.LastWorkerThread, "Shared dispatcher serializes native guests on one worker");
        Check(first.Snapshot.Nodes.Count == 5 && first.Snapshot.Nodes[0].Rect.Width == 240 * 64 &&
            first.Snapshot.Nodes[0].Rect.Height == 160 * 64 && first.Snapshot.Nodes[1].Rect.X == 10 * 64 &&
            first.Snapshot.Nodes[1].Rect.Width == 220 * 64 && first.Snapshot.Nodes.Single(node => node.Kind == UiNodeKind.Button).Rect.Y == 66 * 64,
            "Neutral declarative snapshot has exact native layout");
        first.Observe(513);
        Check(first.HasText("Charge 513") && second.HasText("Charge 0"), "Observation delivery isolated");
        first.Click();
        var deliveredImports = first.Session.Worker.ReadHostCalls(out _);
        Check(deliveredImports.Calls > firstImports.Calls && deliveredImports.BytesWritten >= firstImports.BytesWritten + 10 &&
            deliveredImports.ElapsedTicks >= firstImports.ElapsedTicks, "Observation/action callback work measured across updates");
        Check(second.Session.Worker.ReadHostCalls(out _).Calls == secondImports.Calls, "Per-addon callback metrics remain isolated");
        Check(first.HasText("Count 1") && first.Requests == 1 && first.LastRequest.SequenceEqual(new byte[] { 1, 2 }) &&
            second.HasText("Count 0") && second.Requests == 0, "React state and host request isolated");
        long priorGeneration = first.Session.ActiveGeneration; uint priorOwner = first.Owner; var retained = first.Snapshot;
        first.Load(fail: true);
        Check(first.Apply(first.Completion()) == UiAsyncApplyResult.Rejected && first.Session.ActiveGeneration == priorGeneration &&
            ReferenceEquals(first.Snapshot, retained), "Failed candidate preserves accepted UI");
        first.Click(); Check(first.HasText("Count 2"), "Old UI remains interactive after failed reload");
        first.Load(); first.Accept();
        Check(first.Owner != priorOwner && first.Session.ActiveGeneration != priorGeneration && first.HasText("Count 0"), "Successful reload resets state and owner");
        Check(!first.Session.EnqueueEvent(priorGeneration, initial.Snapshot.Nodes.Single(node => node.Kind == UiNodeKind.Button).Handle,
            UiEventMask.Activate, Array.Empty<byte>()), "Stale event rejected");
        Check(first.Apply(initial) == UiAsyncApplyResult.Stale && first.HasText("Count 0"), "Stale completion cannot replace UI");
        first.Load(); var superseded = first.Completion(); first.Load();
        Check(first.Apply(superseded) == UiAsyncApplyResult.Stale, "Superseded native candidate rejected"); first.Accept();
        first.Dispose();
        Check(SpinWait.SpinUntil(() => first.Session.Worker.IsStopped, 10000), "First store disposed within deadline");
        second.Observe(9); second.Click();
        Check(second.HasText("Count 1") && second.HasText("Charge 9") && NativeYoga.LiveRoots == 1, "Peer usable after first disposal");
        second.Dispose();
        Check(SpinWait.SpinUntil(() => second.Session.Worker.IsStopped, 10000) && NativeYoga.LiveRoots == 0, "All guest stores and Yoga roots retired");
        Check(first.Session.Worker.ReadCallFuel().Available && second.Session.Worker.ReadCallFuel().Available, "Actual runtime fuel evidence available");
        var retiredImports = first.Session.Worker.ReadHostCalls(out bool retiredAvailable);
        Check(retiredAvailable && retiredImports.Calls >= deliveredImports.Calls && retiredImports.BytesRead >= deliveredImports.BytesRead,
            "Reload, rejected candidate and disposal preserve cumulative copied diagnostics");
        Console.WriteLine($"PASS isolated native lifecycle: {checks} checks; cwd={Environment.CurrentDirectory}");
    }
}
