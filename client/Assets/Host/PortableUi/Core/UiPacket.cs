using System;
using System.Collections.Generic;
using System.Text;

namespace PortableUi
{
    public enum UiOpcode : byte
    {
        Create = 1, SetText = 2, SetImage = 3, SetProgress = 4,
        SetVisible = 5, Remove = 6, SetEvents = 7, SetRect = 8, Raise = 9, SetStyle = 10,
        CreateLayout = 11, SetLayout = 12, SetBrightness = 13, SetTextInput = 14, SetWorldAnchor = 15, SetEffect = 16
    }

    internal sealed class UiCommand
    {
        internal UiOpcode Opcode;
        internal ulong Handle, Parent;
        internal UiNodeKind Kind;
        internal UiRect Rect;
        internal string Style, Asset, Text;
        internal ushort Value;
        internal bool Visible;
        internal UiEventMask Events;
        internal UiLayout Layout;
        internal UiTextInputOptions TextInput;
        internal UiWorldAnchor WorldAnchor;
        internal UiPresentationEffect Effect;
    }

    // PUI1, little-endian, exact lengths; no guest-owned span is retained.
    internal static class UiPacket
    {
        internal const int MaxBytes = 65536;
        internal const int MaxCommands = 256;
        internal const ushort Version = 1;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static UiResult Parse(byte[] bytes, out List<UiCommand> commands)
        {
            commands = null;
            if (bytes == null || bytes.Length < 16 || bytes.Length > MaxBytes)
                return new UiResult(UiError.InvalidPacket, "Packet size invalid.");
            try
            {
                var reader = new Reader(bytes, 0, bytes.Length);
                if (reader.U32() != 0x31495550) return new UiResult(UiError.InvalidPacket, "Magic must be PUI1.");
                ushort version = reader.U16();
                if (version != Version && version != 2) return new UiResult(UiError.UnsupportedVersion);
                if (reader.U16() != 0) return new UiResult(UiError.InvalidPacket, "Reserved header must be zero.");
                int count = reader.U16();
                if (count > MaxCommands) return new UiResult(UiError.QuotaExceeded, "Too many commands.");
                if (reader.U16() != 0 || reader.U32() != bytes.Length)
                    return new UiResult(UiError.InvalidPacket, "Header length or reserved field invalid.");
                var parsed = new List<UiCommand>(count);
                for (int i = 0; i < count; i++)
                {
                    var opcode = (UiOpcode)reader.U8();
                    if (version == 1 && (opcode == UiOpcode.CreateLayout || opcode == UiOpcode.SetLayout || opcode == UiOpcode.SetBrightness))
                        return new UiResult(UiError.UnsupportedVersion, "Layout requires packet v2.");
                    if (reader.U8() != 0) return new UiResult(UiError.InvalidPacket, "Command flags must be zero.");
                    int length = reader.U16();
                    var body = reader.Body(length);
                    var command = new UiCommand { Opcode = opcode };
                    switch (opcode)
                    {
                        case UiOpcode.SetWorldAnchor:
                            command.Handle = body.U64();
                            byte anchorEnabled = body.U8();
                            if (anchorEnabled > 1) return new UiResult(UiError.InvalidCommand, "Anchor flag.");
                            if (anchorEnabled == 1) {
                                string entity = body.Key(), attachment = body.Key();
                                int x=body.I32(), y=body.I32(), z=body.I32(), near=body.I32(), far=body.I32(), reference=body.I32(), min=body.I32(), max=body.I32();
                                byte occlusion=body.U8();
                                if(occlusion>1) return new UiResult(UiError.InvalidCommand, "Occlusion flag.");
                                command.WorldAnchor=new UiWorldAnchor(entity,attachment,x,y,z,near,far,reference,min,max,occlusion==1);
                            }
                            break;
                        case UiOpcode.SetEffect:
                            command.Handle=body.U64();
                            byte effectEnabled=body.U8();
                            if(effectEnabled>1) return new UiResult(UiError.InvalidCommand,"Effect flag.");
                            if(effectEnabled==1) command.Effect=new UiPresentationEffect(body.U32(),body.I32(),body.I32(),body.I32(),body.I32());
                            break;
                        case UiOpcode.SetTextInput:
                            command.Handle = body.U64();
                            ushort maxLength = body.U16(), maxBytes = body.U16();
                            byte flags = body.U8();
                            if (flags > 3 || body.U8() != 0) return new UiResult(UiError.InvalidCommand, "Text input flags.");
                            command.TextInput = new UiTextInputOptions(maxLength, maxBytes, (flags & 1) != 0, (flags & 2) != 0, body.U32());
                            break;
                        case UiOpcode.Create:
                        case UiOpcode.CreateLayout:
                            command.Handle = body.U64(); command.Parent = body.U64();
                            command.Kind = (UiNodeKind)body.U8();
                            command.Rect = body.Rect();
                            command.Style = body.Key(); command.Asset = body.Key();
                            command.Text = body.Text(); command.Events = (UiEventMask)body.U16();
                            if (opcode == UiOpcode.CreateLayout) command.Layout = body.Layout();
                            break;
                        case UiOpcode.SetLayout:
                            command.Handle = body.U64(); command.Layout = body.Layout(); break;
                        case UiOpcode.SetText:
                            command.Handle = body.U64(); command.Text = body.Text(); break;
                        case UiOpcode.SetStyle:
                            command.Handle = body.U64(); command.Style = body.Key(); break;
                        case UiOpcode.SetImage:
                            command.Handle = body.U64(); command.Asset = body.Key(); break;
                        case UiOpcode.SetProgress:
                        case UiOpcode.SetBrightness:
                            command.Handle = body.U64(); command.Value = body.U16(); break;
                        case UiOpcode.SetVisible:
                            command.Handle = body.U64();
                            byte visible = body.U8();
                            if (visible > 1) return new UiResult(UiError.InvalidCommand, "Visibility must be 0 or 1.");
                            command.Visible = visible == 1; break;
                        case UiOpcode.Remove:
                        case UiOpcode.Raise:
                            command.Handle = body.U64(); break;
                        case UiOpcode.SetEvents:
                            command.Handle = body.U64(); command.Events = (UiEventMask)body.U16(); break;
                        case UiOpcode.SetRect:
                            command.Handle = body.U64(); command.Rect = body.Rect(); break;
                        default: return new UiResult(UiError.InvalidCommand, "Unknown opcode.");
                    }
                    if (!body.AtEnd) return new UiResult(UiError.InvalidPacket, "Command body has trailing bytes.");
                    parsed.Add(command);
                }
                if (!reader.AtEnd) return new UiResult(UiError.InvalidPacket, "Packet has trailing bytes.");
                commands = parsed;
                return UiResult.Ok;
            }
            catch (DecoderFallbackException)
            {
                return new UiResult(UiError.InvalidPacket, "Invalid UTF-8.");
            }
            catch (ArgumentException error)
            {
                return new UiResult(UiError.InvalidPacket, error.Message);
            }
        }

