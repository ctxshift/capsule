using System;
using System.Security.Cryptography;

namespace PortableUi.Wacs
{
    // The expected digest belongs to the host build, never to an addon manifest.
    // The copy keeps a package writer from changing bytes between approval and parse.
    public sealed class TrustedWasmCarrier
    {
        readonly byte[] source;

        TrustedWasmCarrier(byte[] copiedSource) { source = copiedSource; }

        public static bool TryCreate(byte[] candidate, string hostPinnedSha256,
            out TrustedWasmCarrier carrier, out string failure)
        {
            carrier = null;
            failure = null;
            if (candidate == null || candidate.Length < 8 ||
                candidate.Length > WacsModulePolicy.MaxModuleBytes)
            { failure = "Carrier must be 8 bytes through 1 MiB."; return false; }
            if (hostPinnedSha256 == null || hostPinnedSha256.Length != 64)
            { failure = "Host carrier digest must contain 64 hex characters."; return false; }
            var copy = (byte[])candidate.Clone();
            byte[] digest;
            using (var sha = SHA256.Create()) digest = sha.ComputeHash(copy);
            int difference = 0;
            for (int i = 0; i < digest.Length; ++i)
            {
                int high = Hex(hostPinnedSha256[i * 2]);
                int low = Hex(hostPinnedSha256[i * 2 + 1]);
                if (high < 0 || low < 0)
                { failure = "Host carrier digest contains non-hex characters."; return false; }
                difference |= digest[i] ^ (high << 4 | low);
            }
            if (difference != 0)
            { failure = "Wasm carrier does not match the host-pinned SHA-256."; return false; }
            carrier = new TrustedWasmCarrier(copy);
            return true;
        }

        internal byte[] CopyApprovedBytes() => (byte[])source.Clone();

        static int Hex(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            return -1;
        }
    }
}
