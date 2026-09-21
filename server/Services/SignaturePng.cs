namespace Server.Services;

// Checks the signature image a client sends as a "data:image/png;base64,..."
// URL before it is stored. This is a structural check — the PNG signature,
// a sane IHDR (dimensions within bounds), an IDAT and a closing IEND, all
// inside a size cap — not a full decode. That keeps arbitrary bytes and huge
// images out of the database; the image is only ever handed back to an
// authorized viewer as image/png.
public static class SignaturePng
{
    public const int MaxBytes = 300 * 1024;
    public const int MinWidth = 100;
    public const int MaxWidth = 2000;
    public const int MinHeight = 40;
    public const int MaxHeight = 1000;

    private const string Prefix = "data:image/png;base64,";

    private static readonly byte[] Magic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool TryParse(string? dataUrl, out byte[] png, out string error)
    {
        png = [];

        if (string.IsNullOrWhiteSpace(dataUrl))
        {
            error = "Sign in the box to acknowledge this write-up.";
            return false;
        }

        if (!dataUrl.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = "The signature isn't a PNG image.";
            return false;
        }

        // base64 is 4 bytes for every 3, so check the text length before
        // decoding rather than allocating for something enormous.
        var base64 = dataUrl.AsSpan(Prefix.Length);
        if (base64.Length > (MaxBytes / 3 + 1) * 4)
        {
            error = "The signature image is too large.";
            return false;
        }

        try
        {
            png = Convert.FromBase64String(base64.ToString());
        }
        catch (FormatException)
        {
            error = "The signature image is not valid.";
            return false;
        }

        if (png.Length > MaxBytes)
        {
            error = "The signature image is too large.";
            return false;
        }

        if (!LooksLikePng(png, out var width, out var height))
        {
            error = "The signature isn't a valid PNG image.";
            return false;
        }

        if (width < MinWidth || width > MaxWidth || height < MinHeight || height > MaxHeight)
        {
            error = "The signature image has an unsupported size.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool LooksLikePng(byte[] bytes, out int width, out int height)
    {
        width = height = 0;

        // 8-byte signature, a 13-byte IHDR chunk (length 4 + type 4 + data
        // 13 + crc 4 = 25 bytes), and at the end an empty IEND chunk (12).
        if (bytes.Length < 8 + 25 + 12 || !bytes.AsSpan(0, 8).SequenceEqual(Magic))
        {
            return false;
        }

        if (ReadInt(bytes, 8) != 13 || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        width = ReadInt(bytes, 16);
        height = ReadInt(bytes, 20);

        var end = bytes.Length - 12;
        return ReadInt(bytes, end) == 0
            && bytes.AsSpan(end + 4, 4).SequenceEqual("IEND"u8)
            && bytes.AsSpan().IndexOf("IDAT"u8) >= 0;
    }

    // PNG integers are big-endian.
    private static int ReadInt(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}
