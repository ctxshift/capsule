// Generated from TypeScript feature definitions. Do not edit.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Mmo.Features {
public sealed class TChatState {
public uint @sequence { get; }
public uint @epoch { get; }
public bool @ready { get; }
public bool @pending { get; }
public uint @accepted { get; }
public string @error { get; }
public uint @wake { get; }
public IReadOnlyList<TChatMessage> @messages { get; }
public TChatState(uint @sequence, uint @epoch, bool @ready, bool @pending, uint @accepted, string @error, uint @wake, IReadOnlyList<TChatMessage> @messages) {
this.@sequence = @sequence;
this.@epoch = @epoch;
this.@ready = @ready;
this.@pending = @pending;
this.@accepted = @accepted;
this.@error = @error;
this.@wake = @wake;
this.@messages = @messages;
}
}
public sealed class TChatMessage {
public string @id { get; }
public string @channel { get; }
public string @sender { get; }
public string @text { get; }
public TChatMessage(string @id, string @channel, string @sender, string @text) {
this.@id = @id;
this.@channel = @channel;
this.@sender = @sender;
this.@text = @text;
}
}
public sealed class TChatSend {
public uint @epoch { get; }
public uint @sequence { get; }
public string @channel { get; }
public string @text { get; }
public string @recipient { get; }
public TChatSend(uint @epoch, uint @sequence, string @channel, string @text, string @recipient) {
this.@epoch = @epoch;
this.@sequence = @sequence;
this.@channel = @channel;
this.@text = @text;
this.@recipient = @recipient;
}
}
public sealed class TChatResult {
public bool @accepted { get; }
public TChatResult(bool @accepted) {
this.@accepted = @accepted;
}
}
public sealed class TTradeState {
public uint @sequence { get; }
public string @status { get; }
public uint @revision { get; }
public bool @accepted { get; }
public bool @peerAccepted { get; }
public uint @coins { get; }
public uint @offeredCoins { get; }
public uint @peerCoins { get; }
public string @peerName { get; }
public string @candidateName { get; }
public bool @canCreate { get; }
public string @notice { get; }
public IReadOnlyList<TTradeItem> @items { get; }
public IReadOnlyList<TTradePeerItem> @peerItems { get; }
public TTradeState(uint @sequence, string @status, uint @revision, bool @accepted, bool @peerAccepted, uint @coins, uint @offeredCoins, uint @peerCoins, string @peerName, string @candidateName, bool @canCreate, string @notice, IReadOnlyList<TTradeItem> @items, IReadOnlyList<TTradePeerItem> @peerItems) {
this.@sequence = @sequence;
this.@status = @status;
this.@revision = @revision;
this.@accepted = @accepted;
this.@peerAccepted = @peerAccepted;
this.@coins = @coins;
this.@offeredCoins = @offeredCoins;
this.@peerCoins = @peerCoins;
this.@peerName = @peerName;
this.@candidateName = @candidateName;
this.@canCreate = @canCreate;
this.@notice = @notice;
this.@items = @items;
this.@peerItems = @peerItems;
}
}
public sealed class TTradeItem {
public string @id { get; }
public string @kind { get; }
public uint @available { get; }
public uint @offered { get; }
public TTradeItem(string @id, string @kind, uint @available, uint @offered) {
this.@id = @id;
this.@kind = @kind;
this.@available = @available;
this.@offered = @offered;
}
}
public sealed class TTradePeerItem {
public string @id { get; }
public string @kind { get; }
public uint @quantity { get; }
public TTradePeerItem(string @id, string @kind, uint @quantity) {
this.@id = @id;
this.@kind = @kind;
this.@quantity = @quantity;
}
}
public sealed class TTradeIntent {
public uint @sequence { get; }
public uint @action { get; }
public uint @value { get; }
public TTradeIntent(uint @sequence, uint @action, uint @value) {
this.@sequence = @sequence;
this.@action = @action;
this.@value = @value;
}
}
public sealed class TTradeResult {
public bool @accepted { get; }
public TTradeResult(bool @accepted) {
this.@accepted = @accepted;
}
}
public interface IChatProvider {
TChatState GetState();
event Action<TChatState> StateChanged;
Task<TChatResult> Send(TChatSend request, CancellationToken cancellationToken);
}
public static class ChatContract {
public const string Name = "@mmo/chat", Version = "1.0.0", Fingerprint = "1957c95dcd1ea7c21cb96ce61691675dd16d0e463757868e8ea8081c3a44bee6";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] {  },
new string[] { "chat.read", "chat.send" });
public static ChatBinding Bind(IChatProvider provider) => new ChatBinding(provider);
}
public interface ITradeProvider {
TTradeState GetState();
event Action<TTradeState> StateChanged;
Task<TTradeResult> Interact(TTradeIntent request, CancellationToken cancellationToken);
}
public static class TradeContract {
public const string Name = "@mmo/player-trade", Version = "1.0.0", Fingerprint = "5c31db5f5e800d2a8be4f66179711c62280f139de67ddae6805d027eaba45b74";
public static readonly Capsule.UiFeatureDefinition Definition = new Capsule.UiFeatureDefinition(Name, Version, Fingerprint,
new Capsule.UiFeatureDependency[] {  },
new string[] { "trade.read", "trade.interact" });
public static TradeBinding Bind(ITradeProvider provider) => new TradeBinding(provider);
}
public sealed class ChatBinding : Capsule.UiFeatureProvider {
readonly IChatProvider provider;
public ChatBinding(IChatProvider provider) : base(ChatContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "chat.read", MmoFeatureCodec.Encode(provider.GetState()), Capsule.UiFeatureDelivery.Ordered);
provider.StateChanged += OnState;
AddRequest("Send", "chat.send", async (bytes, cancellation) => MmoFeatureCodec.Encode(await provider.Send(MmoFeatureCodec.DecodeChatSend(bytes), cancellation)));
}
void OnState(TChatState value) => Publish("State", MmoFeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public sealed class TradeBinding : Capsule.UiFeatureProvider {
readonly ITradeProvider provider;
public TradeBinding(ITradeProvider provider) : base(TradeContract.Definition) {
this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
AddState("State", "trade.read", MmoFeatureCodec.Encode(provider.GetState()));
provider.StateChanged += OnState;
AddRequest("Interact", "trade.interact", async (bytes, cancellation) => MmoFeatureCodec.Encode(await provider.Interact(MmoFeatureCodec.DecodeTradeIntent(bytes), cancellation)));
}
void OnState(TTradeState value) => Publish("State", MmoFeatureCodec.Encode(value));
public override void Dispose() {
provider.StateChanged -= OnState;
base.Dispose();
}
}
public static class MmoFeatureCodec {
public static byte[] Encode(TChatState value) { var w = new Wire(SizeChatState(value)); WriteChatState(w, value); return w.Bytes; }
public static TChatState DecodeChatState(byte[] bytes) { var r = new Wire(bytes); var value = ReadChatState(r); r.End(); return value; }
static int SizeChatState(TChatState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + 1 + 1 + 4 + Wire.TextSize(value.@error, 512) + 4 + Wire.ListSize(value.@messages, 4, item0 => SizeChatMessage(item0))); }
static void WriteChatState(Wire w, TChatState value) { w.UInt(value.@sequence);
w.UInt(value.@epoch);
w.Bool(value.@ready);
w.Bool(value.@pending);
w.UInt(value.@accepted);
w.Text(value.@error);
w.UInt(value.@wake);
w.UInt((uint)value.@messages.Count); foreach (var item0 in value.@messages) { WriteChatMessage(w, item0); } }
static TChatState ReadChatState(Wire r) => new TChatState(r.UInt(), r.UInt(), r.Bool(), r.Bool(), r.UInt(), r.Text(512), r.UInt(), r.List(4, () => ReadChatMessage(r)));
public static byte[] Encode(TChatMessage value) { var w = new Wire(SizeChatMessage(value)); WriteChatMessage(w, value); return w.Bytes; }
public static TChatMessage DecodeChatMessage(byte[] bytes) { var r = new Wire(bytes); var value = ReadChatMessage(r); r.End(); return value; }
static int SizeChatMessage(TChatMessage value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 24) + Wire.TextSize(value.@channel, 16) + Wire.TextSize(value.@sender, 160) + Wire.TextSize(value.@text, 960)); }
static void WriteChatMessage(Wire w, TChatMessage value) { w.Text(value.@id);
w.Text(value.@channel);
w.Text(value.@sender);
w.Text(value.@text); }
static TChatMessage ReadChatMessage(Wire r) => new TChatMessage(r.Text(24), r.Text(16), r.Text(160), r.Text(960));
public static byte[] Encode(TChatSend value) { var w = new Wire(SizeChatSend(value)); WriteChatSend(w, value); return w.Bytes; }
public static TChatSend DecodeChatSend(byte[] bytes) { var r = new Wire(bytes); var value = ReadChatSend(r); r.End(); return value; }
static int SizeChatSend(TChatSend value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + Wire.TextSize(value.@channel, 16) + Wire.TextSize(value.@text, 512) + Wire.TextSize(value.@recipient, 80)); }
static void WriteChatSend(Wire w, TChatSend value) { w.UInt(value.@epoch);
w.UInt(value.@sequence);
w.Text(value.@channel);
w.Text(value.@text);
w.Text(value.@recipient); }
static TChatSend ReadChatSend(Wire r) => new TChatSend(r.UInt(), r.UInt(), r.Text(16), r.Text(512), r.Text(80));
public static byte[] Encode(TChatResult value) { var w = new Wire(SizeChatResult(value)); WriteChatResult(w, value); return w.Bytes; }
public static TChatResult DecodeChatResult(byte[] bytes) { var r = new Wire(bytes); var value = ReadChatResult(r); r.End(); return value; }
static int SizeChatResult(TChatResult value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1); }
static void WriteChatResult(Wire w, TChatResult value) { w.Bool(value.@accepted); }
static TChatResult ReadChatResult(Wire r) => new TChatResult(r.Bool());
public static byte[] Encode(TTradeState value) { var w = new Wire(SizeTradeState(value)); WriteTradeState(w, value); return w.Bytes; }
public static TTradeState DecodeTradeState(byte[] bytes) { var r = new Wire(bytes); var value = ReadTradeState(r); r.End(); return value; }
static int SizeTradeState(TTradeState value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + Wire.TextSize(value.@status, 24) + 4 + 1 + 1 + 4 + 4 + 4 + Wire.TextSize(value.@peerName, 160) + Wire.TextSize(value.@candidateName, 160) + 1 + Wire.TextSize(value.@notice, 512) + Wire.ListSize(value.@items, 24, item0 => SizeTradeItem(item0)) + Wire.ListSize(value.@peerItems, 24, item0 => SizeTradePeerItem(item0))); }
static void WriteTradeState(Wire w, TTradeState value) { w.UInt(value.@sequence);
w.Text(value.@status);
w.UInt(value.@revision);
w.Bool(value.@accepted);
w.Bool(value.@peerAccepted);
w.UInt(value.@coins);
w.UInt(value.@offeredCoins);
w.UInt(value.@peerCoins);
w.Text(value.@peerName);
w.Text(value.@candidateName);
w.Bool(value.@canCreate);
w.Text(value.@notice);
w.UInt((uint)value.@items.Count); foreach (var item0 in value.@items) { WriteTradeItem(w, item0); }
w.UInt((uint)value.@peerItems.Count); foreach (var item0 in value.@peerItems) { WriteTradePeerItem(w, item0); } }
static TTradeState ReadTradeState(Wire r) => new TTradeState(r.UInt(), r.Text(24), r.UInt(), r.Bool(), r.Bool(), r.UInt(), r.UInt(), r.UInt(), r.Text(160), r.Text(160), r.Bool(), r.Text(512), r.List(24, () => ReadTradeItem(r)), r.List(24, () => ReadTradePeerItem(r)));
public static byte[] Encode(TTradeItem value) { var w = new Wire(SizeTradeItem(value)); WriteTradeItem(w, value); return w.Bytes; }
public static TTradeItem DecodeTradeItem(byte[] bytes) { var r = new Wire(bytes); var value = ReadTradeItem(r); r.End(); return value; }
static int SizeTradeItem(TTradeItem value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 24) + Wire.TextSize(value.@kind, 128) + 4 + 4); }
static void WriteTradeItem(Wire w, TTradeItem value) { w.Text(value.@id);
w.Text(value.@kind);
w.UInt(value.@available);
w.UInt(value.@offered); }
static TTradeItem ReadTradeItem(Wire r) => new TTradeItem(r.Text(24), r.Text(128), r.UInt(), r.UInt());
public static byte[] Encode(TTradePeerItem value) { var w = new Wire(SizeTradePeerItem(value)); WriteTradePeerItem(w, value); return w.Bytes; }
public static TTradePeerItem DecodeTradePeerItem(byte[] bytes) { var r = new Wire(bytes); var value = ReadTradePeerItem(r); r.End(); return value; }
static int SizeTradePeerItem(TTradePeerItem value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(Wire.TextSize(value.@id, 24) + Wire.TextSize(value.@kind, 128) + 4); }
static void WriteTradePeerItem(Wire w, TTradePeerItem value) { w.Text(value.@id);
w.Text(value.@kind);
w.UInt(value.@quantity); }
static TTradePeerItem ReadTradePeerItem(Wire r) => new TTradePeerItem(r.Text(24), r.Text(128), r.UInt());
public static byte[] Encode(TTradeIntent value) { var w = new Wire(SizeTradeIntent(value)); WriteTradeIntent(w, value); return w.Bytes; }
public static TTradeIntent DecodeTradeIntent(byte[] bytes) { var r = new Wire(bytes); var value = ReadTradeIntent(r); r.End(); return value; }
static int SizeTradeIntent(TTradeIntent value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(4 + 4 + 4); }
static void WriteTradeIntent(Wire w, TTradeIntent value) { w.UInt(value.@sequence);
w.UInt(value.@action);
w.UInt(value.@value); }
static TTradeIntent ReadTradeIntent(Wire r) => new TTradeIntent(r.UInt(), r.UInt(), r.UInt());
public static byte[] Encode(TTradeResult value) { var w = new Wire(SizeTradeResult(value)); WriteTradeResult(w, value); return w.Bytes; }
public static TTradeResult DecodeTradeResult(byte[] bytes) { var r = new Wire(bytes); var value = ReadTradeResult(r); r.End(); return value; }
static int SizeTradeResult(TTradeResult value) { if (value == null) throw new ArgumentNullException(nameof(value)); return checked(1); }
static void WriteTradeResult(Wire w, TTradeResult value) { w.Bool(value.@accepted); }
static TTradeResult ReadTradeResult(Wire r) => new TTradeResult(r.Bool());
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
