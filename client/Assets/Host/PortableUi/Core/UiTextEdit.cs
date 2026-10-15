using System;
using System.Text;

namespace PortableUi
{
    public sealed class UiTextInputOptions
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static readonly char[] Controls = { '\0', '\r', '\n', '\t' };
        public static readonly UiTextInputOptions Default = new UiTextInputOptions(512, 512, false, false, 0);
        public readonly ushort MaxLength, MaxBytes;
        public readonly bool ReadOnly, Numeric;
        public readonly uint Revision;
        public UiTextInputOptions(ushort maxLength, ushort maxBytes, bool readOnly, bool numeric, uint revision)
        { MaxLength = maxLength; MaxBytes = maxBytes; ReadOnly = readOnly; Numeric = numeric; Revision = revision; }
        public bool Valid => MaxLength >= 1 && MaxLength <= 512 && MaxBytes >= 1 && MaxBytes <= 512;
        public bool Accepts(string text)
        {
            if (!Valid || text == null || text.Length > MaxLength || text.IndexOfAny(Controls) >= 0) return false;
            if (Numeric) foreach (char c in text) if (c < '0' || c > '9') return false;
            try { return Utf8.GetByteCount(text) <= MaxBytes; }
            catch (EncoderFallbackException) { return false; }
        }
    }
    public enum UiTextEditPhase : byte { Change = 1, Composition = 2, Submit = 3, Cancel = 4, Focus = 5, Blur = 6 }

    // Selection offsets are UTF-16 boundaries, matching TMP and browser editors.
    // Composition is provisional and never a submit. Native editors own the draft.
    public readonly struct UiTextEdit
    {
        public const int MaxTextBytes = 512, MaxPayloadBytes = 12 + 2 * MaxTextBytes;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public readonly UiTextEditPhase Phase;
        public readonly string Text, Composition;
        public readonly ushort Anchor, Focus;
        public readonly bool Composing, UserInput;
        public UiTextEdit(UiTextEditPhase phase, string text, string composition, ushort anchor, ushort focus, bool composing, bool userInput)
        { Phase = phase; Text = text; Composition = composition; Anchor = anchor; Focus = focus; Composing = composing; UserInput = userInput; }

        static bool Boundary(string text, int at) => at <= text.Length &&
            !(at > 0 && at < text.Length && char.IsHighSurrogate(text[at - 1]) && char.IsLowSurrogate(text[at]));
        bool Valid() => Phase >= UiTextEditPhase.Change && Phase <= UiTextEditPhase.Blur && Text != null && Composition != null &&
            Text.IndexOf('\0') < 0 && Composition.IndexOf('\0') < 0 && Boundary(Text, Anchor) && Boundary(Text, Focus) &&
            (Composing || Composition.Length == 0) &&
            !(Composing && (Phase == UiTextEditPhase.Submit || Phase == UiTextEditPhase.Cancel || Phase == UiTextEditPhase.Blur));
        public byte[] Encode()
        {
            if (!Valid()) throw new ArgumentException("Invalid text edit.");
            var text = Utf8.GetBytes(Text); var composition = Utf8.GetBytes(Composition);
            if (text.Length > MaxTextBytes || composition.Length > MaxTextBytes) throw new ArgumentException("Text byte quota.");
            var bytes = new byte[12 + text.Length + composition.Length];
            bytes[0] = 1; bytes[1] = (byte)Phase; bytes[2] = (byte)((Composing ? 1 : 0) | (UserInput ? 2 : 0));
            Write(bytes, 4, Anchor); Write(bytes, 6, Focus); Write(bytes, 8, text.Length); Write(bytes, 10, composition.Length);
            Buffer.BlockCopy(text, 0, bytes, 12, text.Length); Buffer.BlockCopy(composition, 0, bytes, 12 + text.Length, composition.Length);
            return bytes;
        }
        public static bool TryDecode(byte[] bytes, out UiTextEdit value)
        {
            value = default;
            if (bytes == null || bytes.Length < 12 || bytes.Length > MaxPayloadBytes || bytes[0] != 1 || bytes[2] > 3 || bytes[3] != 0) return false;
            int text = Read(bytes, 8), composition = Read(bytes, 10);
            if (text > MaxTextBytes || composition > MaxTextBytes || bytes.Length != 12 + text + composition) return false;
            try
            {
                value = new UiTextEdit((UiTextEditPhase)bytes[1], Utf8.GetString(bytes, 12, text), Utf8.GetString(bytes, 12 + text, composition),
                    (ushort)Read(bytes, 4), (ushort)Read(bytes, 6), (bytes[2] & 1) != 0, (bytes[2] & 2) != 0);
                return value.Valid();
            }
            catch (DecoderFallbackException) { return false; }
        }
        static int Read(byte[] bytes, int at) => bytes[at] | bytes[at + 1] << 8;
        static void Write(byte[] bytes, int at, int value) { bytes[at] = (byte)value; bytes[at + 1] = (byte)(value >> 8); }
    }
}
