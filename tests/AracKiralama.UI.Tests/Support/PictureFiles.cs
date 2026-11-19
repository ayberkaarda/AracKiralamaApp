using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace AracKiralama.UI.Tests.Support;

/// <summary>Picture files built byte by byte, so their headers can claim any size.</summary>
internal static class PictureFiles
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// A complete black and white PNG of the given size. Its rows are all zero, so even a huge picture
    /// compresses to a small file (30000 x 30000 is about 110 KB).
    /// </summary>
    public static byte[] Png(int width, int height)
    {
        using var file = new MemoryStream();
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 1; // one bit per pixel
        header[9] = 0; // grayscale
        WriteChunk(file, "IHDR", header);

        using (var pixels = new MemoryStream())
        {
            using (var zlib = new ZLibStream(pixels, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                var row = new byte[1 + ((width + 7) / 8)];
                for (var y = 0; y < height; y++)
                {
                    zlib.Write(row);
                }
            }

            WriteChunk(file, "IDAT", pixels.ToArray());
        }

        WriteChunk(file, "IEND", []);
        return file.ToArray();
    }

    /// <summary>The start of a GIF whose logical screen has the given size.</summary>
    public static byte[] HeaderOnlyGif(ushort width, ushort height)
    {
        var data = new byte[13];
        Encoding.ASCII.GetBytes("GIF89a").CopyTo(data, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), width);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), height);
        return data;
    }

    /// <summary>The start of a BMP of the given size; a negative height is a top-down bitmap.</summary>
    public static byte[] HeaderOnlyBmp(int width, int height)
    {
        var data = new byte[54];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), height);
        return data;
    }

    /// <summary>The start of a JPEG with an APP0 segment and a baseline frame of the given size.</summary>
    public static byte[] HeaderOnlyJpeg(ushort width, ushort height)
    {
        using var data = new MemoryStream();
        data.Write([0xFF, 0xD8]);
        data.Write([0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);
        var frame = new byte[] { 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0, 0, 0, 0, 0x01, 0x01, 0x11, 0x00 };
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(5), height);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(7), width);
        data.Write(frame);
        return data.ToArray();
    }

    private static void WriteChunk(Stream file, string type, byte[] content)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, content.Length);
        file.Write(number);

        var typeAndContent = new byte[4 + content.Length];
        Encoding.ASCII.GetBytes(type).CopyTo(typeAndContent, 0);
        content.CopyTo(typeAndContent, 4);
        file.Write(typeAndContent);

        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typeAndContent));
        file.Write(number);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
