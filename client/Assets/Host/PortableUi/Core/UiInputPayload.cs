using System;

namespace PortableUi
{
    public enum UiPointerPhase : byte { Down = 1, Move = 2, Up = 3, Cancel = 4 }
    public enum UiInputKey : byte { Escape = 1, Tab = 2, Enter = 3, Space = 4, Left = 5, Right = 6, Up = 7, Down = 8, E = 9, I = 10, C = 11, Home = 12, End = 13, PageUp = 14, PageDown = 15, J = 16, K = 17 }

    // Explicit version-one host input records. Coordinates use the same 1/64
    // design-unit representation as layout. No platform key names cross the ABI.
    public static class UiInputPayload
    {
        public const ushort AllowedEvents = 1023;
        public const byte Version = 1;
        public const int PointerLength = 16;

        public static byte[] Pointer(UiPointerPhase phase, int pointerId, int x, int y,
            byte button = 0, byte modifiers = 0)
        {
            var bytes = new byte[PointerLength];
            bytes[0] = Version; bytes[1] = (byte)phase;
            bytes[2] = button; bytes[3] = modifiers;
            Write(bytes, 4, pointerId); Write(bytes, 8, x); Write(bytes, 12, y);
            if (!Valid(UiEventMask.Pointer, bytes)) throw new ArgumentOutOfRangeException(nameof(phase));
            return bytes;
        }

        public static byte[] Key(UiInputKey key, bool down, byte modifiers = 0, bool repeat = false)
        {
            var bytes = new[] { Version, (byte)key, (byte)(down ? 1 : 0), modifiers, (byte)(repeat ? 1 : 0) };
            if (!Valid(UiEventMask.Key, bytes)) throw new ArgumentOutOfRangeException(nameof(key));
            return bytes;
        }

        public static byte[] Value(ushort normalized)
        {
            if (normalized > 10000) throw new ArgumentOutOfRangeException(nameof(normalized));
            return new[] { Version, (byte)normalized, (byte)(normalized >> 8) };
        }
        public static byte[] Boundary(bool entered) => new[] { Version, (byte)(entered ? 1 : 0) };

        public static byte[] Wheel(int deltaX, int deltaY, byte modifiers = 0)
        {
            var bytes = new byte[12];
            bytes[0] = Version; bytes[1] = modifiers;
            Write(bytes, 4, deltaX); Write(bytes, 8, deltaY);
            if (!Valid(UiEventMask.Wheel, bytes)) throw new ArgumentOutOfRangeException(nameof(deltaY));
            return bytes;
        }

        public static bool Valid(UiEventMask kind, byte[] bytes)
        {
            if (bytes == null || bytes.Length > UiSession.MaxEventPayloadBytes) return false;
            if (kind == UiEventMask.Wheel)
                return bytes.Length == 12 && bytes[0] == Version && bytes[1] <= 15 && bytes[2] == 0 && bytes[3] == 0 &&
                    Read(bytes, 4) >= -8192 * 64 && Read(bytes, 4) <= 8192 * 64 &&
                    Read(bytes, 8) >= -8192 * 64 && Read(bytes, 8) <= 8192 * 64;
            if (kind == UiEventMask.Pointer)
                return bytes.Length == PointerLength && bytes[0] == Version &&
                    bytes[1] >= 1 && bytes[1] <= 4 && bytes[2] <= 2 && bytes[3] <= 15 &&
                    Read(bytes, 8) >= -8192 * 64 && Read(bytes, 8) <= 8192 * 64 &&
                    Read(bytes, 12) >= -8192 * 64 && Read(bytes, 12) <= 8192 * 64;
            if (kind == UiEventMask.Key)
                return bytes.Length == 5 && bytes[0] == Version && bytes[1] >= 1 && bytes[1] <= 17 &&
                    bytes[2] <= 1 && bytes[3] <= 15 && bytes[4] <= 1;
            if (kind == UiEventMask.ValueChange)
                return bytes.Length == 3 && bytes[0] == Version && (bytes[1] | bytes[2] << 8) <= 10000;
            if (kind == UiEventMask.Hover || kind == UiEventMask.Focus)
                return bytes.Length == 0 || bytes.Length == 2 && bytes[0] == Version && bytes[1] <= 1;
            // Legacy addon event payloads retain their established opaque contract.
            return true;
        }

        public static bool SupersedesMove(byte[] previous, byte[] next) =>
            Valid(UiEventMask.Pointer, previous) && Valid(UiEventMask.Pointer, next) &&
            previous[1] == (byte)UiPointerPhase.Move && next[1] == (byte)UiPointerPhase.Move &&
            Read(previous, 4) == Read(next, 4) && previous[2] == next[2] && previous[3] == next[3];

        static int Read(byte[] bytes, int offset) => bytes[offset] | bytes[offset + 1] << 8 |
            bytes[offset + 2] << 16 | bytes[offset + 3] << 24;
        static void Write(byte[] bytes, int offset, int value)
        { for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)(value >> (i * 8)); }
    }
}
