// Generated from TypeScript feature definitions. Do not edit.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Capsule.InventoryFeature {
public sealed class TItemState {
public uint @revision { get; }
public IReadOnlyList<TItem> @items { get; }
public TItemState(uint @revision, IReadOnlyList<TItem> @items) {
this.@revision = @revision;
this.@items = @items;
}
}
public sealed class TItem {
public string @id { get; }
public string @name { get; }
public string @asset { get; }
public TItem(string @id, string @name, string @asset) {
this.@id = @id;
this.@name = @name;
this.@asset = @asset;
}
}
public sealed class TInventoryState {
public bool @available { get; }
public uint @epoch { get; }
public uint @revision { get; }
public IReadOnlyList<TInventoryEntry> @entries { get; }
public TInventoryState(bool @available, uint @epoch, uint @revision, IReadOnlyList<TInventoryEntry> @entries) {
this.@available = @available;
this.@epoch = @epoch;
this.@revision = @revision;
this.@entries = @entries;
}
}
public sealed class TInventoryEntry {
public string @item { get; }
public string @container { get; }
public uint @quantity { get; }
public bool @transferable { get; }
public TInventoryEntry(string @item, string @container, uint @quantity, bool @transferable) {
this.@item = @item;
this.@container = @container;
this.@quantity = @quantity;
this.@transferable = @transferable;
}
}
public sealed class TTransfer {
public uint @requestId { get; }
public uint @epoch { get; }
public uint @revision { get; }
public string @item { get; }
public string @destination { get; }
public TTransfer(uint @requestId, uint @epoch, uint @revision, string @item, string @destination) {
this.@requestId = @requestId;
this.@epoch = @epoch;
this.@revision = @revision;
this.@item = @item;
this.@destination = @destination;
}
}
public sealed class TTransferResult {
public uint @requestId { get; }
public uint @outcome { get; }
public string @reason { get; }
public TTransferResult(uint @requestId, uint @outcome, string @reason) {
this.@requestId = @requestId;
this.@outcome = @outcome;
this.@reason = @reason;
}
}
public interface IItemsProvider {
TItemState GetState();
event Action<TItemState> StateChanged;

}
public static class ItemsContract {
public const string Name = "@capsule-example/items", Version = "1.0.0", Fingerprint = "a0077b6f3e0184219e32e53d050da1761d80676bb6e73c88cf9de944e27635b1";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] {  },
new string[] { "items.read" });
public static ItemsBinding Bind(IItemsProvider provider) => new ItemsBinding(provider);
}
public interface IInventoryProvider {
TInventoryState GetState();
event Action<TInventoryState> StateChanged;
Task<TTransferResult> Transfer(TTransfer request, CancellationToken cancellationToken);
}
public static class InventoryContract {
public const string Name = "@capsule-example/inventory", Version = "1.2.5", Fingerprint = "370bf4cc8f4cffc80338b82681c06bafea1b64788d237e13f8ae230f19cd67ac";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] { new Capsule.UiFeatureDependency("@capsule-example/items", "1.0.*") },
new string[] { "inventory.read", "inventory.transfer" });
public static InventoryBinding Bind(IInventoryProvider provider) => new InventoryBinding(provider);
}
public sealed class ItemsBinding : Capsule.UiFeatureProvider {
readonly IItemsProvider provider;
public ItemsBinding(IItemsProvider provider) : base(ItemsContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "items.read", FeatureCodec.Encode(provider.GetState()));
provider.StateChanged += OnState;

}
void OnState(TItemState value) => Publish("State", FeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public sealed class InventoryBinding : Capsule.UiFeatureProvider {
readonly IInventoryProvider provider;
public InventoryBinding(IInventoryProvider provider) : base(InventoryContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "inventory.read", FeatureCodec.Encode(provider.GetState()));
provider.StateChanged += OnState;
AddRequest("Transfer", "inventory.transfer", async (bytes, cancellation) => FeatureCodec.Encode(await provider.Transfer(FeatureCodec.DecodeTransfer(bytes), cancellation)));
}
void OnState(TInventoryState value) => Publish("State", FeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public static class FeatureCodec {
public static byte[] Encode(TItemState value) { var w = new Wire(SizeItemState(value)); WriteItemState(w, value); return w.Bytes; }
public static TItemState DecodeItemState(byte[] bytes) { var r = new Wire(bytes); var value = ReadItemState(r); r.End(); return value; }
static int SizeItemState(TItemState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + Wire.ListSize(value.@items, 4096, item0 => SizeItem(item0))); }
static void WriteItemState(Wire w, TItemState value) { w.UInt(value.@revision);
w.UInt((uint)value.@items.Count); foreach (var item0 in value.@items) { WriteItem(w, item0); } }
static TItemState ReadItemState(Wire r) => new TItemState(r.UInt(), r.List(4096, () => ReadItem(r)));
public static byte[] Encode(TItem value) { var w = new Wire(SizeItem(value)); WriteItem(w, value); return w.Bytes; }
public static TItem DecodeItem(byte[] bytes) { var r = new Wire(bytes); var value = ReadItem(r); r.End(); return value; }
static int SizeItem(TItem value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 128) + Wire.TextSize(value.@name, 128) + Wire.TextSize(value.@asset, 128)); }
static void WriteItem(Wire w, TItem value) { w.Text(value.@id);
w.Text(value.@name);
w.Text(value.@asset); }
static TItem ReadItem(Wire r) => new TItem(r.Text(128), r.Text(128), r.Text(128));
public static byte[] Encode(TInventoryState value) { var w = new Wire(SizeInventoryState(value)); WriteInventoryState(w, value); return w.Bytes; }
public static TInventoryState DecodeInventoryState(byte[] bytes) { var r = new Wire(bytes); var value = ReadInventoryState(r); r.End(); return value; }
static int SizeInventoryState(TInventoryState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1 + 4 + 4 + Wire.ListSize(value.@entries, 4096, item0 => SizeInventoryEntry(item0))); }
static void WriteInventoryState(Wire w, TInventoryState value) { w.Bool(value.@available);
w.UInt(value.@epoch);
w.UInt(value.@revision);
w.UInt((uint)value.@entries.Count); foreach (var item0 in value.@entries) { WriteInventoryEntry(w, item0); } }
static TInventoryState ReadInventoryState(Wire r) => new TInventoryState(r.Bool(), r.UInt(), r.UInt(), r.List(4096, () => ReadInventoryEntry(r)));
public static byte[] Encode(TInventoryEntry value) { var w = new Wire(SizeInventoryEntry(value)); WriteInventoryEntry(w, value); return w.Bytes; }
public static TInventoryEntry DecodeInventoryEntry(byte[] bytes) { var r = new Wire(bytes); var value = ReadInventoryEntry(r); r.End(); return value; }
static int SizeInventoryEntry(TInventoryEntry value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@item, 128) + Wire.TextSize(value.@container, 64) + 4 + 1); }
static void WriteInventoryEntry(Wire w, TInventoryEntry value) { w.Text(value.@item);
w.Text(value.@container);
w.UInt(value.@quantity);
w.Bool(value.@transferable); }
static TInventoryEntry ReadInventoryEntry(Wire r) => new TInventoryEntry(r.Text(128), r.Text(64), r.UInt(), r.Bool());
public static byte[] Encode(TTransfer value) { var w = new Wire(SizeTransfer(value)); WriteTransfer(w, value); return w.Bytes; }
public static TTransfer DecodeTransfer(byte[] bytes) { var r = new Wire(bytes); var value = ReadTransfer(r); r.End(); return value; }
static int SizeTransfer(TTransfer value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + 4 + Wire.TextSize(value.@item, 128) + Wire.TextSize(value.@destination, 64)); }
static void WriteTransfer(Wire w, TTransfer value) { w.UInt(value.@requestId);
w.UInt(value.@epoch);
w.UInt(value.@revision);
w.Text(value.@item);
w.Text(value.@destination); }
static TTransfer ReadTransfer(Wire r) => new TTransfer(r.UInt(), r.UInt(), r.UInt(), r.Text(128), r.Text(64));
public static byte[] Encode(TTransferResult value) { var w = new Wire(SizeTransferResult(value)); WriteTransferResult(w, value); return w.Bytes; }
public static TTransferResult DecodeTransferResult(byte[] bytes) { var r = new Wire(bytes); var value = ReadTransferResult(r); r.End(); return value; }
static int SizeTransferResult(TTransferResult value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + Wire.TextSize(value.@reason, 256)); }
static void WriteTransferResult(Wire w, TTransferResult value) { w.UInt(value.@requestId);
w.UInt(value.@outcome);
w.Text(value.@reason); }
static TTransferResult ReadTransferResult(Wire r) => new TTransferResult(r.UInt(), r.UInt(), r.Text(256));
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
