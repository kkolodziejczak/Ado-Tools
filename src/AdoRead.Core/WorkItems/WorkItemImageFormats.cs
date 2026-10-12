using System.Buffers.Binary;

namespace AdoRead.WorkItems;

internal sealed record SupportedImageFormat(string Extension, string ContentType)
{
    public static SupportedImageFormat? FromFileName(string fileName)
    {
        if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return new("png", "image/png");
        }

        if (fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return new("jpg", "image/jpeg");
        }

        return fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            ? new("webp", "image/webp")
            : null;
    }

    public static SupportedImageFormat? Detect(ReadOnlySpan<byte> signature)
    {
        using var stream = new MemoryStream(signature.ToArray(), writable: false);
        return Detect(stream);
    }

    public static SupportedImageFormat? Detect(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return null;
        }

        long originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            Span<byte> header = stackalloc byte[64];
            int headerLength = ReadAtMost(stream, header);
            ReadOnlySpan<byte> availableHeader = header[..headerLength];
            long fileLength = stream.Length;

            if (IsStructurallyValidPng(stream, availableHeader, fileLength))
            {
                return new("png", "image/png");
            }

            if (IsStructurallyValidWebp(stream, fileLength))
            {
                return new("webp", "image/webp");
            }

            if (availableHeader.Length >= 2
                && availableHeader[0] == 0xFF
                && availableHeader[1] == 0xD8
                && IsStructurallyValidJpeg(stream))
            {
                return new("jpg", "image/jpeg");
            }

            return null;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static bool IsStructurallyValidPng(
        Stream stream,
        ReadOnlySpan<byte> data,
        long fileLength)
    {
        if (data.Length < 33
            || !data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || BinaryPrimitives.ReadUInt32BigEndian(data[8..12]) != 13
            || !data[12..16].SequenceEqual("IHDR"u8)
            || BinaryPrimitives.ReadUInt32BigEndian(data[16..20]) == 0
            || BinaryPrimitives.ReadUInt32BigEndian(data[20..24]) == 0
            || !IsValidPngColorFormat(data[24], data[25])
            || data[26] != 0
            || data[27] != 0
            || data[28] > 1
            || Crc32(data[12..29]) != BinaryPrimitives.ReadUInt32BigEndian(data[29..33]))
        {
            return false;
        }

        stream.Position = 33;
        Span<byte> chunkHeader = stackalloc byte[8];
        bool hasImageData = false;
        while (stream.Position < fileLength)
        {
            long chunkStart = stream.Position;
            if (fileLength - chunkStart < 12 || !TryReadExactly(stream, chunkHeader))
            {
                return false;
            }

            long chunkSize = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader[..4]);
            long chunkEnd = checked(chunkStart + 12 + chunkSize);
            if (chunkEnd > fileLength)
            {
                return false;
            }

            if (chunkHeader[4..8].SequenceEqual("IDAT"u8) && chunkSize > 0)
            {
                hasImageData = true;
            }

            stream.Position = chunkEnd;
            if (chunkHeader[4..8].SequenceEqual("IEND"u8))
            {
                return chunkSize == 0 && hasImageData;
            }
        }

        return false;
    }

    private static bool IsStructurallyValidWebp(Stream stream, long fileLength)
    {
        stream.Position = 0;
        Span<byte> riffHeader = stackalloc byte[20];
        if (!TryReadExactly(stream, riffHeader)
            || !riffHeader[..4].SequenceEqual("RIFF"u8)
            || !riffHeader[8..12].SequenceEqual("WEBP"u8))
        {
            return false;
        }

        long riffSize = BinaryPrimitives.ReadUInt32LittleEndian(riffHeader[4..8]);
        long containerLength = checked(8 + riffSize);
        if (riffSize < 12 || containerLength > fileLength || containerLength < 20)
        {
            return false;
        }

        long chunkPosition = 12;
        bool hasImagePayload = false;
        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> vp8xHeader = stackalloc byte[10];
        Span<byte> vp8Header = stackalloc byte[10];
        Span<byte> vp8lHeader = stackalloc byte[5];
        while (chunkPosition < containerLength)
        {
            if (containerLength - chunkPosition < 8)
            {
                return false;
            }

            stream.Position = chunkPosition;
            if (!TryReadExactly(stream, chunkHeader))
            {
                return false;
            }

            long chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..8]);
            long chunkEnd = checked(chunkPosition + 8 + chunkSize);
            long paddedChunkEnd = checked(chunkEnd + (chunkSize & 1));
            if (paddedChunkEnd > containerLength || paddedChunkEnd > fileLength)
            {
                return false;
            }

            ReadOnlySpan<byte> chunkType = chunkHeader[..4];
            if (chunkType.SequenceEqual("VP8X"u8))
            {
                if (chunkSize != 10
                    || !TryReadAt(stream, chunkPosition + 8, vp8xHeader)
                    || vp8xHeader[1] != 0
                    || vp8xHeader[2] != 0
                    || vp8xHeader[3] != 0)
                {
                    return false;
                }
            }
            else if (chunkType.SequenceEqual("VP8L"u8))
            {
                if (chunkSize < 5
                    || !TryReadAt(stream, chunkPosition + 8, vp8lHeader)
                    || vp8lHeader[0] != 0x2F)
                {
                    return false;
                }

                uint dimensions = BinaryPrimitives.ReadUInt32LittleEndian(vp8lHeader[1..5]);
                if ((dimensions & 0x3FFF) + 1 <= 0
                    || ((dimensions >> 14) & 0x3FFF) + 1 <= 0)
                {
                    return false;
                }

                hasImagePayload = true;
            }
            else if (chunkType.SequenceEqual("VP8 "u8))
            {
                if (chunkSize < 10 || !TryReadAt(stream, chunkPosition + 8, vp8Header))
                {
                    return false;
                }

                if (vp8Header[3] != 0x9D
                    || vp8Header[4] != 0x01
                    || vp8Header[5] != 0x2A
                    || BinaryPrimitives.ReadUInt16LittleEndian(vp8Header[6..8]) == 0
                    || BinaryPrimitives.ReadUInt16LittleEndian(vp8Header[8..10]) == 0)
                {
                    return false;
                }

                hasImagePayload = true;
            }

            chunkPosition = paddedChunkEnd;
        }

        return chunkPosition == containerLength && hasImagePayload;
    }

    private static bool IsValidPngColorFormat(byte bitDepth, byte colorType) =>
        colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            4 => bitDepth is 8 or 16,
            6 => bitDepth is 8 or 16,
            _ => false
        };

    private static bool IsStructurallyValidJpeg(Stream stream)
    {
        stream.Position = 2;
        while (TryReadMarker(stream, out byte marker))
        {
            if (marker is 0x00 or 0xD9 or 0xDA)
            {
                return false;
            }

            if (IsStartOfFrame(marker))
            {
                return TryReadAndValidateFrame(stream);
            }

            if (marker == 0xD8 || marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (!TryReadSegmentLength(stream, out int segmentLength)
                || !TrySkip(stream, segmentLength - 2))
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryReadAndValidateFrame(Stream stream)
    {
        if (!TryReadSegmentLength(stream, out int segmentLength) || segmentLength < 8)
        {
            return false;
        }

        Span<byte> frameHeader = stackalloc byte[6];
        if (!TryReadExactly(stream, frameHeader))
        {
            return false;
        }

        int precision = frameHeader[0];
        int height = BinaryPrimitives.ReadUInt16BigEndian(frameHeader[1..3]);
        int width = BinaryPrimitives.ReadUInt16BigEndian(frameHeader[3..5]);
        int components = frameHeader[5];
        return precision > 0
            && width > 0
            && height > 0
            && components > 0
            && segmentLength >= 8 + 3 * components
            && TrySkip(stream, segmentLength - 8);
    }

    private static bool TryReadMarker(Stream stream, out byte marker)
    {
        marker = 0;
        if (!TryReadByte(stream, out byte prefix) || prefix != 0xFF)
        {
            return false;
        }

        do
        {
            if (!TryReadByte(stream, out marker))
            {
                return false;
            }
        }
        while (marker == 0xFF);

        return true;
    }

    private static bool TryReadSegmentLength(Stream stream, out int segmentLength)
    {
        segmentLength = 0;
        Span<byte> length = stackalloc byte[2];
        if (!TryReadExactly(stream, length))
        {
            return false;
        }

        segmentLength = BinaryPrimitives.ReadUInt16BigEndian(length);
        return segmentLength >= 2;
    }

    private static bool TryReadByte(Stream stream, out byte value)
    {
        Span<byte> buffer = stackalloc byte[1];
        if (!TryReadExactly(stream, buffer))
        {
            value = 0;
            return false;
        }

        value = buffer[0];
        return true;
    }

    private static bool TryReadExactly(Stream stream, Span<byte> buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.Read(buffer[offset..]);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static bool TryReadAt(Stream stream, long position, Span<byte> buffer)
    {
        if (position < 0 || position > stream.Length - buffer.Length)
        {
            return false;
        }

        stream.Position = position;
        return TryReadExactly(stream, buffer);
    }

    private static bool TrySkip(Stream stream, int count)
    {
        if (count < 0)
        {
            return false;
        }

        if (stream.Position > stream.Length - count)
        {
            return false;
        }

        stream.Seek(count, SeekOrigin.Current);
        return true;
    }

    private static int ReadAtMost(Stream stream, Span<byte> buffer)
    {
        int length = 0;
        while (length < buffer.Length)
        {
            int read = stream.Read(buffer[length..]);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        return length;
    }

    private static uint Crc32(ReadOnlySpan<byte> value)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte item in value)
        {
            crc ^= item;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xEDB88320u : 0u);
            }
        }

        return ~crc;
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7
            or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
}
