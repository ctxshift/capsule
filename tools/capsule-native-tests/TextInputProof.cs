using System.Text;
using Capsule;
using Capsule.Wasmtime;

static class TextInputProof
{
    sealed class Host : IUiTheme, IUiCommitTarget, IUiActionAdapter, IUiObservationAdapter
    {
        public UiSnapshot Snapshot;
        public readonly List<(ulong Source, ushort Capability, byte[] Payload)> Requests = new();
        public bool AllowsStyle(string key) => key == "";
        public bool AllowsAsset(string key) => key == "";
        public bool TryCommit(UiSnapshot snapshot, out string error) { Snapshot = snapshot; error = null!; return true; }
        public bool TryRequest(ushort capability, ulong source, byte[] payload) { Requests.Add((source, capability, payload)); return true; }
        public bool TryDequeue(out ushort topic, out ushort schema, out byte[] payload) { topic = schema = 0; payload = null; return false; }
    }
    public static void Run(string carrierPath, string sdkPath)
    {
        int checks = 0;
        void Check(bool valid, string message) { ++checks; if (!valid) throw new InvalidOperationException(message); }
        var host = new Host();
        var registry = new UiAddonRegistry();
        var registration = registry.Register("example.text-input", UiAddonTrust.Community,
            new UiCapabilityGrant(Array.Empty<UiObservationCapability>(), new ushort[] {7}, null));
        const string source = """
            const {React,Box,TextInput,render}=__portableReact;
            let reset=0;
            function draw(){render(React.createElement(Box,{width:300,height:100},React.createElement(TextInput,{
              text:'',textRevision:reset,maxLength:80,maxBytes:160,
              onCompositionChange:e=>{if(e.composing && e.composition!=='日')throw Error('Composition corrupt');},
              onTextChange:e=>{if(e.userInput && e.text!=='日😀')throw Error('Text corrupt');},
              onSubmit:e=>{if(e.text!=='日😀'||e.anchor!==3||e.focus!==3)throw Error('Submit corrupt');e.request(7,new Uint8Array([42]));reset++;draw();}
            })));}draw();
            """;
        using var guest = new WasmtimeUiGuest(File.ReadAllBytes(carrierPath), File.ReadAllBytes(sdkPath), Encoding.UTF8.GetBytes(source),
            registry.Activate(registration), host, host, host, host);
        Check(guest.Initialize(out var error), "Initialize real React/QuickJS: " + error);
        Check(guest.Update(out error), "Create native TextInput snapshot: " + error);
        var field = host.Snapshot!.Nodes.Single(n => n.Kind == UiNodeKind.TextInput);
        Check(field.TextInput.MaxLength == 80 && field.TextInput.MaxBytes == 160, "Native options match React");
        foreach (var edit in new[] {
            new UiTextEdit(UiTextEditPhase.Composition,"","日",0,0,true,true),
            new UiTextEdit(UiTextEditPhase.Change,"日😀","",3,3,false,true),
            new UiTextEdit(UiTextEditPhase.Submit,"日😀","",3,3,false,true) })
        {
            Check(guest.Session.QueueEvent(field.Handle,UiEventMask.TextEdit,edit.Encode()).Success,"Queue owned semantic event");
            Check(guest.Update(out error),"Dispatch through actual carrier to React: "+error);
        }
        Check(host.Requests.Count==1 && host.Requests[0].Source==field.Handle && host.Requests[0].Capability==7 && host.Requests[0].Payload.SequenceEqual(new byte[]{42}),"Submit retains source and grant");
        Check(host.Snapshot!.Nodes.Single(n=>n.Handle==field.Handle).TextInput.Revision==1,"React clear command returns to native snapshot");
        Check(guest.Session.Unload(host).Success,"Unload native generation");
        Check(!guest.Session.QueueEvent(field.Handle,UiEventMask.TextEdit,new UiTextEdit(UiTextEditPhase.Submit,"","",0,0,false,true).Encode()).Success,"Stale native editor cannot send");
        Console.WriteLine($"PASS actual React native text path: {checks} checks");
    }
}
