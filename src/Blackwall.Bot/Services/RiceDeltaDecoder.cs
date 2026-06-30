using System.Numerics;
using System.Text.Json.Serialization;
// ReSharper disable UnusedAutoPropertyAccessor.Global

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
        var data = Convert.FromBase64String(encoded.EncodedData ?? "");

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
        var data = Convert.FromBase64String(encoded.EncodedData ?? "");

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
    private static BigInteger BuildBigInteger256(string? first, string? second, string? third, string? fourth) {
        var a = ParseUInt64(first);
        var b = ParseUInt64(second);
        var c = ParseUInt64(third);
        var d = ParseUInt64(fourth);

        return (new BigInteger(a) << 192)
             | (new BigInteger(b) << 128)
             | (new BigInteger(c) << 64)
             | new BigInteger(d);
    }

    /// <summary>
    /// Parses a string as a 64-bit unsigned integer, returning zero for null or empty input.
    /// </summary>
    private static ulong ParseUInt64(string? s) =>
        string.IsNullOrEmpty(s) ? 0UL : ulong.Parse(s);

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
    /// Reads individual bits from a byte array in MSB-first (big-endian) order.
    /// </summary>
    private sealed class BitReader(byte[] data) {
        private int _bytePos;
        private int _bitPos;

        /// <summary>
        /// Reads a unary-coded value by counting zero bits until a one bit is encountered.
        /// </summary>
        public int ReadUnary() {
            var count = 0;
            while (!ReadBit())
                count++;
            return count;
        }

        /// <summary>
        /// Reads the specified number of bits and returns them as a 32-bit unsigned integer.
        /// </summary>
        public uint ReadBits(int count) {
            uint result = 0;
            for (var i = 0; i < count; i++) {
                result = (result << 1) | (ReadBit() ? 1u : 0u);
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
                result = (result << 1) | (ReadBit() ? BigInteger.One : BigInteger.Zero);
            }
            return result;
        }

        /// <summary>
        /// Reads a single bit from the underlying byte array in MSB-first order,
        /// returning false when the data is exhausted.
        /// </summary>
        private bool ReadBit() {
            if (_bytePos >= data.Length)
                return false;

            var bit = (data[_bytePos] & (0x80 >> _bitPos)) != 0;
            _bitPos++;
            if (_bitPos != 8) return bit;
            _bitPos = 0;
            _bytePos++;
            return bit;
        }
    }
}

public sealed class RiceDeltaEncoded32Bit {
    [JsonPropertyName("firstValue")]
    public uint FirstValue { get; set; }

    [JsonPropertyName("riceParameter")]
    public int RiceParameter { get; set; }

    [JsonPropertyName("entriesCount")]
    public int EntriesCount { get; set; }

    [JsonPropertyName("encodedData")]
    public string? EncodedData { get; set; }
}

public sealed class RiceDeltaEncoded256Bit {
    [JsonPropertyName("firstValueFirstPart")]
    public string? FirstValueFirstPart { get; set; }

    [JsonPropertyName("firstValueSecondPart")]
    public string? FirstValueSecondPart { get; set; }

    [JsonPropertyName("firstValueThirdPart")]
    public string? FirstValueThirdPart { get; set; }

    [JsonPropertyName("firstValueFourthPart")]
    public string? FirstValueFourthPart { get; set; }

    [JsonPropertyName("riceParameter")]
    public int RiceParameter { get; set; }

    [JsonPropertyName("entriesCount")]
    public int EntriesCount { get; set; }

    [JsonPropertyName("encodedData")]
    public string? EncodedData { get; set; }
}