        sealed class Reader
        {
            readonly byte[] bytes;
            readonly int end;
            int offset;
            internal bool AtEnd => offset == end;
            internal Reader(byte[] bytes, int start, int end)
            { this.bytes = bytes; offset = start; this.end = end; }
            void Need(int size)
            {
                if (size < 0 || size > end - offset) throw new ArgumentException("Truncated packet.");
            }
            internal byte U8() { Need(1); return bytes[offset++]; }
            internal ushort U16() { Need(2); int p = offset; offset += 2; return (ushort)(bytes[p] | bytes[p + 1] << 8); }
            internal uint U32()
            {
                Need(4); int p = offset; offset += 4;
                return (uint)(bytes[p] | bytes[p + 1] << 8 | bytes[p + 2] << 16 | bytes[p + 3] << 24);
            }
            internal ulong U64() { ulong lo = U32(); return lo | (ulong)U32() << 32; }
            internal int I32() => unchecked((int)U32());
            internal UiRect Rect() => new UiRect(I32(), I32(), I32(), I32());
            internal UiLayout Layout() => new UiLayout(U8(), U8(), U8(), U8(),
                I32(), I32(), I32(), I32(), I32(), I32(), I32(), I32(), I32(), I32(), I32());
            internal Reader Body(int length)
            {
                Need(length); var body = new Reader(bytes, offset, offset + length); offset += length;
                return body;
            }
            string String(int count, bool key)
            {
                Need(count);
                var value = Utf8.GetString(bytes, offset, count);
                offset += count;
                if (value.IndexOf('\0') >= 0) throw new ArgumentException("NUL string rejected.");
                if (key)
                {
                    foreach (char c in value)
                        if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                            !(c >= '0' && c <= '9') && c != '-' && c != '_' && c != '.')
                            throw new ArgumentException("Theme key has invalid characters.");
                }
                return value;
            }
            internal string Key() { int count = U8(); if (count > 96) throw new ArgumentException("Theme key too long."); return String(count, true); }
            internal string Text() { int count = U16(); if (count > 512) throw new ArgumentException("Text too long."); return String(count, false); }
        }
    }
}
