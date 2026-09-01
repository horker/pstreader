using System.Buffers.Binary;

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

internal sealed class PffFormatException(string message) : IOException(message);

internal static class PffHeaderReader
{
    private static ReadOnlySpan<byte> Magic => [0x21, 0x42, 0x44, 0x4E];

    public static PffHeader Read(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        Span<byte> header = stackalloc byte[32];
        stream.ReadExactly(header);

        if (!header[..4].SequenceEqual(Magic))
        {
            throw new PffFormatException("PST/OST の !BDN シグネチャがありません。");
        }

        var storeKind = BinaryPrimitives.ReadUInt16LittleEndian(header[8..10]) switch
        {
            0x4D53 => PffStoreKind.Pst,
            0x4F53 => PffStoreKind.Ost,
            var contentType => throw new PffFormatException(
                $"未対応の content type です: 0x{contentType:X4}"),
        };

        var version = BinaryPrimitives.ReadUInt16LittleEndian(header[10..12]);
        var layout = version switch
        {
            0x000E or 0x000F => PffLayout.Ansi32,
            0x0017 => PffLayout.Unicode64,
            0x0024 => PffLayout.Unicode64_4K,
            _ => throw new PffFormatException($"未対応の PFF バージョンです: 0x{version:X4}"),
        };

        var clientVersion = BinaryPrimitives.ReadUInt16LittleEndian(header[12..14]);
        return new PffHeader(
            storeKind,
            version,
            clientVersion,
            layout,
            layout is PffLayout.Unicode64_4K ? 4096 : 512,
            stream.Length);
    }
}
