using System.Text;
using Capsule;
using Capsule.Wasmtime;

static class WorldPresentationProof
{
    sealed class Host : IUiTheme, IUiCommitTarget, IUiObservationAdapter
    {
        public UiSnapshot Snapshot;
        public bool TryDequeue(out ushort topic,out ushort schema,out byte[] payload){topic=schema=0;payload=null;return false;}
        public bool AllowsStyle(string key)=>key=="";
        public bool AllowsAsset(string key)=>key=="";
        public bool TryCommit(UiSnapshot snapshot,out string error){Snapshot=snapshot;return UiPresentation.Validate(snapshot,out error);}
    }
    public static void Run(string carrierPath,string sdkPath)
    {
        int checks=0;
        void Check(bool result,string message){if(!result)throw new Exception(message);checks++;}
        var host=new Host();var registry=new UiAddonRegistry();
        var registration=registry.Register("example.world-presentation",UiAddonTrust.Community,new UiCapabilityGrant(Array.Empty<UiObservationCapability>(),Array.Empty<ushort>(),null));
        const string source="""
            const {React,Box,WorldAnchor,Text,render}=__portableReact;
            let revision=1;
            function draw(){render(React.createElement(Box,{width:800,height:600},React.createElement(WorldAnchor,{
              width:80,height:20,worldAnchor:{entity:'actor.life1',attachment:'head',offset:[0,2,0],referenceDistance:10,minScale:0.5,maxScale:2,occlusion:true},
              effect:{revision,durationMs:1000,fadeMs:400,offset:[10,-40]},onActivate:()=>{revision++;draw();}
            },React.createElement(Text,null,'12'))));}draw();
            """;
        using var guest=new WasmtimeUiGuest(File.ReadAllBytes(carrierPath),File.ReadAllBytes(sdkPath),Encoding.UTF8.GetBytes(source),registry.Activate(registration),host,host,host,null);
        Check(guest.Initialize(out var error),"Initialize actual React carrier: "+error);
        Check(guest.Update(out error),"Publish world declaration: "+error);
        var node=host.Snapshot.Nodes.Single(n=>n.WorldAnchor!=null);
        Check(node.WorldAnchor.Entity=="actor.life1" && node.WorldAnchor.Attachment=="head" && node.WorldAnchor.Y==128 && node.WorldAnchor.Occlusion,"World wire options");
        Check(node.Effect.DurationMs==1000 && node.Effect.X==640 && node.Effect.Y==-2560,"Effect wire options");
        Check(node.With(rect:new UiRect(0,0,640,640)).WorldAnchor==node.WorldAnchor,"Layout preserves immutable attachment");
        Check(guest.Session.QueueEvent(node.Handle,UiEventMask.Activate,Array.Empty<byte>()).Success,"Queue owned event");
        Check(guest.Update(out error),"React revision update: "+error);
        Check(host.Snapshot.Nodes.Single(n=>n.Handle==node.Handle).Effect.Revision==2,"Explicit effect restart arrives in native snapshot");
        Check(guest.Session.Unload(host).Success && host.Snapshot.Nodes.Count==0,"Generation unload removes presentation");
        Check(!guest.Session.QueueEvent(node.Handle,UiEventMask.Activate,Array.Empty<byte>()).Success,"Stale world UI handle rejected");
        Console.WriteLine($"PASS actual React world presentation: {checks} checks");
    }
}
