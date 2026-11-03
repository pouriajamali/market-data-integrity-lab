using System.Security.Cryptography;

namespace MarketDataIntegrity;

// Data is borrowed and valid only during the synchronous visitor call.
public readonly record struct RawRecord(long Line, long Offset, long ByteLength,
    string Sha256, ReadOnlyMemory<byte> Data, bool TooLong);

public static class BoundedReader
{
    public const int ReadBufferBytes = 8192;

    public static void Visit(Stream input, int maxRecordBytes, Action<RawRecord> visitor)
    {
        if (maxRecordBytes is < 64 or > 1_048_576) throw new ArgumentOutOfRangeException(nameof(maxRecordBytes));
        ArgumentNullException.ThrowIfNull(visitor);
        var readBuffer = new byte[ReadBufferBytes];
        var recordBuffer = new byte[maxRecordBytes];
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long line = 1, offset = 0, length = 0;
        int retained = 0;
        while (true)
        {
            var read = input.Read(readBuffer);
            if (read == 0) break;
            var start = 0;
            while (start < read)
            {
                var relativeNewline = readBuffer.AsSpan(start, read - start).IndexOf((byte)10);
                var segmentLength = relativeNewline < 0 ? read - start : relativeNewline;
                var segment = readBuffer.AsSpan(start, segmentLength);
                hasher.AppendData(segment);
                length = checked(length + segmentLength);
                var copy = Math.Min(segmentLength, maxRecordBytes - retained);
                segment[..copy].CopyTo(recordBuffer.AsSpan(retained));
                retained += copy;
                start += segmentLength;
                if (relativeNewline < 0) break;
                Emit();
                start++; // LF only; CR is part of raw bytes.
                offset = checked(offset + length + 1);
                length = 0;
                retained = 0;
                line = checked(line + 1);
            }
        }
        if (length > 0) Emit();

        void Emit()
        {
            var hash = Convert.ToHexStringLower(hasher.GetHashAndReset());
            visitor(new RawRecord(line, offset, length, hash,
                recordBuffer.AsMemory(0, retained), length > maxRecordBytes));
        }
    }
}
