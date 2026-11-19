using System.Buffers.Binary;

namespace AracKiralama.UI.Infrastructure;

/// <summary>
/// Reads the width and height a picture file declares in its header, without decoding any pixel, so a
/// picture that would need too much memory once opened can be refused first. Only the formats the car
/// pictures use are understood: PNG, JPEG, GIF and BMP.
/// </summary>
internal static class PictureHeader
{
    /// <summary>Whether <paramref name="data"/> starts like a PNG, JPEG, GIF or BMP file with a readable size.</summary>
    public static bool TryReadSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        return TryReadPng(data, ref width, ref height)
            || TryReadGif(data, ref width, ref height)
            || TryReadBmp(data, ref width, ref height)
            || TryReadJpeg(data, ref width, ref height);
    }

    private static bool TryReadPng(ReadOnlySpan<byte> data, ref int width, ref int height)
    {
        // Signature, then the IHDR chunk, which must come first: length, type, width, height.
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        ReadOnlySpan<byte> ihdr = "IHDR"u8;
        if (data.Length < 24 || !data.StartsWith(signature) || !data.Slice(12, 4).SequenceEqual(ihdr))
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(20, 4));
        return true;
    }

    private static bool TryReadGif(ReadOnlySpan<byte> data, ref int width, ref int height)
    {
        if (data.Length < 10 || !(data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8)))
        {
            return false;
        }

        width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2));
        height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2));
        return true;
    }

    private static bool TryReadBmp(ReadOnlySpan<byte> data, ref int width, ref int height)
    {
        if (data.Length < 26 || data[0] != (byte)'B' || data[1] != (byte)'M')
        {
            return false;
        }

        var infoSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(14, 4));
        if (infoSize == 12)
        {
            // BITMAPCOREHEADER: 16-bit width and height.
            width = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(18, 2));
            height = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(20, 2));
        }
        else if (infoSize >= 40)
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(18, 4));
            height = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(22, 4));
        }
        else
        {
            return false;
        }

        // A negative height is a top-down bitmap; the smallest int has no positive counterpart.
        if (height == int.MinValue)
        {
            return false;
        }

        height = Math.Abs(height);
        return true;
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> data, ref int width, ref int height)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return false;
        }

        // Walks the segments up to the first frame header (SOF0-SOF15 except DHT, JPG and DAC).
        var position = 2;
        while (position + 1 < data.Length)
        {
            if (data[position] != 0xFF)
            {
                return false;
            }

            var marker = data[position + 1];
            if (marker == 0xFF)
            {
                // Fill byte before a marker.
                position++;
                continue;
            }

            position += 2;
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                // Markers without a segment.
                continue;
            }

            if (marker is 0xD9 or 0xDA || position + 2 > data.Length)
            {
                // The image ends or its data starts before any frame header.
                return false;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position, 2));
            if (length < 2)
            {
                return false;
            }

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                // Length, precision, height, width.
                if (length < 7 || position + 7 > data.Length)
                {
                    return false;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 5, 2));
                return true;
            }

            position += length;
        }

        return false;
    }
}
