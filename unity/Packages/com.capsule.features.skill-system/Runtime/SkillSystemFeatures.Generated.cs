// Generated from TypeScript feature definitions. Do not edit.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace SkillSystem.Features {
public sealed class TSkillProgressionState {
public string @freshness { get; }
public uint @epoch { get; }
public uint @ownerGeneration { get; }
public uint @sequence { get; }
public bool @available { get; }
public IReadOnlyList<TSkillProgress> @skills { get; }
public IReadOnlyList<TProgressionResource> @resources { get; }
public TSkillProgressionState(string @freshness, uint @epoch, uint @ownerGeneration, uint @sequence, bool @available, IReadOnlyList<TSkillProgress> @skills, IReadOnlyList<TProgressionResource> @resources) {
this.@freshness = @freshness;
this.@epoch = @epoch;
this.@ownerGeneration = @ownerGeneration;
this.@sequence = @sequence;
this.@available = @available;
this.@skills = @skills;
this.@resources = @resources;
}
}
public sealed class TSkillProgress {
public string @id { get; }
public string @kind { get; }
public string @name { get; }
public string @description { get; }
public string @icon { get; }
public uint @rank { get; }
public uint @maximumRank { get; }
public uint @experience { get; }
public uint @nextRankExperience { get; }
public bool @unlocked { get; }
public IReadOnlyList<TSkillRequirement> @requirements { get; }
public TSkillProgress(string @id, string @kind, string @name, string @description, string @icon, uint @rank, uint @maximumRank, uint @experience, uint @nextRankExperience, bool @unlocked, IReadOnlyList<TSkillRequirement> @requirements) {
this.@id = @id;
this.@kind = @kind;
this.@name = @name;
this.@description = @description;
this.@icon = @icon;
this.@rank = @rank;
this.@maximumRank = @maximumRank;
this.@experience = @experience;
this.@nextRankExperience = @nextRankExperience;
this.@unlocked = @unlocked;
this.@requirements = @requirements;
}
}
public sealed class TSkillRequirement {
public string @id { get; }
public string @description { get; }
public bool @met { get; }
public TSkillRequirement(string @id, string @description, bool @met) {
this.@id = @id;
this.@description = @description;
this.@met = @met;
}
}
public sealed class TProgressionResource {
public string @id { get; }
public uint @value { get; }
public uint @spent { get; }
public uint @limit { get; }
public TProgressionResource(string @id, uint @value, uint @spent, uint @limit) {
this.@id = @id;
this.@value = @value;
this.@spent = @spent;
this.@limit = @limit;
}
}
public sealed class TTrainingState {
public string @freshness { get; }
public uint @epoch { get; }
public uint @ownerGeneration { get; }
public uint @sequence { get; }
public bool @open { get; }
public string @source { get; }
public IReadOnlyList<TTrainingOffer> @offers { get; }
public IReadOnlyList<TProgressionResource> @resources { get; }
public TTrainingState(string @freshness, uint @epoch, uint @ownerGeneration, uint @sequence, bool @open, string @source, IReadOnlyList<TTrainingOffer> @offers, IReadOnlyList<TProgressionResource> @resources) {
this.@freshness = @freshness;
this.@epoch = @epoch;
this.@ownerGeneration = @ownerGeneration;
this.@sequence = @sequence;
this.@open = @open;
this.@source = @source;
this.@offers = @offers;
this.@resources = @resources;
}
}
public sealed class TTrainingOffer {
public string @id { get; }
public string @skill { get; }
public uint @rank { get; }
public uint @currentRank { get; }
public bool @available { get; }
public string @reason { get; }
public IReadOnlyList<TSkillCost> @costs { get; }
public IReadOnlyList<TSkillRequirement> @requirements { get; }
public TTrainingOffer(string @id, string @skill, uint @rank, uint @currentRank, bool @available, string @reason, IReadOnlyList<TSkillCost> @costs, IReadOnlyList<TSkillRequirement> @requirements) {
this.@id = @id;
this.@skill = @skill;
this.@rank = @rank;
this.@currentRank = @currentRank;
this.@available = @available;
this.@reason = @reason;
this.@costs = @costs;
this.@requirements = @requirements;
}
}
public sealed class TSkillCost {
public string @resource { get; }
public uint @amount { get; }
public bool @affordable { get; }
public TSkillCost(string @resource, uint @amount, bool @affordable) {
this.@resource = @resource;
this.@amount = @amount;
this.@affordable = @affordable;
}
}
public sealed class TTrainingIntent {
public uint @epoch { get; }
public uint @ownerGeneration { get; }
public uint @sequence { get; }
public string @offer { get; }
public bool @close { get; }
public TTrainingIntent(uint @epoch, uint @ownerGeneration, uint @sequence, string @offer, bool @close) {
this.@epoch = @epoch;
this.@ownerGeneration = @ownerGeneration;
this.@sequence = @sequence;
this.@offer = @offer;
this.@close = @close;
}
}
public sealed class TTrainingResult {
public bool @accepted { get; }
public TTrainingResult(bool @accepted) {
this.@accepted = @accepted;
}
}
public interface ISkillProgressionProvider {
TSkillProgressionState GetState();
event Action<TSkillProgressionState> StateChanged;

}
public static class SkillProgressionContract {
public const string Name = "@skill-system/progression", Version = "1.0.0", Fingerprint = "d565b7a1e669ac0d48a3274a0587281baff3e13dc80a9e3f322cfd57c3a89a36";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] {  },
new string[] { "progression.read" });
public static SkillProgressionBinding Bind(ISkillProgressionProvider provider) => new SkillProgressionBinding(provider);
}
public interface ITrainingProvider {
TTrainingState GetState();
event Action<TTrainingState> StateChanged;
Task<TTrainingResult> Interact(TTrainingIntent request, CancellationToken cancellationToken);
}
public static class TrainingContract {
public const string Name = "@skill-system/training", Version = "1.0.0", Fingerprint = "c0d7ee1ecf3fbb33b0f53bac7afac0c352060c955f99ede906a6c7fc7cca15d9";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] {  },
new string[] { "training.read", "training.interact" });
public static TrainingBinding Bind(ITrainingProvider provider) => new TrainingBinding(provider);
}
public sealed class SkillProgressionBinding : Capsule.UiFeatureProvider {
readonly ISkillProgressionProvider provider;
public SkillProgressionBinding(ISkillProgressionProvider provider) : base(SkillProgressionContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "progression.read", SkillSystemFeatureCodec.Encode(provider.GetState()));
provider.StateChanged += OnState;

}
void OnState(TSkillProgressionState value) => Publish("State", SkillSystemFeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public sealed class TrainingBinding : Capsule.UiFeatureProvider {
readonly ITrainingProvider provider;
public TrainingBinding(ITrainingProvider provider) : base(TrainingContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "training.read", SkillSystemFeatureCodec.Encode(provider.GetState()));
provider.StateChanged += OnState;
AddRequest("Interact", "training.interact", async (bytes, cancellation) => SkillSystemFeatureCodec.Encode(await provider.Interact(SkillSystemFeatureCodec.DecodeTrainingIntent(bytes), cancellation)));
}
void OnState(TTrainingState value) => Publish("State", SkillSystemFeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public static class SkillSystemFeatureCodec {
public static byte[] Encode(TSkillProgressionState value) { var w = new Wire(SizeSkillProgressionState(value)); WriteSkillProgressionState(w, value); return w.Bytes; }
public static TSkillProgressionState DecodeSkillProgressionState(byte[] bytes) { var r = new Wire(bytes); var value = ReadSkillProgressionState(r); r.End(); return value; }
static int SizeSkillProgressionState(TSkillProgressionState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@freshness, 16) + 4 + 4 + 4 + 1 + Wire.ListSize(value.@skills, 256, item0 => SizeSkillProgress(item0)) + Wire.ListSize(value.@resources, 32, item0 => SizeProgressionResource(item0))); }
static void WriteSkillProgressionState(Wire w, TSkillProgressionState value) { w.Text(value.@freshness);
w.UInt(value.@epoch);
w.UInt(value.@ownerGeneration);
w.UInt(value.@sequence);
w.Bool(value.@available);
w.UInt((uint)value.@skills.Count); foreach (var item0 in value.@skills) { WriteSkillProgress(w, item0); }
w.UInt((uint)value.@resources.Count); foreach (var item0 in value.@resources) { WriteProgressionResource(w, item0); } }
static TSkillProgressionState ReadSkillProgressionState(Wire r) => new TSkillProgressionState(r.Text(16), r.UInt(), r.UInt(), r.UInt(), r.Bool(), r.List(256, () => ReadSkillProgress(r)), r.List(32, () => ReadProgressionResource(r)));
public static byte[] Encode(TSkillProgress value) { var w = new Wire(SizeSkillProgress(value)); WriteSkillProgress(w, value); return w.Bytes; }
public static TSkillProgress DecodeSkillProgress(byte[] bytes) { var r = new Wire(bytes); var value = ReadSkillProgress(r); r.End(); return value; }
static int SizeSkillProgress(TSkillProgress value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 128) + Wire.TextSize(value.@kind, 32) + Wire.TextSize(value.@name, 128) + Wire.TextSize(value.@description, 2048) + Wire.TextSize(value.@icon, 128) + 4 + 4 + 4 + 4 + 1 + Wire.ListSize(value.@requirements, 32, item0 => SizeSkillRequirement(item0))); }
static void WriteSkillProgress(Wire w, TSkillProgress value) { w.Text(value.@id);
w.Text(value.@kind);
w.Text(value.@name);
w.Text(value.@description);
w.Text(value.@icon);
w.UInt(value.@rank);
w.UInt(value.@maximumRank);
w.UInt(value.@experience);
w.UInt(value.@nextRankExperience);
w.Bool(value.@unlocked);
w.UInt((uint)value.@requirements.Count); foreach (var item0 in value.@requirements) { WriteSkillRequirement(w, item0); } }
static TSkillProgress ReadSkillProgress(Wire r) => new TSkillProgress(r.Text(128), r.Text(32), r.Text(128), r.Text(2048), r.Text(128), r.UInt(), r.UInt(), r.UInt(), r.UInt(), r.Bool(), r.List(32, () => ReadSkillRequirement(r)));
public static byte[] Encode(TSkillRequirement value) { var w = new Wire(SizeSkillRequirement(value)); WriteSkillRequirement(w, value); return w.Bytes; }
public static TSkillRequirement DecodeSkillRequirement(byte[] bytes) { var r = new Wire(bytes); var value = ReadSkillRequirement(r); r.End(); return value; }
static int SizeSkillRequirement(TSkillRequirement value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 128) + Wire.TextSize(value.@description, 512) + 1); }
static void WriteSkillRequirement(Wire w, TSkillRequirement value) { w.Text(value.@id);
w.Text(value.@description);
w.Bool(value.@met); }
static TSkillRequirement ReadSkillRequirement(Wire r) => new TSkillRequirement(r.Text(128), r.Text(512), r.Bool());
public static byte[] Encode(TProgressionResource value) { var w = new Wire(SizeProgressionResource(value)); WriteProgressionResource(w, value); return w.Bytes; }
public static TProgressionResource DecodeProgressionResource(byte[] bytes) { var r = new Wire(bytes); var value = ReadProgressionResource(r); r.End(); return value; }
static int SizeProgressionResource(TProgressionResource value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 128) + 4 + 4 + 4); }
static void WriteProgressionResource(Wire w, TProgressionResource value) { w.Text(value.@id);
w.UInt(value.@value);
w.UInt(value.@spent);
w.UInt(value.@limit); }
static TProgressionResource ReadProgressionResource(Wire r) => new TProgressionResource(r.Text(128), r.UInt(), r.UInt(), r.UInt());
public static byte[] Encode(TTrainingState value) { var w = new Wire(SizeTrainingState(value)); WriteTrainingState(w, value); return w.Bytes; }
public static TTrainingState DecodeTrainingState(byte[] bytes) { var r = new Wire(bytes); var value = ReadTrainingState(r); r.End(); return value; }
static int SizeTrainingState(TTrainingState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@freshness, 16) + 4 + 4 + 4 + 1 + Wire.TextSize(value.@source, 128) + Wire.ListSize(value.@offers, 128, item0 => SizeTrainingOffer(item0)) + Wire.ListSize(value.@resources, 32, item0 => SizeProgressionResource(item0))); }
static void WriteTrainingState(Wire w, TTrainingState value) { w.Text(value.@freshness);
w.UInt(value.@epoch);
w.UInt(value.@ownerGeneration);
w.UInt(value.@sequence);
w.Bool(value.@open);
w.Text(value.@source);
w.UInt((uint)value.@offers.Count); foreach (var item0 in value.@offers) { WriteTrainingOffer(w, item0); }
w.UInt((uint)value.@resources.Count); foreach (var item0 in value.@resources) { WriteProgressionResource(w, item0); } }
static TTrainingState ReadTrainingState(Wire r) => new TTrainingState(r.Text(16), r.UInt(), r.UInt(), r.UInt(), r.Bool(), r.Text(128), r.List(128, () => ReadTrainingOffer(r)), r.List(32, () => ReadProgressionResource(r)));
public static byte[] Encode(TTrainingOffer value) { var w = new Wire(SizeTrainingOffer(value)); WriteTrainingOffer(w, value); return w.Bytes; }
public static TTrainingOffer DecodeTrainingOffer(byte[] bytes) { var r = new Wire(bytes); var value = ReadTrainingOffer(r); r.End(); return value; }
static int SizeTrainingOffer(TTrainingOffer value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 128) + Wire.TextSize(value.@skill, 128) + 4 + 4 + 1 + Wire.TextSize(value.@reason, 512) + Wire.ListSize(value.@costs, 16, item0 => SizeSkillCost(item0)) + Wire.ListSize(value.@requirements, 32, item0 => SizeSkillRequirement(item0))); }
static void WriteTrainingOffer(Wire w, TTrainingOffer value) { w.Text(value.@id);
w.Text(value.@skill);
w.UInt(value.@rank);
w.UInt(value.@currentRank);
w.Bool(value.@available);
w.Text(value.@reason);
w.UInt((uint)value.@costs.Count); foreach (var item0 in value.@costs) { WriteSkillCost(w, item0); }
w.UInt((uint)value.@requirements.Count); foreach (var item0 in value.@requirements) { WriteSkillRequirement(w, item0); } }
static TTrainingOffer ReadTrainingOffer(Wire r) => new TTrainingOffer(r.Text(128), r.Text(128), r.UInt(), r.UInt(), r.Bool(), r.Text(512), r.List(16, () => ReadSkillCost(r)), r.List(32, () => ReadSkillRequirement(r)));
public static byte[] Encode(TSkillCost value) { var w = new Wire(SizeSkillCost(value)); WriteSkillCost(w, value); return w.Bytes; }
public static TSkillCost DecodeSkillCost(byte[] bytes) { var r = new Wire(bytes); var value = ReadSkillCost(r); r.End(); return value; }
static int SizeSkillCost(TSkillCost value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@resource, 128) + 4 + 1); }
static void WriteSkillCost(Wire w, TSkillCost value) { w.Text(value.@resource);
w.UInt(value.@amount);
w.Bool(value.@affordable); }
static TSkillCost ReadSkillCost(Wire r) => new TSkillCost(r.Text(128), r.UInt(), r.Bool());
public static byte[] Encode(TTrainingIntent value) { var w = new Wire(SizeTrainingIntent(value)); WriteTrainingIntent(w, value); return w.Bytes; }
public static TTrainingIntent DecodeTrainingIntent(byte[] bytes) { var r = new Wire(bytes); var value = ReadTrainingIntent(r); r.End(); return value; }
static int SizeTrainingIntent(TTrainingIntent value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + 4 + Wire.TextSize(value.@offer, 128) + 1); }
static void WriteTrainingIntent(Wire w, TTrainingIntent value) { w.UInt(value.@epoch);
w.UInt(value.@ownerGeneration);
w.UInt(value.@sequence);
w.Text(value.@offer);
w.Bool(value.@close); }
static TTrainingIntent ReadTrainingIntent(Wire r) => new TTrainingIntent(r.UInt(), r.UInt(), r.UInt(), r.Text(128), r.Bool());
public static byte[] Encode(TTrainingResult value) { var w = new Wire(SizeTrainingResult(value)); WriteTrainingResult(w, value); return w.Bytes; }
public static TTrainingResult DecodeTrainingResult(byte[] bytes) { var r = new Wire(bytes); var value = ReadTrainingResult(r); r.End(); return value; }
static int SizeTrainingResult(TTrainingResult value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1); }
static void WriteTrainingResult(Wire w, TTrainingResult value) { w.Bool(value.@accepted); }
static TTrainingResult ReadTrainingResult(Wire r) => new TTrainingResult(r.Bool());
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
