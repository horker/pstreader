namespace PstReader.Format;

internal enum PffStoreKind
{
    Pst,
    Ost,
}

internal enum PffLayout
{
    Ansi32,
    Unicode64,
    Unicode64_4K,
}

internal sealed record PffHeader(
    PffStoreKind StoreKind,
    ushort Version,
    ushort ClientVersion,
    PffLayout Layout,
    int PageSize,
    long FileSize);

public sealed class PffFormatException(string message) : IOException(message);

internal static class PffHeaderReader
{
    public static PffHeader Read(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        var header = new byte[32];
        var offset = 0;
        while (offset < header.Length)
        {
            var read = stream.Read(header, offset, header.Length - offset);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }

        if (header[0] != 0x21 || header[1] != 0x42 || header[2] != 0x44 || header[3] != 0x4E)
        {
            throw new PffFormatException("The PST/OST !BDN signature is missing.");
        }

        var storeKind = ReadUInt16(header, 8) switch
        {
            0x4D53 => PffStoreKind.Pst,
            0x4F53 => PffStoreKind.Ost,
            var contentType => throw new PffFormatException(
                $"Unsupported content type: 0x{contentType:X4}"),
        };

        var version = ReadUInt16(header, 10);
        var layout = version switch
        {
            0x000E or 0x000F => PffLayout.Ansi32,
            0x0017 => PffLayout.Unicode64,
            0x0024 => PffLayout.Unicode64_4K,
            _ => throw new PffFormatException($"Unsupported PFF version: 0x{version:X4}"),
        };

        var clientVersion = ReadUInt16(header, 12);
        return new PffHeader(
            storeKind,
            version,
            clientVersion,
            layout,
            layout is PffLayout.Unicode64_4K ? 4096 : 512,
            stream.Length);
    }

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
}
