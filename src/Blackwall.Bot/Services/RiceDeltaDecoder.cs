using System.Numerics;
using Blackwall.Bot.Services.SafeBrowsingProto;

namespace Blackwall.Bot.Services;

/// <summary>
/// Decodes Rice-Golomb delta-encoded data used by the Google Safe Browsing V5
/// hashLists:batchGet API. Supports 32-bit (threat list prefixes) and 256-bit
/// (Global Cache full hashes) variants. All values are big-endian.
/// </summary>
public static class RiceDeltaDecoder {
    /// <summary>
    /// Decodes a 32-bit Rice-Golomb encoded list into a sorted list of uint values.
    /// Used for 4-byte threat list hash prefixes.
    /// </summary>
    public static List<uint> Decode32Bit(RiceDeltaEncoded32Bit encoded) {
        var firstValue = encoded.FirstValue;
        var k = encoded.RiceParameter;
        var count = encoded.EntriesCount;
        var data = encoded.EncodedData.ToByteArray();

        var result = new List<uint>(count + 1) { firstValue };

        if (count == 0)
            return result;

        var reader = new BitReader(data);
        var prev = firstValue;

        for (var i = 0; i < count; i++) {
            var quotient = reader.ReadUnary();
            var remainder = reader.ReadBits(k);
            var delta = (uint)(quotient * (1u << k) + remainder);
            prev += delta;
            result.Add(prev);
        }

        return result;
    }

    /// <summary>
    /// Decodes a 256-bit Rice-Golomb encoded list into a sorted list of 32-byte arrays.
    /// Used for Global Cache SHA256 hashes.
    /// </summary>
    public static List<byte[]> Decode256Bit(RiceDeltaEncoded256Bit encoded) {
        var firstValue = BuildBigInteger256(
            encoded.FirstValueFirstPart,
            encoded.FirstValueSecondPart,
            encoded.FirstValueThirdPart,
            encoded.FirstValueFourthPart
        );

        var k = encoded.RiceParameter;
        var count = encoded.EntriesCount;
        var data = encoded.EncodedData.ToByteArray();

        var result = new List<byte[]>(count + 1) { ToBigEndianBytes(firstValue, 32) };

        if (count == 0)
            return result;

        var reader = new BitReader(data);
        var prev = firstValue;
        var powerOfK = BigInteger.One << k;

        for (var i = 0; i < count; i++) {
            var quotient = reader.ReadUnary();
            var remainder = reader.ReadBigBits(k);
            var delta = quotient * powerOfK + remainder;
            prev += delta;
            result.Add(ToBigEndianBytes(prev, 32));
        }

        return result;
    }

    /// <summary>
    /// Combines four 64-bit string parts into a single 256-bit BigInteger,
    /// with the first part occupying the most significant position.
    /// </summary>
    private static BigInteger BuildBigInteger256(ulong first, ulong second, ulong third, ulong fourth) {
        return (new BigInteger(first) << 192)
             | (new BigInteger(second) << 128)
             | (new BigInteger(third) << 64)
             | new BigInteger(fourth);
    }

    /// <summary>
    /// Converts a BigInteger to a fixed-length big-endian byte array,
    /// padding with leading zeros or trimming overflow as needed.
    /// </summary>
    private static byte[] ToBigEndianBytes(BigInteger value, int length) {
        var bytes = value.ToByteArray(isUnsigned: true);

        if (bytes.Length == length)
            Array.Reverse(bytes);
        else if (bytes.Length < length) {
            var padded = new byte[length];
            Buffer.BlockCopy(bytes, 0, padded, length - bytes.Length, bytes.Length);
            Array.Reverse(padded);
            return padded;
        } else {
            var trimmed = new byte[length];
            Array.Copy(bytes, bytes.Length - length, trimmed, 0, length);
            Array.Reverse(trimmed);
            return trimmed;
        }

        return bytes;
    }

    /// <summary>
    /// Reads individual bits from a byte array in LSB-first (little-endian) order,
    /// as Rice-Golomb encoded data is packed from the least significant bit.
    /// </summary>
    private sealed class BitReader(byte[] data) {
        private int _bytePos;
        private int _bitPos;

        /// <summary>
        /// Reads a unary-coded value by counting one bits until a zero bit is encountered.
        /// </summary>
        public int ReadUnary() {
            var count = 0;
            while (ReadBit())
                count++;
            return count;
        }

        /// <summary>
        /// Reads the specified number of bits and returns them as a 32-bit unsigned integer.
        /// </summary>
        public uint ReadBits(int count) {
            uint result = 0;
            for (var i = 0; i < count; i++) {
                result |= (ReadBit() ? 1u : 0u) << i;
            }
            return result;
        }

        /// <summary>
        /// Reads the specified number of bits and returns them as a BigInteger,
        /// supporting widths greater than 32 bits.
        /// </summary>
        public BigInteger ReadBigBits(int count) {
            var result = BigInteger.Zero;
            for (var i = 0; i < count; i++) {
                if (ReadBit())
                    result |= BigInteger.One << i;
            }
            return result;
        }

        /// <summary>
        /// Reads a single bit from the underlying byte array in LSB-first order,
        /// as Rice-Golomb encoded data is packed little-endian within each byte.
        /// Throws when the data is exhausted to prevent infinite loops in callers.
        /// </summary>
        private bool ReadBit() {
            if (_bytePos >= data.Length)
                throw new EndOfStreamException("Rice-Golomb encoded data exhausted before all entries were decoded");

            var bit = (data[_bytePos] & (1 << _bitPos)) != 0;
            _bitPos++;
            if (_bitPos != 8) return bit;
            _bitPos = 0;
            _bytePos++;
            return bit;
        }
    }
}
