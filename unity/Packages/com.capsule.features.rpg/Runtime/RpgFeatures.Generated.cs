// Generated from TypeScript feature definitions. Do not edit.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Rpg.Features {
public sealed class TCombatState {
public bool @available { get; }
public uint @epoch { get; }
public IReadOnlyList<TCombatSlot> @slots { get; }
public IReadOnlyList<TCombatHit> @hits { get; }
public uint @errorRevision { get; }
public string @error { get; }
public TCombatState(bool @available, uint @epoch, IReadOnlyList<TCombatSlot> @slots, IReadOnlyList<TCombatHit> @hits, uint @errorRevision, string @error) {
this.@available = @available;
this.@epoch = @epoch;
this.@slots = @slots;
this.@hits = @hits;
this.@errorRevision = @errorRevision;
this.@error = @error;
}
}
public sealed class TCombatSlot {
public string @action { get; }
public bool @available { get; }
public bool @active { get; }
public string @name { get; }
public string @icon { get; }
public string @phase { get; }
public uint @durationMs { get; }
public uint @revision { get; }
public bool @queued { get; }
public TCombatSlot(string @action, bool @available, bool @active, string @name, string @icon, string @phase, uint @durationMs, uint @revision, bool @queued) {
this.@action = @action;
this.@available = @available;
this.@active = @active;
this.@name = @name;
this.@icon = @icon;
this.@phase = @phase;
this.@durationMs = @durationMs;
this.@revision = @revision;
this.@queued = @queued;
}
}
public sealed class TCombatHit {
public uint @entity { get; }
public uint @generation { get; }
public uint @revision { get; }
public uint @damage { get; }
public uint @lane { get; }
public bool @incoming { get; }
public bool @healing { get; }
public TCombatHit(uint @entity, uint @generation, uint @revision, uint @damage, uint @lane, bool @incoming, bool @healing) {
this.@entity = @entity;
this.@generation = @generation;
this.@revision = @revision;
this.@damage = @damage;
this.@lane = @lane;
this.@incoming = @incoming;
this.@healing = @healing;
}
}
public sealed class TActivateCombat {
public uint @epoch { get; }
public uint @slot { get; }
public string @action { get; }
public TActivateCombat(uint @epoch, uint @slot, string @action) {
this.@epoch = @epoch;
this.@slot = @slot;
this.@action = @action;
}
}
public sealed class TCombatActivation {
public bool @accepted { get; }
public TCombatActivation(bool @accepted) {
this.@accepted = @accepted;
}
}
public sealed class TEffectsState {
public bool @available { get; }
public uint @epoch { get; }
public TEffectActor @owner { get; }
public TEffectActor @target { get; }
public TEffectsState(bool @available, uint @epoch, TEffectActor @owner, TEffectActor @target) {
this.@available = @available;
this.@epoch = @epoch;
this.@owner = @owner;
this.@target = @target;
}
}
public sealed class TEffectActor {
public uint @entity { get; }
public uint @generation { get; }
public IReadOnlyList<TStatusEffect> @effects { get; }
public TEffectActor(uint @entity, uint @generation, IReadOnlyList<TStatusEffect> @effects) {
this.@entity = @entity;
this.@generation = @generation;
this.@effects = @effects;
}
}
public sealed class TStatusEffect {
public string @instance { get; }
public string @id { get; }
public string @name { get; }
public string @icon { get; }
public string @kind { get; }
public uint @stacks { get; }
public bool @timed { get; }
public uint @remainingMs { get; }
public TStatusEffect(string @instance, string @id, string @name, string @icon, string @kind, uint @stacks, bool @timed, uint @remainingMs) {
this.@instance = @instance;
this.@id = @id;
this.@name = @name;
this.@icon = @icon;
this.@kind = @kind;
this.@stacks = @stacks;
this.@timed = @timed;
this.@remainingMs = @remainingMs;
}
}
public sealed class TQuestState {
public string @freshness { get; }
public uint @epoch { get; }
public uint @ownerGeneration { get; }
public uint @sequence { get; }
public IReadOnlyList<TQuestEntry> @quests { get; }
public TQuestState(string @freshness, uint @epoch, uint @ownerGeneration, uint @sequence, IReadOnlyList<TQuestEntry> @quests) {
this.@freshness = @freshness;
this.@epoch = @epoch;
this.@ownerGeneration = @ownerGeneration;
this.@sequence = @sequence;
this.@quests = @quests;
}
}
public sealed class TQuestEntry {
public string @id { get; }
public string @status { get; }
public uint @contentVersion { get; }
public string @title { get; }
public string @description { get; }
public string @npc { get; }
public IReadOnlyList<TQuestObjective> @objectives { get; }
public IReadOnlyList<TQuestReward> @rewards { get; }
public TQuestEntry(string @id, string @status, uint @contentVersion, string @title, string @description, string @npc, IReadOnlyList<TQuestObjective> @objectives, IReadOnlyList<TQuestReward> @rewards) {
this.@id = @id;
this.@status = @status;
this.@contentVersion = @contentVersion;
this.@title = @title;
this.@description = @description;
this.@npc = @npc;
this.@objectives = @objectives;
this.@rewards = @rewards;
}
}
public sealed class TQuestObjective {
public string @id { get; }
public string @description { get; }
public uint @current { get; }
public uint @required { get; }
public TQuestObjective(string @id, string @description, uint @current, uint @required) {
this.@id = @id;
this.@description = @description;
this.@current = @current;
this.@required = @required;
}
}
public sealed class TQuestReward {
public string @kind { get; }
public string @id { get; }
public string @name { get; }
public uint @quantity { get; }
public TQuestReward(string @kind, string @id, string @name, uint @quantity) {
this.@kind = @kind;
this.@id = @id;
this.@name = @name;
this.@quantity = @quantity;
}
}
public sealed class THudFrameState {
public bool @available { get; }
public uint @epoch { get; }
public THudUnitState @player { get; }
public THudUnitState @target { get; }
public THudFrameState(bool @available, uint @epoch, THudUnitState @player, THudUnitState @target) {
this.@available = @available;
this.@epoch = @epoch;
this.@player = @player;
this.@target = @target;
}
}
public sealed class THudUnitState {
public bool @present { get; }
public string @name { get; }
public uint @current { get; }
public uint @maximum { get; }
public string @portrait { get; }
public THudUnitState(bool @present, string @name, uint @current, uint @maximum, string @portrait) {
this.@present = @present;
this.@name = @name;
this.@current = @current;
this.@maximum = @maximum;
this.@portrait = @portrait;
}
}
public sealed class TNameplateState {
public bool @available { get; }
public uint @epoch { get; }
public uint @selected { get; }
public IReadOnlyList<TNameplateActor> @actors { get; }
public TNameplateState(bool @available, uint @epoch, uint @selected, IReadOnlyList<TNameplateActor> @actors) {
this.@available = @available;
this.@epoch = @epoch;
this.@selected = @selected;
this.@actors = @actors;
}
}
public sealed class TNameplateActor {
public uint @entity { get; }
public uint @generation { get; }
public uint @current { get; }
public uint @maximum { get; }
public uint @kind { get; }
public string @relation { get; }
public string @questMarker { get; }
public string @name { get; }
public TNameplateActor(uint @entity, uint @generation, uint @current, uint @maximum, uint @kind, string @relation, string @questMarker, string @name) {
this.@entity = @entity;
this.@generation = @generation;
this.@current = @current;
this.@maximum = @maximum;
this.@kind = @kind;
this.@relation = @relation;
this.@questMarker = @questMarker;
this.@name = @name;
}
}
public sealed class TSelectNameplate {
public uint @epoch { get; }
public uint @entity { get; }
public uint @generation { get; }
public TSelectNameplate(uint @epoch, uint @entity, uint @generation) {
this.@epoch = @epoch;
this.@entity = @entity;
this.@generation = @generation;
}
}
public sealed class TNameplateSelection {
public bool @selected { get; }
public TNameplateSelection(bool @selected) {
this.@selected = @selected;
}
}
public sealed class TDialogueState {
public uint @revision { get; }
public bool @open { get; }
public string @title { get; }
public string @body { get; }
public string @prompt { get; }
public string @primary { get; }
public bool @primaryEnabled { get; }
public string @secondary { get; }
public bool @secondaryEnabled { get; }
public TDialogueState(uint @revision, bool @open, string @title, string @body, string @prompt, string @primary, bool @primaryEnabled, string @secondary, bool @secondaryEnabled) {
this.@revision = @revision;
this.@open = @open;
this.@title = @title;
this.@body = @body;
this.@prompt = @prompt;
this.@primary = @primary;
this.@primaryEnabled = @primaryEnabled;
this.@secondary = @secondary;
this.@secondaryEnabled = @secondaryEnabled;
}
}
public sealed class TDialogueIntent {
public uint @revision { get; }
public uint @action { get; }
public TDialogueIntent(uint @revision, uint @action) {
this.@revision = @revision;
this.@action = @action;
}
}
public sealed class TDialogueResult {
public bool @accepted { get; }
public TDialogueResult(bool @accepted) {
this.@accepted = @accepted;
}
}
public sealed class TBagState {
public string @status { get; }
public uint @epoch { get; }
public uint @revision { get; }
public IReadOnlyList<TBagItem> @items { get; }
public TBagState(string @status, uint @epoch, uint @revision, IReadOnlyList<TBagItem> @items) {
this.@status = @status;
this.@epoch = @epoch;
this.@revision = @revision;
this.@items = @items;
}
}
public sealed class TBagItem {
public uint @token { get; }
public uint @quantity { get; }
public string @kind { get; }
public bool @canEquip { get; }
public TBagItem(uint @token, uint @quantity, string @kind, bool @canEquip) {
this.@token = @token;
this.@quantity = @quantity;
this.@kind = @kind;
this.@canEquip = @canEquip;
}
}
public sealed class TEquipBagItem {
public uint @epoch { get; }
public uint @revision { get; }
public uint @token { get; }
public TEquipBagItem(uint @epoch, uint @revision, uint @token) {
this.@epoch = @epoch;
this.@revision = @revision;
this.@token = @token;
}
}
public sealed class TBagEquipResult {
public bool @accepted { get; }
public TBagEquipResult(bool @accepted) {
this.@accepted = @accepted;
}
}
public sealed class TEquipmentState {
public string @status { get; }
public uint @epoch { get; }
public uint @generation { get; }
public uint @revision { get; }
public uint @health { get; }
public bool @surface { get; }
public IReadOnlyList<TEquippedItem> @items { get; }
public TEquipmentState(string @status, uint @epoch, uint @generation, uint @revision, uint @health, bool @surface, IReadOnlyList<TEquippedItem> @items) {
this.@status = @status;
this.@epoch = @epoch;
this.@generation = @generation;
this.@revision = @revision;
this.@health = @health;
this.@surface = @surface;
this.@items = @items;
}
}
public sealed class TEquippedItem {
public string @slot { get; }
public string @kind { get; }
public bool @canUnequip { get; }
public TEquippedItem(string @slot, string @kind, bool @canUnequip) {
this.@slot = @slot;
this.@kind = @kind;
this.@canUnequip = @canUnequip;
}
}
public sealed class TUnequipItem {
public uint @epoch { get; }
public uint @generation { get; }
public uint @revision { get; }
public string @slot { get; }
public TUnequipItem(uint @epoch, uint @generation, uint @revision, string @slot) {
this.@epoch = @epoch;
this.@generation = @generation;
this.@revision = @revision;
this.@slot = @slot;
}
}
public sealed class TUnequipResult {
public bool @accepted { get; }
public TUnequipResult(bool @accepted) {
this.@accepted = @accepted;
}
}
public sealed class TLootState {
public uint @sequence { get; }
public uint @state { get; }
public IReadOnlyList<TLootEntry> @entries { get; }
public uint @epoch { get; }
public IReadOnlyList<TLootableCorpse> @corpses { get; }
public TLootState(uint @sequence, uint @state, IReadOnlyList<TLootEntry> @entries, uint @epoch, IReadOnlyList<TLootableCorpse> @corpses) {
this.@sequence = @sequence;
this.@state = @state;
this.@entries = @entries;
this.@epoch = @epoch;
this.@corpses = @corpses;
}
}
public sealed class TLootEntry {
public string @kind { get; }
public string @name { get; }
public uint @quantity { get; }
public TLootEntry(string @kind, string @name, uint @quantity) {
this.@kind = @kind;
this.@name = @name;
this.@quantity = @quantity;
}
}
public sealed class TLootableCorpse {
public string @id { get; }
public string @reference { get; }
public TLootableCorpse(string @id, string @reference) {
this.@id = @id;
this.@reference = @reference;
}
}
public sealed class TLootIntent {
public uint @sequence { get; }
public uint @action { get; }
public TLootIntent(uint @sequence, uint @action) {
this.@sequence = @sequence;
this.@action = @action;
}
}
public sealed class TLootResult {
public bool @accepted { get; }
public TLootResult(bool @accepted) {
this.@accepted = @accepted;
}
}
public sealed class TOpenLoot {
public uint @epoch { get; }
public string @corpseId { get; }
public TOpenLoot(uint @epoch, string @corpseId) {
this.@epoch = @epoch;
this.@corpseId = @corpseId;
}
}
public sealed class TMerchantState {
public bool @available { get; }
public uint @sequence { get; }
public bool @ready { get; }
public bool @pending { get; }
public bool @retry { get; }
public string @state { get; }
public string @name { get; }
public uint @coins { get; }
public string @notice { get; }
public IReadOnlyList<TMerchantOffer> @offers { get; }
public IReadOnlyList<TMerchantItem> @bag { get; }
public TMerchantState(bool @available, uint @sequence, bool @ready, bool @pending, bool @retry, string @state, string @name, uint @coins, string @notice, IReadOnlyList<TMerchantOffer> @offers, IReadOnlyList<TMerchantItem> @bag) {
this.@available = @available;
this.@sequence = @sequence;
this.@ready = @ready;
this.@pending = @pending;
this.@retry = @retry;
this.@state = @state;
this.@name = @name;
this.@coins = @coins;
this.@notice = @notice;
this.@offers = @offers;
this.@bag = @bag;
}
}
public sealed class TMerchantOffer {
public string @id { get; }
public string @kind { get; }
public string @name { get; }
public uint @price { get; }
public uint @quantity { get; }
public TMerchantOffer(string @id, string @kind, string @name, uint @price, uint @quantity) {
this.@id = @id;
this.@kind = @kind;
this.@name = @name;
this.@price = @price;
this.@quantity = @quantity;
}
}
public sealed class TMerchantItem {
public bool @present { get; }
public string @id { get; }
public string @kind { get; }
public uint @quantity { get; }
public bool @sellable { get; }
public uint @salePrice { get; }
public string @reason { get; }
public TMerchantItem(bool @present, string @id, string @kind, uint @quantity, bool @sellable, uint @salePrice, string @reason) {
this.@present = @present;
this.@id = @id;
this.@kind = @kind;
this.@quantity = @quantity;
this.@sellable = @sellable;
this.@salePrice = @salePrice;
this.@reason = @reason;
}
}
public sealed class TMerchantIntent {
public uint @sequence { get; }
public uint @action { get; }
public uint @index { get; }
public TMerchantIntent(uint @sequence, uint @action, uint @index) {
this.@sequence = @sequence;
this.@action = @action;
this.@index = @index;
}
}
public sealed class TMerchantResult {
public bool @accepted { get; }
public TMerchantResult(bool @accepted) {
this.@accepted = @accepted;
}
}
public sealed class TQuestPrerequisites {
public uint @version { get; }
public string @root { get; }
public IReadOnlyList<TQuestConditionNode> @nodes { get; }
public TQuestPrerequisites(uint @version, string @root, IReadOnlyList<TQuestConditionNode> @nodes) {
this.@version = @version;
this.@root = @root;
this.@nodes = @nodes;
}
}
public sealed class TQuestConditionNode {
public string @id { get; }
public string @kind { get; }
public string @subject { get; }
public IReadOnlyList<string> @children { get; }
public TQuestConditionNode(string @id, string @kind, string @subject, IReadOnlyList<string> @children) {
this.@id = @id;
this.@kind = @kind;
this.@subject = @subject;
this.@children = @children;
}
}
public sealed class TQuestDefinition {
public string @id { get; }
public uint @revision { get; }
public string @title { get; }
public string @description { get; }
public TQuestPrerequisites @prerequisites { get; }
public IReadOnlyList<TQuestObjectiveDefinition> @objectives { get; }
public string @completion { get; }
public string @repeat { get; }
public string @reward { get; }
public TQuestDefinition(string @id, uint @revision, string @title, string @description, TQuestPrerequisites @prerequisites, IReadOnlyList<TQuestObjectiveDefinition> @objectives, string @completion, string @repeat, string @reward) {
this.@id = @id;
this.@revision = @revision;
this.@title = @title;
this.@description = @description;
this.@prerequisites = @prerequisites;
this.@objectives = @objectives;
this.@completion = @completion;
this.@repeat = @repeat;
this.@reward = @reward;
}
}
public sealed class TQuestObjectiveDefinition {
public string @id { get; }
public string @event { get; }
public string @target { get; }
public string @world { get; }
public string @description { get; }
public uint @required { get; }
public IReadOnlyList<string> @after { get; }
public TQuestObjectiveDefinition(string @id, string @event, string @target, string @world, string @description, uint @required, IReadOnlyList<string> @after) {
this.@id = @id;
this.@event = @event;
this.@target = @target;
this.@world = @world;
this.@description = @description;
this.@required = @required;
this.@after = @after;
}
}
public sealed class TQuestBinding {
public string @quest { get; }
public string @role { get; }
public string @actor { get; }
public TQuestBinding(string @quest, string @role, string @actor) {
this.@quest = @quest;
this.@role = @role;
this.@actor = @actor;
}
}
public sealed class TQuestFact {
public string @id { get; }
public string @type { get; }
public string @owner { get; }
public string @scope { get; }
public string @lifetime { get; }
public TQuestFact(string @id, string @type, string @owner, string @scope, string @lifetime) {
this.@id = @id;
this.@type = @type;
this.@owner = @owner;
this.@scope = @scope;
this.@lifetime = @lifetime;
}
}
public interface ICombatProvider {
TCombatState GetState();
event Action<TCombatState> StateChanged;
Task<TCombatActivation> Activate(TActivateCombat request, CancellationToken cancellationToken);
}
public static class CombatContract {
public const string Name = "@rpg/combat", Version = "1.0.0", Fingerprint = "6dc56f295a6e9719a04cb4015d0592ad81eeb5caec0fc7edc9d9d92d0454d2fe";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] {  },
new string[] { "combat.read", "combat.activate" });
public static CombatBinding Bind(ICombatProvider provider) => new CombatBinding(provider);
}
public interface IStatusEffectsProvider {
TEffectsState GetState();
event Action<TEffectsState> StateChanged;

}
public static class StatusEffectsContract {
public const string Name = "@rpg/status-effects", Version = "1.0.0", Fingerprint = "3ace475d3bffcc8f5f25d9da771a8aab43aecf894a5bdde5ece93013bc93d526";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] {  },
new string[] { "effects.read" });
public static StatusEffectsBinding Bind(IStatusEffectsProvider provider) => new StatusEffectsBinding(provider);
}
public interface IQuestsProvider {
TQuestState GetState();
event Action<TQuestState> StateChanged;

}
public static class QuestsContract {
public const string Name = "@rpg/quests", Version = "1.0.0", Fingerprint = "b9d00f2cc298955108f6aeb3aaeef562c1f7330bf729d8e6551092a11b4c25a0";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] {  },
new string[] { "quests.read" });
public static QuestsBinding Bind(IQuestsProvider provider) => new QuestsBinding(provider);
}
public interface IHudFramesProvider {
THudFrameState GetState();
event Action<THudFrameState> StateChanged;

}
public static class HudFramesContract {
public const string Name = "@rpg/hud-frames", Version = "1.0.0", Fingerprint = "651047f05d829f44ac7a0e9fb4c335587b1804e2808a0e0ace8c37cb3ecea185";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] {  },
new string[] { "hud.read" });
public static HudFramesBinding Bind(IHudFramesProvider provider) => new HudFramesBinding(provider);
}
public interface INameplatesProvider {
TNameplateState GetState();
event Action<TNameplateState> StateChanged;
Task<TNameplateSelection> Select(TSelectNameplate request, CancellationToken cancellationToken);
}
public static class NameplatesContract {
public const string Name = "@rpg/nameplates", Version = "1.0.0", Fingerprint = "f17ea87e038fe2a33049ccdffd7fc561eef244f6a3c78dcdce3bcd9e5c9ac44b";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] {  },
new string[] { "nameplates.read", "nameplates.select" });
public static NameplatesBinding Bind(INameplatesProvider provider) => new NameplatesBinding(provider);
}
public interface IDialogueProvider {
TDialogueState GetState();
event Action<TDialogueState> StateChanged;
Task<TDialogueResult> Act(TDialogueIntent request, CancellationToken cancellationToken);
}
public static class DialogueContract {
public const string Name = "@rpg/dialogue", Version = "1.0.0", Fingerprint = "6fcbd6fafdbe08a696ef6fe5a7cf260e1dad436ac73dea60136324b0b9a34043";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] {  },
new string[] { "dialogue.read", "dialogue.act" });
public static DialogueBinding Bind(IDialogueProvider provider) => new DialogueBinding(provider);
}
public interface IBagProvider {
TBagState GetState();
event Action<TBagState> StateChanged;
Task<TBagEquipResult> Equip(TEquipBagItem request, CancellationToken cancellationToken);
}
public static class BagContract {
public const string Name = "@rpg/bag", Version = "1.0.0", Fingerprint = "30a9ccd49dedaaec385f950f5de0d074c503338f63e5dfae61db6d9c45c5c85d";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] {  },
new string[] { "bag.read", "bag.equip" });
public static BagBinding Bind(IBagProvider provider) => new BagBinding(provider);
}
public interface IEquipmentProvider {
TEquipmentState GetState();
event Action<TEquipmentState> StateChanged;
Task<TUnequipResult> Unequip(TUnequipItem request, CancellationToken cancellationToken);
}
public static class EquipmentContract {
public const string Name = "@rpg/equipment", Version = "1.0.0", Fingerprint = "8f8361b6b5d00466de6920c0c911a90ebee1c098ed6dc9499853d3c91bc8af02";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] {  },
new string[] { "equipment.read", "equipment.unequip" });
public static EquipmentBinding Bind(IEquipmentProvider provider) => new EquipmentBinding(provider);
}
public interface ILootProvider {
TLootState GetState();
event Action<TLootState> StateChanged;
Task<TLootResult> Interact(TLootIntent request, CancellationToken cancellationToken);
Task<TLootResult> Open(TOpenLoot request, CancellationToken cancellationToken);
}
public static class LootContract {
public const string Name = "@rpg/loot", Version = "1.0.0", Fingerprint = "7b34edb85c748ae24fbc086fdcbc2a2708b22003702ada61f25f963742fc5193";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] {  },
new string[] { "loot.read", "loot.interact" });
public static LootBinding Bind(ILootProvider provider) => new LootBinding(provider);
}
public interface IMerchantProvider {
TMerchantState GetState();
event Action<TMerchantState> StateChanged;
Task<TMerchantResult> Interact(TMerchantIntent request, CancellationToken cancellationToken);
}
public static class MerchantContract {
public const string Name = "@rpg/merchant", Version = "1.0.0", Fingerprint = "e5e871d2d5b9bddf82ff0ccb64c4b6a5bbef5d9f4c101cf019d2698845d7c845";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] {  },
new string[] { "merchant.read", "merchant.interact" });
public static MerchantBinding Bind(IMerchantProvider provider) => new MerchantBinding(provider);
}
public sealed class CombatBinding : Capsule.UiFeatureProvider {
readonly ICombatProvider provider;
public CombatBinding(ICombatProvider provider) : base(CombatContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "combat.read", RpgFeatureCodec.Encode(provider.GetState()), Capsule.UiFeatureDelivery.Ordered);
provider.StateChanged += OnState;
AddRequest("Activate", "combat.activate", async (bytes, cancellation) => RpgFeatureCodec.Encode(await provider.Activate(RpgFeatureCodec.DecodeActivateCombat(bytes), cancellation)));
}
void OnState(TCombatState value) => Publish("State", RpgFeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public sealed class StatusEffectsBinding : Capsule.UiFeatureProvider {
readonly IStatusEffectsProvider provider;
public StatusEffectsBinding(IStatusEffectsProvider provider) : base(StatusEffectsContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "effects.read", RpgFeatureCodec.Encode(provider.GetState()));
provider.StateChanged += OnState;

}
void OnState(TEffectsState value) => Publish("State", RpgFeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public sealed class QuestsBinding : Capsule.UiFeatureProvider {
readonly IQuestsProvider provider;
public QuestsBinding(IQuestsProvider provider) : base(QuestsContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "quests.read", RpgFeatureCodec.Encode(provider.GetState()));
provider.StateChanged += OnState;

}
void OnState(TQuestState value) => Publish("State", RpgFeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public sealed class HudFramesBinding : Capsule.UiFeatureProvider {
readonly IHudFramesProvider provider;
public HudFramesBinding(IHudFramesProvider provider) : base(HudFramesContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "hud.read", RpgFeatureCodec.Encode(provider.GetState()));
provider.StateChanged += OnState;

}
void OnState(THudFrameState value) => Publish("State", RpgFeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public sealed class NameplatesBinding : Capsule.UiFeatureProvider {
readonly INameplatesProvider provider;
public NameplatesBinding(INameplatesProvider provider) : base(NameplatesContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "nameplates.read", RpgFeatureCodec.Encode(provider.GetState()));
provider.StateChanged += OnState;
AddRequest("Select", "nameplates.select", async (bytes, cancellation) => RpgFeatureCodec.Encode(await provider.Select(RpgFeatureCodec.DecodeSelectNameplate(bytes), cancellation)));
}
void OnState(TNameplateState value) => Publish("State", RpgFeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public sealed class DialogueBinding : Capsule.UiFeatureProvider {
readonly IDialogueProvider provider;
public DialogueBinding(IDialogueProvider provider) : base(DialogueContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "dialogue.read", RpgFeatureCodec.Encode(provider.GetState()));
provider.StateChanged += OnState;
AddRequest("Act", "dialogue.act", async (bytes, cancellation) => RpgFeatureCodec.Encode(await provider.Act(RpgFeatureCodec.DecodeDialogueIntent(bytes), cancellation)));
}
void OnState(TDialogueState value) => Publish("State", RpgFeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public sealed class BagBinding : Capsule.UiFeatureProvider {
readonly IBagProvider provider;
public BagBinding(IBagProvider provider) : base(BagContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "bag.read", RpgFeatureCodec.Encode(provider.GetState()));
provider.StateChanged += OnState;
AddRequest("Equip", "bag.equip", async (bytes, cancellation) => RpgFeatureCodec.Encode(await provider.Equip(RpgFeatureCodec.DecodeEquipBagItem(bytes), cancellation)));
}
void OnState(TBagState value) => Publish("State", RpgFeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public sealed class EquipmentBinding : Capsule.UiFeatureProvider {
readonly IEquipmentProvider provider;
public EquipmentBinding(IEquipmentProvider provider) : base(EquipmentContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "equipment.read", RpgFeatureCodec.Encode(provider.GetState()));
provider.StateChanged += OnState;
AddRequest("Unequip", "equipment.unequip", async (bytes, cancellation) => RpgFeatureCodec.Encode(await provider.Unequip(RpgFeatureCodec.DecodeUnequipItem(bytes), cancellation)));
}
void OnState(TEquipmentState value) => Publish("State", RpgFeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public sealed class LootBinding : Capsule.UiFeatureProvider {
readonly ILootProvider provider;
public LootBinding(ILootProvider provider) : base(LootContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "loot.read", RpgFeatureCodec.Encode(provider.GetState()));
provider.StateChanged += OnState;
AddRequest("Interact", "loot.interact", async (bytes, cancellation) => RpgFeatureCodec.Encode(await provider.Interact(RpgFeatureCodec.DecodeLootIntent(bytes), cancellation)));
AddRequest("Open", "loot.interact", async (bytes, cancellation) => RpgFeatureCodec.Encode(await provider.Open(RpgFeatureCodec.DecodeOpenLoot(bytes), cancellation)));
}
void OnState(TLootState value) => Publish("State", RpgFeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public sealed class MerchantBinding : Capsule.UiFeatureProvider {
readonly IMerchantProvider provider;
public MerchantBinding(IMerchantProvider provider) : base(MerchantContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "merchant.read", RpgFeatureCodec.Encode(provider.GetState()));
provider.StateChanged += OnState;
AddRequest("Interact", "merchant.interact", async (bytes, cancellation) => RpgFeatureCodec.Encode(await provider.Interact(RpgFeatureCodec.DecodeMerchantIntent(bytes), cancellation)));
}
void OnState(TMerchantState value) => Publish("State", RpgFeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public static class RpgFeatureCodec {
public static byte[] Encode(TCombatState value) { var w = new Wire(SizeCombatState(value)); WriteCombatState(w, value); return w.Bytes; }
public static TCombatState DecodeCombatState(byte[] bytes) { var r = new Wire(bytes); var value = ReadCombatState(r); r.End(); return value; }
static int SizeCombatState(TCombatState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1 + 4 + Wire.ListSize(value.@slots, 64, item0 => SizeCombatSlot(item0)) + Wire.ListSize(value.@hits, 64, item0 => SizeCombatHit(item0)) + 4 + Wire.TextSize(value.@error, 80)); }
static void WriteCombatState(Wire w, TCombatState value) { w.Bool(value.@available);
w.UInt(value.@epoch);
w.UInt((uint)value.@slots.Count); foreach (var item0 in value.@slots) { WriteCombatSlot(w, item0); }
w.UInt((uint)value.@hits.Count); foreach (var item0 in value.@hits) { WriteCombatHit(w, item0); }
w.UInt(value.@errorRevision);
w.Text(value.@error); }
static TCombatState ReadCombatState(Wire r) => new TCombatState(r.Bool(), r.UInt(), r.List(64, () => ReadCombatSlot(r)), r.List(64, () => ReadCombatHit(r)), r.UInt(), r.Text(80));
public static byte[] Encode(TCombatSlot value) { var w = new Wire(SizeCombatSlot(value)); WriteCombatSlot(w, value); return w.Bytes; }
public static TCombatSlot DecodeCombatSlot(byte[] bytes) { var r = new Wire(bytes); var value = ReadCombatSlot(r); r.End(); return value; }
static int SizeCombatSlot(TCombatSlot value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@action, 128) + 1 + 1 + Wire.TextSize(value.@name, 128) + Wire.TextSize(value.@icon, 128) + Wire.TextSize(value.@phase, 16) + 4 + 4 + 1); }
static void WriteCombatSlot(Wire w, TCombatSlot value) { w.Text(value.@action);
w.Bool(value.@available);
w.Bool(value.@active);
w.Text(value.@name);
w.Text(value.@icon);
w.Text(value.@phase);
w.UInt(value.@durationMs);
w.UInt(value.@revision);
w.Bool(value.@queued); }
static TCombatSlot ReadCombatSlot(Wire r) => new TCombatSlot(r.Text(128), r.Bool(), r.Bool(), r.Text(128), r.Text(128), r.Text(16), r.UInt(), r.UInt(), r.Bool());
public static byte[] Encode(TCombatHit value) { var w = new Wire(SizeCombatHit(value)); WriteCombatHit(w, value); return w.Bytes; }
public static TCombatHit DecodeCombatHit(byte[] bytes) { var r = new Wire(bytes); var value = ReadCombatHit(r); r.End(); return value; }
static int SizeCombatHit(TCombatHit value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + 4 + 4 + 4 + 1 + 1); }
static void WriteCombatHit(Wire w, TCombatHit value) { w.UInt(value.@entity);
w.UInt(value.@generation);
w.UInt(value.@revision);
w.UInt(value.@damage);
w.UInt(value.@lane);
w.Bool(value.@incoming);
w.Bool(value.@healing); }
static TCombatHit ReadCombatHit(Wire r) => new TCombatHit(r.UInt(), r.UInt(), r.UInt(), r.UInt(), r.UInt(), r.Bool(), r.Bool());
public static byte[] Encode(TActivateCombat value) { var w = new Wire(SizeActivateCombat(value)); WriteActivateCombat(w, value); return w.Bytes; }
public static TActivateCombat DecodeActivateCombat(byte[] bytes) { var r = new Wire(bytes); var value = ReadActivateCombat(r); r.End(); return value; }
static int SizeActivateCombat(TActivateCombat value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + Wire.TextSize(value.@action, 128)); }
static void WriteActivateCombat(Wire w, TActivateCombat value) { w.UInt(value.@epoch);
w.UInt(value.@slot);
w.Text(value.@action); }
static TActivateCombat ReadActivateCombat(Wire r) => new TActivateCombat(r.UInt(), r.UInt(), r.Text(128));
public static byte[] Encode(TCombatActivation value) { var w = new Wire(SizeCombatActivation(value)); WriteCombatActivation(w, value); return w.Bytes; }
public static TCombatActivation DecodeCombatActivation(byte[] bytes) { var r = new Wire(bytes); var value = ReadCombatActivation(r); r.End(); return value; }
static int SizeCombatActivation(TCombatActivation value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1); }
static void WriteCombatActivation(Wire w, TCombatActivation value) { w.Bool(value.@accepted); }
static TCombatActivation ReadCombatActivation(Wire r) => new TCombatActivation(r.Bool());
public static byte[] Encode(TEffectsState value) { var w = new Wire(SizeEffectsState(value)); WriteEffectsState(w, value); return w.Bytes; }
public static TEffectsState DecodeEffectsState(byte[] bytes) { var r = new Wire(bytes); var value = ReadEffectsState(r); r.End(); return value; }
static int SizeEffectsState(TEffectsState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1 + 4 + SizeEffectActor(value.@owner) + SizeEffectActor(value.@target)); }
static void WriteEffectsState(Wire w, TEffectsState value) { w.Bool(value.@available);
w.UInt(value.@epoch);
WriteEffectActor(w, value.@owner);
WriteEffectActor(w, value.@target); }
static TEffectsState ReadEffectsState(Wire r) => new TEffectsState(r.Bool(), r.UInt(), ReadEffectActor(r), ReadEffectActor(r));
public static byte[] Encode(TEffectActor value) { var w = new Wire(SizeEffectActor(value)); WriteEffectActor(w, value); return w.Bytes; }
public static TEffectActor DecodeEffectActor(byte[] bytes) { var r = new Wire(bytes); var value = ReadEffectActor(r); r.End(); return value; }
static int SizeEffectActor(TEffectActor value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + Wire.ListSize(value.@effects, 64, item0 => SizeStatusEffect(item0))); }
static void WriteEffectActor(Wire w, TEffectActor value) { w.UInt(value.@entity);
w.UInt(value.@generation);
w.UInt((uint)value.@effects.Count); foreach (var item0 in value.@effects) { WriteStatusEffect(w, item0); } }
static TEffectActor ReadEffectActor(Wire r) => new TEffectActor(r.UInt(), r.UInt(), r.List(64, () => ReadStatusEffect(r)));
public static byte[] Encode(TStatusEffect value) { var w = new Wire(SizeStatusEffect(value)); WriteStatusEffect(w, value); return w.Bytes; }
public static TStatusEffect DecodeStatusEffect(byte[] bytes) { var r = new Wire(bytes); var value = ReadStatusEffect(r); r.End(); return value; }
static int SizeStatusEffect(TStatusEffect value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@instance, 128) + Wire.TextSize(value.@id, 128) + Wire.TextSize(value.@name, 128) + Wire.TextSize(value.@icon, 128) + Wire.TextSize(value.@kind, 32) + 4 + 1 + 4); }
static void WriteStatusEffect(Wire w, TStatusEffect value) { w.Text(value.@instance);
w.Text(value.@id);
w.Text(value.@name);
w.Text(value.@icon);
w.Text(value.@kind);
w.UInt(value.@stacks);
w.Bool(value.@timed);
w.UInt(value.@remainingMs); }
static TStatusEffect ReadStatusEffect(Wire r) => new TStatusEffect(r.Text(128), r.Text(128), r.Text(128), r.Text(128), r.Text(32), r.UInt(), r.Bool(), r.UInt());
public static byte[] Encode(TQuestState value) { var w = new Wire(SizeQuestState(value)); WriteQuestState(w, value); return w.Bytes; }
public static TQuestState DecodeQuestState(byte[] bytes) { var r = new Wire(bytes); var value = ReadQuestState(r); r.End(); return value; }
static int SizeQuestState(TQuestState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@freshness, 16) + 4 + 4 + 4 + Wire.ListSize(value.@quests, 64, item0 => SizeQuestEntry(item0))); }
static void WriteQuestState(Wire w, TQuestState value) { w.Text(value.@freshness);
w.UInt(value.@epoch);
w.UInt(value.@ownerGeneration);
w.UInt(value.@sequence);
w.UInt((uint)value.@quests.Count); foreach (var item0 in value.@quests) { WriteQuestEntry(w, item0); } }
static TQuestState ReadQuestState(Wire r) => new TQuestState(r.Text(16), r.UInt(), r.UInt(), r.UInt(), r.List(64, () => ReadQuestEntry(r)));
public static byte[] Encode(TQuestEntry value) { var w = new Wire(SizeQuestEntry(value)); WriteQuestEntry(w, value); return w.Bytes; }
public static TQuestEntry DecodeQuestEntry(byte[] bytes) { var r = new Wire(bytes); var value = ReadQuestEntry(r); r.End(); return value; }
static int SizeQuestEntry(TQuestEntry value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 128) + Wire.TextSize(value.@status, 16) + 4 + Wire.TextSize(value.@title, 512) + Wire.TextSize(value.@description, 4096) + Wire.TextSize(value.@npc, 512) + Wire.ListSize(value.@objectives, 8, item0 => SizeQuestObjective(item0)) + Wire.ListSize(value.@rewards, 16, item0 => SizeQuestReward(item0))); }
static void WriteQuestEntry(Wire w, TQuestEntry value) { w.Text(value.@id);
w.Text(value.@status);
w.UInt(value.@contentVersion);
w.Text(value.@title);
w.Text(value.@description);
w.Text(value.@npc);
w.UInt((uint)value.@objectives.Count); foreach (var item0 in value.@objectives) { WriteQuestObjective(w, item0); }
w.UInt((uint)value.@rewards.Count); foreach (var item0 in value.@rewards) { WriteQuestReward(w, item0); } }
static TQuestEntry ReadQuestEntry(Wire r) => new TQuestEntry(r.Text(128), r.Text(16), r.UInt(), r.Text(512), r.Text(4096), r.Text(512), r.List(8, () => ReadQuestObjective(r)), r.List(16, () => ReadQuestReward(r)));
public static byte[] Encode(TQuestObjective value) { var w = new Wire(SizeQuestObjective(value)); WriteQuestObjective(w, value); return w.Bytes; }
public static TQuestObjective DecodeQuestObjective(byte[] bytes) { var r = new Wire(bytes); var value = ReadQuestObjective(r); r.End(); return value; }
static int SizeQuestObjective(TQuestObjective value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 128) + Wire.TextSize(value.@description, 1024) + 4 + 4); }
static void WriteQuestObjective(Wire w, TQuestObjective value) { w.Text(value.@id);
w.Text(value.@description);
w.UInt(value.@current);
w.UInt(value.@required); }
static TQuestObjective ReadQuestObjective(Wire r) => new TQuestObjective(r.Text(128), r.Text(1024), r.UInt(), r.UInt());
public static byte[] Encode(TQuestReward value) { var w = new Wire(SizeQuestReward(value)); WriteQuestReward(w, value); return w.Bytes; }
public static TQuestReward DecodeQuestReward(byte[] bytes) { var r = new Wire(bytes); var value = ReadQuestReward(r); r.End(); return value; }
static int SizeQuestReward(TQuestReward value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@kind, 32) + Wire.TextSize(value.@id, 128) + Wire.TextSize(value.@name, 128) + 4); }
static void WriteQuestReward(Wire w, TQuestReward value) { w.Text(value.@kind);
w.Text(value.@id);
w.Text(value.@name);
w.UInt(value.@quantity); }
static TQuestReward ReadQuestReward(Wire r) => new TQuestReward(r.Text(32), r.Text(128), r.Text(128), r.UInt());
public static byte[] Encode(THudFrameState value) { var w = new Wire(SizeHudFrameState(value)); WriteHudFrameState(w, value); return w.Bytes; }
public static THudFrameState DecodeHudFrameState(byte[] bytes) { var r = new Wire(bytes); var value = ReadHudFrameState(r); r.End(); return value; }
static int SizeHudFrameState(THudFrameState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1 + 4 + SizeHudUnitState(value.@player) + SizeHudUnitState(value.@target)); }
static void WriteHudFrameState(Wire w, THudFrameState value) { w.Bool(value.@available);
w.UInt(value.@epoch);
WriteHudUnitState(w, value.@player);
WriteHudUnitState(w, value.@target); }
static THudFrameState ReadHudFrameState(Wire r) => new THudFrameState(r.Bool(), r.UInt(), ReadHudUnitState(r), ReadHudUnitState(r));
public static byte[] Encode(THudUnitState value) { var w = new Wire(SizeHudUnitState(value)); WriteHudUnitState(w, value); return w.Bytes; }
public static THudUnitState DecodeHudUnitState(byte[] bytes) { var r = new Wire(bytes); var value = ReadHudUnitState(r); r.End(); return value; }
static int SizeHudUnitState(THudUnitState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1 + Wire.TextSize(value.@name, 80) + 4 + 4 + Wire.TextSize(value.@portrait, 16)); }
static void WriteHudUnitState(Wire w, THudUnitState value) { w.Bool(value.@present);
w.Text(value.@name);
w.UInt(value.@current);
w.UInt(value.@maximum);
w.Text(value.@portrait); }
static THudUnitState ReadHudUnitState(Wire r) => new THudUnitState(r.Bool(), r.Text(80), r.UInt(), r.UInt(), r.Text(16));
public static byte[] Encode(TNameplateState value) { var w = new Wire(SizeNameplateState(value)); WriteNameplateState(w, value); return w.Bytes; }
public static TNameplateState DecodeNameplateState(byte[] bytes) { var r = new Wire(bytes); var value = ReadNameplateState(r); r.End(); return value; }
static int SizeNameplateState(TNameplateState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1 + 4 + 4 + Wire.ListSize(value.@actors, 64, item0 => SizeNameplateActor(item0))); }
static void WriteNameplateState(Wire w, TNameplateState value) { w.Bool(value.@available);
w.UInt(value.@epoch);
w.UInt(value.@selected);
w.UInt((uint)value.@actors.Count); foreach (var item0 in value.@actors) { WriteNameplateActor(w, item0); } }
static TNameplateState ReadNameplateState(Wire r) => new TNameplateState(r.Bool(), r.UInt(), r.UInt(), r.List(64, () => ReadNameplateActor(r)));
public static byte[] Encode(TNameplateActor value) { var w = new Wire(SizeNameplateActor(value)); WriteNameplateActor(w, value); return w.Bytes; }
public static TNameplateActor DecodeNameplateActor(byte[] bytes) { var r = new Wire(bytes); var value = ReadNameplateActor(r); r.End(); return value; }
static int SizeNameplateActor(TNameplateActor value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + 4 + 4 + 4 + Wire.TextSize(value.@relation, 16) + Wire.TextSize(value.@questMarker, 16) + Wire.TextSize(value.@name, 80)); }
static void WriteNameplateActor(Wire w, TNameplateActor value) { w.UInt(value.@entity);
w.UInt(value.@generation);
w.UInt(value.@current);
w.UInt(value.@maximum);
w.UInt(value.@kind);
w.Text(value.@relation);
w.Text(value.@questMarker);
w.Text(value.@name); }
static TNameplateActor ReadNameplateActor(Wire r) => new TNameplateActor(r.UInt(), r.UInt(), r.UInt(), r.UInt(), r.UInt(), r.Text(16), r.Text(16), r.Text(80));
public static byte[] Encode(TSelectNameplate value) { var w = new Wire(SizeSelectNameplate(value)); WriteSelectNameplate(w, value); return w.Bytes; }
public static TSelectNameplate DecodeSelectNameplate(byte[] bytes) { var r = new Wire(bytes); var value = ReadSelectNameplate(r); r.End(); return value; }
static int SizeSelectNameplate(TSelectNameplate value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + 4); }
static void WriteSelectNameplate(Wire w, TSelectNameplate value) { w.UInt(value.@epoch);
w.UInt(value.@entity);
w.UInt(value.@generation); }
static TSelectNameplate ReadSelectNameplate(Wire r) => new TSelectNameplate(r.UInt(), r.UInt(), r.UInt());
public static byte[] Encode(TNameplateSelection value) { var w = new Wire(SizeNameplateSelection(value)); WriteNameplateSelection(w, value); return w.Bytes; }
public static TNameplateSelection DecodeNameplateSelection(byte[] bytes) { var r = new Wire(bytes); var value = ReadNameplateSelection(r); r.End(); return value; }
static int SizeNameplateSelection(TNameplateSelection value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1); }
static void WriteNameplateSelection(Wire w, TNameplateSelection value) { w.Bool(value.@selected); }
static TNameplateSelection ReadNameplateSelection(Wire r) => new TNameplateSelection(r.Bool());
public static byte[] Encode(TDialogueState value) { var w = new Wire(SizeDialogueState(value)); WriteDialogueState(w, value); return w.Bytes; }
public static TDialogueState DecodeDialogueState(byte[] bytes) { var r = new Wire(bytes); var value = ReadDialogueState(r); r.End(); return value; }
static int SizeDialogueState(TDialogueState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 1 + Wire.TextSize(value.@title, 128) + Wire.TextSize(value.@body, 2048) + Wire.TextSize(value.@prompt, 256) + Wire.TextSize(value.@primary, 64) + 1 + Wire.TextSize(value.@secondary, 64) + 1); }
static void WriteDialogueState(Wire w, TDialogueState value) { w.UInt(value.@revision);
w.Bool(value.@open);
w.Text(value.@title);
w.Text(value.@body);
w.Text(value.@prompt);
w.Text(value.@primary);
w.Bool(value.@primaryEnabled);
w.Text(value.@secondary);
w.Bool(value.@secondaryEnabled); }
static TDialogueState ReadDialogueState(Wire r) => new TDialogueState(r.UInt(), r.Bool(), r.Text(128), r.Text(2048), r.Text(256), r.Text(64), r.Bool(), r.Text(64), r.Bool());
public static byte[] Encode(TDialogueIntent value) { var w = new Wire(SizeDialogueIntent(value)); WriteDialogueIntent(w, value); return w.Bytes; }
public static TDialogueIntent DecodeDialogueIntent(byte[] bytes) { var r = new Wire(bytes); var value = ReadDialogueIntent(r); r.End(); return value; }
static int SizeDialogueIntent(TDialogueIntent value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4); }
static void WriteDialogueIntent(Wire w, TDialogueIntent value) { w.UInt(value.@revision);
w.UInt(value.@action); }
static TDialogueIntent ReadDialogueIntent(Wire r) => new TDialogueIntent(r.UInt(), r.UInt());
public static byte[] Encode(TDialogueResult value) { var w = new Wire(SizeDialogueResult(value)); WriteDialogueResult(w, value); return w.Bytes; }
public static TDialogueResult DecodeDialogueResult(byte[] bytes) { var r = new Wire(bytes); var value = ReadDialogueResult(r); r.End(); return value; }
static int SizeDialogueResult(TDialogueResult value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1); }
static void WriteDialogueResult(Wire w, TDialogueResult value) { w.Bool(value.@accepted); }
static TDialogueResult ReadDialogueResult(Wire r) => new TDialogueResult(r.Bool());
public static byte[] Encode(TBagState value) { var w = new Wire(SizeBagState(value)); WriteBagState(w, value); return w.Bytes; }
public static TBagState DecodeBagState(byte[] bytes) { var r = new Wire(bytes); var value = ReadBagState(r); r.End(); return value; }
static int SizeBagState(TBagState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@status, 16) + 4 + 4 + Wire.ListSize(value.@items, 24, item0 => SizeBagItem(item0))); }
static void WriteBagState(Wire w, TBagState value) { w.Text(value.@status);
w.UInt(value.@epoch);
w.UInt(value.@revision);
w.UInt((uint)value.@items.Count); foreach (var item0 in value.@items) { WriteBagItem(w, item0); } }
static TBagState ReadBagState(Wire r) => new TBagState(r.Text(16), r.UInt(), r.UInt(), r.List(24, () => ReadBagItem(r)));
public static byte[] Encode(TBagItem value) { var w = new Wire(SizeBagItem(value)); WriteBagItem(w, value); return w.Bytes; }
public static TBagItem DecodeBagItem(byte[] bytes) { var r = new Wire(bytes); var value = ReadBagItem(r); r.End(); return value; }
static int SizeBagItem(TBagItem value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + Wire.TextSize(value.@kind, 128) + 1); }
static void WriteBagItem(Wire w, TBagItem value) { w.UInt(value.@token);
w.UInt(value.@quantity);
w.Text(value.@kind);
w.Bool(value.@canEquip); }
static TBagItem ReadBagItem(Wire r) => new TBagItem(r.UInt(), r.UInt(), r.Text(128), r.Bool());
public static byte[] Encode(TEquipBagItem value) { var w = new Wire(SizeEquipBagItem(value)); WriteEquipBagItem(w, value); return w.Bytes; }
public static TEquipBagItem DecodeEquipBagItem(byte[] bytes) { var r = new Wire(bytes); var value = ReadEquipBagItem(r); r.End(); return value; }
static int SizeEquipBagItem(TEquipBagItem value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + 4); }
static void WriteEquipBagItem(Wire w, TEquipBagItem value) { w.UInt(value.@epoch);
w.UInt(value.@revision);
w.UInt(value.@token); }
static TEquipBagItem ReadEquipBagItem(Wire r) => new TEquipBagItem(r.UInt(), r.UInt(), r.UInt());
public static byte[] Encode(TBagEquipResult value) { var w = new Wire(SizeBagEquipResult(value)); WriteBagEquipResult(w, value); return w.Bytes; }
public static TBagEquipResult DecodeBagEquipResult(byte[] bytes) { var r = new Wire(bytes); var value = ReadBagEquipResult(r); r.End(); return value; }
static int SizeBagEquipResult(TBagEquipResult value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1); }
static void WriteBagEquipResult(Wire w, TBagEquipResult value) { w.Bool(value.@accepted); }
static TBagEquipResult ReadBagEquipResult(Wire r) => new TBagEquipResult(r.Bool());
public static byte[] Encode(TEquipmentState value) { var w = new Wire(SizeEquipmentState(value)); WriteEquipmentState(w, value); return w.Bytes; }
public static TEquipmentState DecodeEquipmentState(byte[] bytes) { var r = new Wire(bytes); var value = ReadEquipmentState(r); r.End(); return value; }
static int SizeEquipmentState(TEquipmentState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@status, 16) + 4 + 4 + 4 + 4 + 1 + Wire.ListSize(value.@items, 8, item0 => SizeEquippedItem(item0))); }
static void WriteEquipmentState(Wire w, TEquipmentState value) { w.Text(value.@status);
w.UInt(value.@epoch);
w.UInt(value.@generation);
w.UInt(value.@revision);
w.UInt(value.@health);
w.Bool(value.@surface);
w.UInt((uint)value.@items.Count); foreach (var item0 in value.@items) { WriteEquippedItem(w, item0); } }
static TEquipmentState ReadEquipmentState(Wire r) => new TEquipmentState(r.Text(16), r.UInt(), r.UInt(), r.UInt(), r.UInt(), r.Bool(), r.List(8, () => ReadEquippedItem(r)));
public static byte[] Encode(TEquippedItem value) { var w = new Wire(SizeEquippedItem(value)); WriteEquippedItem(w, value); return w.Bytes; }
public static TEquippedItem DecodeEquippedItem(byte[] bytes) { var r = new Wire(bytes); var value = ReadEquippedItem(r); r.End(); return value; }
static int SizeEquippedItem(TEquippedItem value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@slot, 16) + Wire.TextSize(value.@kind, 128) + 1); }
static void WriteEquippedItem(Wire w, TEquippedItem value) { w.Text(value.@slot);
w.Text(value.@kind);
w.Bool(value.@canUnequip); }
static TEquippedItem ReadEquippedItem(Wire r) => new TEquippedItem(r.Text(16), r.Text(128), r.Bool());
public static byte[] Encode(TUnequipItem value) { var w = new Wire(SizeUnequipItem(value)); WriteUnequipItem(w, value); return w.Bytes; }
public static TUnequipItem DecodeUnequipItem(byte[] bytes) { var r = new Wire(bytes); var value = ReadUnequipItem(r); r.End(); return value; }
static int SizeUnequipItem(TUnequipItem value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + 4 + Wire.TextSize(value.@slot, 16)); }
static void WriteUnequipItem(Wire w, TUnequipItem value) { w.UInt(value.@epoch);
w.UInt(value.@generation);
w.UInt(value.@revision);
w.Text(value.@slot); }
static TUnequipItem ReadUnequipItem(Wire r) => new TUnequipItem(r.UInt(), r.UInt(), r.UInt(), r.Text(16));
public static byte[] Encode(TUnequipResult value) { var w = new Wire(SizeUnequipResult(value)); WriteUnequipResult(w, value); return w.Bytes; }
public static TUnequipResult DecodeUnequipResult(byte[] bytes) { var r = new Wire(bytes); var value = ReadUnequipResult(r); r.End(); return value; }
static int SizeUnequipResult(TUnequipResult value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1); }
static void WriteUnequipResult(Wire w, TUnequipResult value) { w.Bool(value.@accepted); }
static TUnequipResult ReadUnequipResult(Wire r) => new TUnequipResult(r.Bool());
public static byte[] Encode(TLootState value) { var w = new Wire(SizeLootState(value)); WriteLootState(w, value); return w.Bytes; }
public static TLootState DecodeLootState(byte[] bytes) { var r = new Wire(bytes); var value = ReadLootState(r); r.End(); return value; }
static int SizeLootState(TLootState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + Wire.ListSize(value.@entries, 24, item0 => SizeLootEntry(item0)) + 4 + Wire.ListSize(value.@corpses, 64, item0 => SizeLootableCorpse(item0))); }
static void WriteLootState(Wire w, TLootState value) { w.UInt(value.@sequence);
w.UInt(value.@state);
w.UInt((uint)value.@entries.Count); foreach (var item0 in value.@entries) { WriteLootEntry(w, item0); }
w.UInt(value.@epoch);
w.UInt((uint)value.@corpses.Count); foreach (var item0 in value.@corpses) { WriteLootableCorpse(w, item0); } }
static TLootState ReadLootState(Wire r) => new TLootState(r.UInt(), r.UInt(), r.List(24, () => ReadLootEntry(r)), r.UInt(), r.List(64, () => ReadLootableCorpse(r)));
public static byte[] Encode(TLootEntry value) { var w = new Wire(SizeLootEntry(value)); WriteLootEntry(w, value); return w.Bytes; }
public static TLootEntry DecodeLootEntry(byte[] bytes) { var r = new Wire(bytes); var value = ReadLootEntry(r); r.End(); return value; }
static int SizeLootEntry(TLootEntry value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@kind, 128) + Wire.TextSize(value.@name, 128) + 4); }
static void WriteLootEntry(Wire w, TLootEntry value) { w.Text(value.@kind);
w.Text(value.@name);
w.UInt(value.@quantity); }
static TLootEntry ReadLootEntry(Wire r) => new TLootEntry(r.Text(128), r.Text(128), r.UInt());
public static byte[] Encode(TLootableCorpse value) { var w = new Wire(SizeLootableCorpse(value)); WriteLootableCorpse(w, value); return w.Bytes; }
public static TLootableCorpse DecodeLootableCorpse(byte[] bytes) { var r = new Wire(bytes); var value = ReadLootableCorpse(r); r.End(); return value; }
static int SizeLootableCorpse(TLootableCorpse value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 128) + Wire.TextSize(value.@reference, 64)); }
static void WriteLootableCorpse(Wire w, TLootableCorpse value) { w.Text(value.@id);
w.Text(value.@reference); }
static TLootableCorpse ReadLootableCorpse(Wire r) => new TLootableCorpse(r.Text(128), r.Text(64));
public static byte[] Encode(TLootIntent value) { var w = new Wire(SizeLootIntent(value)); WriteLootIntent(w, value); return w.Bytes; }
public static TLootIntent DecodeLootIntent(byte[] bytes) { var r = new Wire(bytes); var value = ReadLootIntent(r); r.End(); return value; }
static int SizeLootIntent(TLootIntent value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4); }
static void WriteLootIntent(Wire w, TLootIntent value) { w.UInt(value.@sequence);
w.UInt(value.@action); }
static TLootIntent ReadLootIntent(Wire r) => new TLootIntent(r.UInt(), r.UInt());
public static byte[] Encode(TLootResult value) { var w = new Wire(SizeLootResult(value)); WriteLootResult(w, value); return w.Bytes; }
public static TLootResult DecodeLootResult(byte[] bytes) { var r = new Wire(bytes); var value = ReadLootResult(r); r.End(); return value; }
static int SizeLootResult(TLootResult value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1); }
static void WriteLootResult(Wire w, TLootResult value) { w.Bool(value.@accepted); }
static TLootResult ReadLootResult(Wire r) => new TLootResult(r.Bool());
public static byte[] Encode(TOpenLoot value) { var w = new Wire(SizeOpenLoot(value)); WriteOpenLoot(w, value); return w.Bytes; }
public static TOpenLoot DecodeOpenLoot(byte[] bytes) { var r = new Wire(bytes); var value = ReadOpenLoot(r); r.End(); return value; }
static int SizeOpenLoot(TOpenLoot value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + Wire.TextSize(value.@corpseId, 128)); }
static void WriteOpenLoot(Wire w, TOpenLoot value) { w.UInt(value.@epoch);
w.Text(value.@corpseId); }
static TOpenLoot ReadOpenLoot(Wire r) => new TOpenLoot(r.UInt(), r.Text(128));
public static byte[] Encode(TMerchantState value) { var w = new Wire(SizeMerchantState(value)); WriteMerchantState(w, value); return w.Bytes; }
public static TMerchantState DecodeMerchantState(byte[] bytes) { var r = new Wire(bytes); var value = ReadMerchantState(r); r.End(); return value; }
static int SizeMerchantState(TMerchantState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1 + 4 + 1 + 1 + 1 + Wire.TextSize(value.@state, 64) + Wire.TextSize(value.@name, 512) + 4 + Wire.TextSize(value.@notice, 1024) + Wire.ListSize(value.@offers, 64, item0 => SizeMerchantOffer(item0)) + Wire.ListSize(value.@bag, 24, item0 => SizeMerchantItem(item0))); }
static void WriteMerchantState(Wire w, TMerchantState value) { w.Bool(value.@available);
w.UInt(value.@sequence);
w.Bool(value.@ready);
w.Bool(value.@pending);
w.Bool(value.@retry);
w.Text(value.@state);
w.Text(value.@name);
w.UInt(value.@coins);
w.Text(value.@notice);
w.UInt((uint)value.@offers.Count); foreach (var item0 in value.@offers) { WriteMerchantOffer(w, item0); }
w.UInt((uint)value.@bag.Count); foreach (var item0 in value.@bag) { WriteMerchantItem(w, item0); } }
static TMerchantState ReadMerchantState(Wire r) => new TMerchantState(r.Bool(), r.UInt(), r.Bool(), r.Bool(), r.Bool(), r.Text(64), r.Text(512), r.UInt(), r.Text(1024), r.List(64, () => ReadMerchantOffer(r)), r.List(24, () => ReadMerchantItem(r)));
public static byte[] Encode(TMerchantOffer value) { var w = new Wire(SizeMerchantOffer(value)); WriteMerchantOffer(w, value); return w.Bytes; }
public static TMerchantOffer DecodeMerchantOffer(byte[] bytes) { var r = new Wire(bytes); var value = ReadMerchantOffer(r); r.End(); return value; }
static int SizeMerchantOffer(TMerchantOffer value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 16) + Wire.TextSize(value.@kind, 128) + Wire.TextSize(value.@name, 512) + 4 + 4); }
static void WriteMerchantOffer(Wire w, TMerchantOffer value) { w.Text(value.@id);
w.Text(value.@kind);
w.Text(value.@name);
w.UInt(value.@price);
w.UInt(value.@quantity); }
static TMerchantOffer ReadMerchantOffer(Wire r) => new TMerchantOffer(r.Text(16), r.Text(128), r.Text(512), r.UInt(), r.UInt());
public static byte[] Encode(TMerchantItem value) { var w = new Wire(SizeMerchantItem(value)); WriteMerchantItem(w, value); return w.Bytes; }
public static TMerchantItem DecodeMerchantItem(byte[] bytes) { var r = new Wire(bytes); var value = ReadMerchantItem(r); r.End(); return value; }
static int SizeMerchantItem(TMerchantItem value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1 + Wire.TextSize(value.@id, 16) + Wire.TextSize(value.@kind, 128) + 4 + 1 + 4 + Wire.TextSize(value.@reason, 256)); }
static void WriteMerchantItem(Wire w, TMerchantItem value) { w.Bool(value.@present);
w.Text(value.@id);
w.Text(value.@kind);
w.UInt(value.@quantity);
w.Bool(value.@sellable);
w.UInt(value.@salePrice);
w.Text(value.@reason); }
static TMerchantItem ReadMerchantItem(Wire r) => new TMerchantItem(r.Bool(), r.Text(16), r.Text(128), r.UInt(), r.Bool(), r.UInt(), r.Text(256));
public static byte[] Encode(TMerchantIntent value) { var w = new Wire(SizeMerchantIntent(value)); WriteMerchantIntent(w, value); return w.Bytes; }
public static TMerchantIntent DecodeMerchantIntent(byte[] bytes) { var r = new Wire(bytes); var value = ReadMerchantIntent(r); r.End(); return value; }
static int SizeMerchantIntent(TMerchantIntent value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + 4); }
static void WriteMerchantIntent(Wire w, TMerchantIntent value) { w.UInt(value.@sequence);
w.UInt(value.@action);
w.UInt(value.@index); }
static TMerchantIntent ReadMerchantIntent(Wire r) => new TMerchantIntent(r.UInt(), r.UInt(), r.UInt());
public static byte[] Encode(TMerchantResult value) { var w = new Wire(SizeMerchantResult(value)); WriteMerchantResult(w, value); return w.Bytes; }
public static TMerchantResult DecodeMerchantResult(byte[] bytes) { var r = new Wire(bytes); var value = ReadMerchantResult(r); r.End(); return value; }
static int SizeMerchantResult(TMerchantResult value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1); }
static void WriteMerchantResult(Wire w, TMerchantResult value) { w.Bool(value.@accepted); }
static TMerchantResult ReadMerchantResult(Wire r) => new TMerchantResult(r.Bool());
public static byte[] Encode(TQuestPrerequisites value) { var w = new Wire(SizeQuestPrerequisites(value)); WriteQuestPrerequisites(w, value); return w.Bytes; }
public static TQuestPrerequisites DecodeQuestPrerequisites(byte[] bytes) { var r = new Wire(bytes); var value = ReadQuestPrerequisites(r); r.End(); return value; }
static int SizeQuestPrerequisites(TQuestPrerequisites value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + Wire.TextSize(value.@root, 128) + Wire.ListSize(value.@nodes, 256, item0 => SizeQuestConditionNode(item0))); }
static void WriteQuestPrerequisites(Wire w, TQuestPrerequisites value) { w.UInt(value.@version);
w.Text(value.@root);
w.UInt((uint)value.@nodes.Count); foreach (var item0 in value.@nodes) { WriteQuestConditionNode(w, item0); } }
static TQuestPrerequisites ReadQuestPrerequisites(Wire r) => new TQuestPrerequisites(r.UInt(), r.Text(128), r.List(256, () => ReadQuestConditionNode(r)));
public static byte[] Encode(TQuestConditionNode value) { var w = new Wire(SizeQuestConditionNode(value)); WriteQuestConditionNode(w, value); return w.Bytes; }
public static TQuestConditionNode DecodeQuestConditionNode(byte[] bytes) { var r = new Wire(bytes); var value = ReadQuestConditionNode(r); r.End(); return value; }
static int SizeQuestConditionNode(TQuestConditionNode value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 128) + Wire.TextSize(value.@kind, 32) + Wire.TextSize(value.@subject, 128) + Wire.ListSize(value.@children, 32, item0 => Wire.TextSize(item0, 128))); }
static void WriteQuestConditionNode(Wire w, TQuestConditionNode value) { w.Text(value.@id);
w.Text(value.@kind);
w.Text(value.@subject);
w.UInt((uint)value.@children.Count); foreach (var item0 in value.@children) { w.Text(item0); } }
static TQuestConditionNode ReadQuestConditionNode(Wire r) => new TQuestConditionNode(r.Text(128), r.Text(32), r.Text(128), r.List(32, () => r.Text(128)));
public static byte[] Encode(TQuestDefinition value) { var w = new Wire(SizeQuestDefinition(value)); WriteQuestDefinition(w, value); return w.Bytes; }
public static TQuestDefinition DecodeQuestDefinition(byte[] bytes) { var r = new Wire(bytes); var value = ReadQuestDefinition(r); r.End(); return value; }
static int SizeQuestDefinition(TQuestDefinition value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 128) + 4 + Wire.TextSize(value.@title, 512) + Wire.TextSize(value.@description, 4096) + SizeQuestPrerequisites(value.@prerequisites) + Wire.ListSize(value.@objectives, 32, item0 => SizeQuestObjectiveDefinition(item0)) + Wire.TextSize(value.@completion, 32) + Wire.TextSize(value.@repeat, 32) + Wire.TextSize(value.@reward, 128)); }
static void WriteQuestDefinition(Wire w, TQuestDefinition value) { w.Text(value.@id);
w.UInt(value.@revision);
w.Text(value.@title);
w.Text(value.@description);
WriteQuestPrerequisites(w, value.@prerequisites);
w.UInt((uint)value.@objectives.Count); foreach (var item0 in value.@objectives) { WriteQuestObjectiveDefinition(w, item0); }
w.Text(value.@completion);
w.Text(value.@repeat);
w.Text(value.@reward); }
static TQuestDefinition ReadQuestDefinition(Wire r) => new TQuestDefinition(r.Text(128), r.UInt(), r.Text(512), r.Text(4096), ReadQuestPrerequisites(r), r.List(32, () => ReadQuestObjectiveDefinition(r)), r.Text(32), r.Text(32), r.Text(128));
public static byte[] Encode(TQuestObjectiveDefinition value) { var w = new Wire(SizeQuestObjectiveDefinition(value)); WriteQuestObjectiveDefinition(w, value); return w.Bytes; }
public static TQuestObjectiveDefinition DecodeQuestObjectiveDefinition(byte[] bytes) { var r = new Wire(bytes); var value = ReadQuestObjectiveDefinition(r); r.End(); return value; }
static int SizeQuestObjectiveDefinition(TQuestObjectiveDefinition value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 128) + Wire.TextSize(value.@event, 32) + Wire.TextSize(value.@target, 128) + Wire.TextSize(value.@world, 128) + Wire.TextSize(value.@description, 1024) + 4 + Wire.ListSize(value.@after, 32, item0 => Wire.TextSize(item0, 128))); }
static void WriteQuestObjectiveDefinition(Wire w, TQuestObjectiveDefinition value) { w.Text(value.@id);
w.Text(value.@event);
w.Text(value.@target);
w.Text(value.@world);
w.Text(value.@description);
w.UInt(value.@required);
w.UInt((uint)value.@after.Count); foreach (var item0 in value.@after) { w.Text(item0); } }
static TQuestObjectiveDefinition ReadQuestObjectiveDefinition(Wire r) => new TQuestObjectiveDefinition(r.Text(128), r.Text(32), r.Text(128), r.Text(128), r.Text(1024), r.UInt(), r.List(32, () => r.Text(128)));
public static byte[] Encode(TQuestBinding value) { var w = new Wire(SizeQuestBinding(value)); WriteQuestBinding(w, value); return w.Bytes; }
public static TQuestBinding DecodeQuestBinding(byte[] bytes) { var r = new Wire(bytes); var value = ReadQuestBinding(r); r.End(); return value; }
static int SizeQuestBinding(TQuestBinding value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@quest, 128) + Wire.TextSize(value.@role, 32) + Wire.TextSize(value.@actor, 128)); }
static void WriteQuestBinding(Wire w, TQuestBinding value) { w.Text(value.@quest);
w.Text(value.@role);
w.Text(value.@actor); }
static TQuestBinding ReadQuestBinding(Wire r) => new TQuestBinding(r.Text(128), r.Text(32), r.Text(128));
public static byte[] Encode(TQuestFact value) { var w = new Wire(SizeQuestFact(value)); WriteQuestFact(w, value); return w.Bytes; }
public static TQuestFact DecodeQuestFact(byte[] bytes) { var r = new Wire(bytes); var value = ReadQuestFact(r); r.End(); return value; }
static int SizeQuestFact(TQuestFact value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 128) + Wire.TextSize(value.@type, 32) + Wire.TextSize(value.@owner, 128) + Wire.TextSize(value.@scope, 32) + Wire.TextSize(value.@lifetime, 32)); }
static void WriteQuestFact(Wire w, TQuestFact value) { w.Text(value.@id);
w.Text(value.@type);
w.Text(value.@owner);
w.Text(value.@scope);
w.Text(value.@lifetime); }
static TQuestFact ReadQuestFact(Wire r) => new TQuestFact(r.Text(128), r.Text(32), r.Text(128), r.Text(32), r.Text(32));
}
// Borrow input only during synchronous decode. Encode allocates one exact-size buffer.
internal sealed class Wire {
static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
public readonly byte[] Bytes;
int offset;
public Wire(int length) { Bytes = new byte[length]; }
public Wire(byte[] bytes) { Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes)); }
void Need(int length) { if (length < 0 || length > Bytes.Length - offset) throw new FormatException("Truncated feature payload"); }
public void End() { if (offset != Bytes.Length) throw new FormatException("Trailing feature payload"); }
public void UInt(uint value) { Need(4); for (int n = 0; n < 4; n++) Bytes[offset++] = (byte)(value >> (n * 8)); }
public uint UInt() { Need(4); uint value = 0; for (int n = 0; n < 4; n++) value |= (uint)Bytes[offset++] << (n * 8); return value; }
public void Bool(bool value) { Need(1); Bytes[offset++] = value ? (byte)1 : (byte)0; }
public bool Bool() { Need(1); byte value = Bytes[offset++]; if (value > 1) throw new FormatException("Invalid bool"); return value == 1; }
public static int TextSize(string value, int max) { if (value == null) throw new ArgumentNullException(nameof(value)); int count = Utf8.GetByteCount(value); if (count > max) throw new FormatException("Text bound exceeded"); return checked(4 + count); }
public static int ListSize<T>(IReadOnlyList<T> values, int max, Func<T, int> measure) { if (values == null || values.Count > max) throw new FormatException("List bound exceeded"); int size = 4; foreach (var value in values) size = checked(size + measure(value)); return size; }
public void Text(string value) { int size = Utf8.GetByteCount(value); UInt((uint)size); Need(size); offset += Utf8.GetBytes(value, 0, value.Length, Bytes, offset); }
public string Text(int max) { uint count = UInt(); if (count > max) throw new FormatException("Text bound exceeded"); int size = (int)count; Need(size); string value = Utf8.GetString(Bytes, offset, size); offset += size; return value; }
public IReadOnlyList<T> List<T>(int max, Func<T> read) { uint count = UInt(); if (count > max || count > Bytes.Length - offset) throw new FormatException("Invalid list count"); var result = new T[(int)count]; for (int n = 0; n < result.Length; n++) result[n] = read(); return Array.AsReadOnly(result); }
}
}
