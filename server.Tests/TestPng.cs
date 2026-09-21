using System.Buffers.Binary;

namespace Server.Tests;

// Builds byte arrays that are structurally PNGs (signature, IHDR, IDAT, IEND)
// for the signature tests. They carry filler instead of real image data, so
// they can't be decoded — SignaturePng only checks structure, which is all
// these need.
internal static class TestPng
{
    public static byte[] Build(int width = 1000, int height = 320, int dataBytes = 200, bool idat = true, bool iend = true)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        Chunk(ms, "IHDR", header);

        if (idat)
        {
            Chunk(ms, "IDAT", new byte[dataBytes]);
        }

        if (iend)
        {
            Chunk(ms, "IEND", []);
        }

        return ms.ToArray();
    }

    public static string DataUrl(byte[] png) => "data:image/png;base64," + Convert.ToBase64String(png);

    public static string DataUrl() => DataUrl(Build());

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(System.Text.Encoding.ASCII.GetBytes(type));
        stream.Write(data);
        stream.Write(new byte[4]); // CRC — not checked by SignaturePng
    }
}
