namespace TransLog.Web.Storage;

/// <summary>
/// Checks that bytes are a JPEG and reads its width and height from the header, without an image library.
/// The phone always sends JPEG (it compresses in the browser), so nothing else is accepted.
/// </summary>
public static class JpegInfo
{
    /// <summary>Width and height, or null when the bytes are not a JPEG with a readable size.</summary>
    public static (int Width, int Height)? Read(ReadOnlySpan<byte> b)
    {
        // Start of image: FF D8, followed by another marker.
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8 || b[2] != 0xFF)
            return null;

        var i = 2;
        while (i + 4 <= b.Length)
        {
            if (b[i] != 0xFF)
                return null;
            var marker = b[i + 1];

            // Fill bytes between markers.
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            // Markers without a length.
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                i += 2;
                continue;
            }

            // End of image or start of scan before any frame header: no size found.
            if (marker == 0xD9 || marker == 0xDA)
                return null;

            var length = (b[i + 2] << 8) | b[i + 3];
            if (length < 2)
                return null;

            // Start of frame: C0–CF except C4 (Huffman tables), C8 (reserved) and CC (arithmetic coding).
            if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                if (i + 9 > b.Length)
                    return null;
                var height = (b[i + 5] << 8) | b[i + 6];
                var width = (b[i + 7] << 8) | b[i + 8];
                return width > 0 && height > 0 ? (width, height) : null;
            }

            i += 2 + length;
        }
        return null;
    }
}
