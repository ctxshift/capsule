using Rpg.Features;
using Mmo.Features;
using SkillSystem.Features;
var prerequisites = new TQuestPrerequisites(1, "unlock", new[] {
 new TQuestConditionNode("unlock", "quest-completed", "example.quest", Array.Empty<string>())
});
var prerequisiteBytes = RpgFeatureCodec.Encode(prerequisites);
var restoredPrerequisites = RpgFeatureCodec.DecodeQuestPrerequisites(prerequisiteBytes);
if (restoredPrerequisites.version != 1 || restoredPrerequisites.root != "unlock" || restoredPrerequisites.nodes[0].subject != "example.quest")
 throw new Exception("Standalone quest prerequisites lost authored identities");
foreach (var kind in new[] {"example_blade", "example_tool"}) {
 var provider=new LocalBag(kind);using var binding=BagContract.Bind(provider);TBagState? observed=null;using var subscription=binding.Subscribe("State",bytes=>observed=RpgFeatureCodec.DecodeBagState(bytes));
 if(observed?.items[0].kind!=kind)throw new Exception("Shared UI contract lost game-owned item identity");
 foreach(var epoch in new uint[]{9,1}) {var result=RpgFeatureCodec.DecodeBagEquipResult(await binding.Request("Equip",RpgFeatureCodec.Encode(new TEquipBagItem(epoch,2,7)),default));if(result.accepted!=(epoch==1))throw new Exception("Reviewed request identity was not preserved");}
 provider.Publish();if(observed?.revision!=3)throw new Exception("Provider publication was not observed");
}
var chat=new LocalChat();using var chatBinding=ChatContract.Bind(chat);var text=new string('x',400);var reply=MmoFeatureCodec.DecodeChatResult(await chatBinding.Request("Send",MmoFeatureCodec.Encode(new TChatSend(1,2,"party",text,"")),default));if(!reply.accepted||chat.Last!=text)throw new Exception("MMO request failed without a game/network dependency");
try {MmoFeatureCodec.Encode(new TChatSend(1,2,"party",new string('x',513),""));throw new Exception("Missing contract bound");}catch(FormatException){}
var skill=new LocalSkill();using var skillBinding=SkillProgressionContract.Bind(skill);TSkillProgressionState? observedSkill=null;
using var skillSubscription=skillBinding.Subscribe("State",bytes=>observedSkill=SkillSystemFeatureCodec.DecodeSkillProgressionState(bytes));
if(observedSkill?.skills[0].id!="navigation")throw new Exception("Skill provider initial state missing");
skill.Publish();if(observedSkill?.sequence!=2)throw new Exception("Skill provider publication missing");
var training=new LocalTraining();using var trainingBinding=TrainingContract.Bind(training);TTrainingState? observedTraining=null;
using var trainingSubscription=trainingBinding.Subscribe("State",bytes=>observedTraining=SkillSystemFeatureCodec.DecodeTrainingState(bytes));
if(observedTraining?.offers[0].id!="navigation-2")throw new Exception("Training provider initial state missing");
foreach(var sequence in new uint[]{1,2}){var answer=SkillSystemFeatureCodec.DecodeTrainingResult(await trainingBinding.Request("Interact",SkillSystemFeatureCodec.Encode(new TTrainingIntent(1,1,sequence,"navigation-2",false)),default));if(answer.accepted!=(sequence==2))throw new Exception("Training request identity not enforced");}
training.Publish();if(observedTraining?.sequence!=3)throw new Exception("Training provider publication missing");
Console.WriteLine("PASS: independent RPG, MMO and skill providers, generated codecs, state publication and request bounds");
sealed class LocalBag(string kind):IBagProvider {
 TBagState state=new("ready",1,2,new[]{new TBagItem(7,1,kind,true)});
 public event Action<TBagState>? StateChanged;public TBagState GetState()=>state;
 public Task<TBagEquipResult> Equip(TEquipBagItem request,CancellationToken token)=>Task.FromResult(new TBagEquipResult(!token.IsCancellationRequested&&request.epoch==state.epoch&&request.revision==state.revision&&request.token==7));
 public void Publish(){state=new TBagState("ready",1,3,state.items);StateChanged?.Invoke(state);}
}
sealed class LocalChat:IChatProvider {
 public string Last="";public event Action<TChatState>? StateChanged;
 public TChatState GetState()=>new(2,1,true,false,0,"",0,Array.Empty<TChatMessage>());
 public Task<TChatResult> Send(TChatSend request,CancellationToken token){bool accepted=!token.IsCancellationRequested&&request.epoch==1&&request.sequence==2;if(accepted){Last=request.text;StateChanged?.Invoke(GetState());}return Task.FromResult(new TChatResult(accepted));}
}
sealed class LocalSkill:ISkillProgressionProvider {
 TSkillProgressionState state=new("fresh",1,1,1,true,new[]{new TSkillProgress("navigation","skill","Navigation","","",1,3,40,100,true,Array.Empty<TSkillRequirement>())},Array.Empty<TProgressionResource>());
 public event Action<TSkillProgressionState>? StateChanged;public TSkillProgressionState GetState()=>state;
 public void Publish(){state=new TSkillProgressionState("fresh",1,1,2,true,state.skills,state.resources);StateChanged?.Invoke(state);}
}
sealed class LocalTraining:ITrainingProvider {
 TTrainingState state=new("fresh",1,1,2,true,"instructor",new[]{new TTrainingOffer("navigation-2","navigation",2,1,true,"",Array.Empty<TSkillCost>(),Array.Empty<TSkillRequirement>())},Array.Empty<TProgressionResource>());
 public event Action<TTrainingState>? StateChanged;public TTrainingState GetState()=>state;
 public Task<TTrainingResult> Interact(TTrainingIntent request,CancellationToken token)=>Task.FromResult(new TTrainingResult(!token.IsCancellationRequested&&request.epoch==state.epoch&&request.ownerGeneration==state.ownerGeneration&&request.sequence==state.sequence&&request.offer=="navigation-2"));
 public void Publish(){state=new TTrainingState("fresh",1,1,3,true,state.source,state.offers,state.resources);StateChanged?.Invoke(state);}
}
