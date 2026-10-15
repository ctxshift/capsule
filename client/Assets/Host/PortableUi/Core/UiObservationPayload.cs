using System;
using System.Runtime.InteropServices;

namespace PortableUi
{
    // Host-owned immutable segments. Neither array may be mutated after construction.
    // Keep the shared body alive until the guest has consumed the observation.
    public sealed class UiObservationPayload
    {
        readonly byte[] header, body;
        byte[] contiguous;
        public int Length { get; }
        public bool IsMaterialized => contiguous != null;
        public UiObservationPayload(byte[] header, byte[] body)
        {
            this.header = header ?? throw new ArgumentNullException(nameof(header));
            this.body = body ?? throw new ArgumentNullException(nameof(body));
            Length = checked(header.Length + body.Length);
        }
        public static UiObservationPayload Owned(byte[] bytes) => new UiObservationPayload(Array.Empty<byte>(), bytes);
        // Compatibility path. Returned storage is borrowed and must remain immutable.
        public byte[] AsContiguous()
        {
            if (header.Length == 0) return body;
            if (contiguous == null)
            {
                contiguous = new byte[Length];
                Buffer.BlockCopy(header, 0, contiguous, 0, header.Length);
                Buffer.BlockCopy(body, 0, contiguous, header.Length, body.Length);
            }
            return contiguous;
        }
        // Caller validates the destination range before entering the memory boundary.
        public void WriteTo(IntPtr destination)
        {
            if (header.Length > 0) Marshal.Copy(header, 0, destination, header.Length);
            if (body.Length > 0) Marshal.Copy(body, 0, IntPtr.Add(destination, header.Length), body.Length);
        }
    }
    public interface IUiSegmentedObservationAdapter : IUiObservationAdapter
    {
        bool TryDequeueSegments(out ushort topic, out ushort schemaVersion, out UiObservationPayload payload);
    }
}
