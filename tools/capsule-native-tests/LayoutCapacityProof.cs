using System.Text;
using Capsule;
using Capsule.Wasmtime;
using Capsule.Yoga;

static class LayoutCapacityProof
{
    sealed class Theme : IUiTheme
    {
        public bool AllowsStyle(string key) => string.IsNullOrEmpty(key);
        public bool AllowsAsset(string key) => string.IsNullOrEmpty(key);
    }
    sealed class Target : IUiCommitTarget, IUiObservationAdapter
    {
        public int Nodes;
        public bool TryCommit(UiSnapshot snapshot, out string error)
        { Nodes = snapshot.Nodes.Count; error = null; return true; }
        public bool TryDequeue(out ushort topic, out ushort schema, out byte[] payload)
        { topic = schema = 0; payload = null; return false; }
    }
    public static void Run(string carrierPath, string sdkPath)
    {
        var registry = new UiAddonRegistry();
        var registration = registry.Register("example.capacity", UiAddonTrust.FirstParty,
            new UiCapabilityGrant(Array.Empty<UiObservationCapability>(), Array.Empty<ushort>(), null));
        var activation = registry.Activate(registration);
        var target = new Target();
        var theme = new Theme();
        var source = Encoding.UTF8.GetBytes("const {React,Box,render}=__portableReact;" +
            "render(React.createElement(Box,{width:800,height:600},Array.from({length:700},(_,i)=>React.createElement(Box,{key:i,width:20,height:20}))),{layout:'yoga'});");
        using var guest = new YogaUiGuest(activation.OwnerNonce, theme, new YogaTextCatalog(new Dictionary<string, YogaFontMetrics>()), target,
            output => new WasmtimeUiGuest(File.ReadAllBytes(carrierPath), File.ReadAllBytes(sdkPath), source,
                activation, theme, output, target, null, hostCallsPerInvocation: UiHostBridgeContract.ExtendedCallsPerInvocation,
                layoutNodeLimit: UiSession.ExtendedLayoutNodes, executionFuel: ulong.MaxValue), layoutNodeLimit: UiSession.ExtendedLayoutNodes);
        if (!guest.Initialize(out var error) || !guest.Update(out error)) throw new Exception(error);
        if (target.Nodes != 701) throw new Exception("Incomplete core layout: " + target.Nodes);
        Console.WriteLine("PASS actual QuickJS/Wasmtime/Yoga core layout: 701 nodes across bounded packets");
    }
}
