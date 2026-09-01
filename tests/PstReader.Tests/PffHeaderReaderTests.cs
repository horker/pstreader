using PstReader.Format;

namespace PstReader.Tests;

public sealed class PffHeaderReaderTests
{
    [Fact]
    public void Detects4KOstFromHeaderRegardlessOfExtension()
    {
        var path = WriteHeader(".dat", 0x4F53, 0x0024, 0x000C);
        try
        {
            var header = PffHeaderReader.Read(path);

            Assert.Equal(PffStoreKind.Ost, header.StoreKind);
            Assert.Equal(36, header.Version);
            Assert.Equal(12, header.ClientVersion);
            Assert.Equal(PffLayout.Unicode64_4K, header.Layout);
            Assert.Equal(4096, header.PageSize);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DetectsUnicodePstFromHeader()
    {
        var path = WriteHeader(".ost", 0x4D53, 0x0017, 0x0013);
        try
        {
            var header = PffHeaderReader.Read(path);

            Assert.Equal(PffStoreKind.Pst, header.StoreKind);
            Assert.Equal(PffLayout.Unicode64, header.Layout);
            Assert.Equal(512, header.PageSize);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsUnknownContentType()
    {
        var path = WriteHeader(".pst", 0x1234, 0x0017, 0x0013);
        try
        {
            var exception = Assert.Throws<PffFormatException>(() => PffHeaderReader.Read(path));
            Assert.Contains("content type", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteHeader(string extension, ushort contentType, ushort version, ushort clientVersion)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pstreader-{Guid.NewGuid():N}{extension}");
        var bytes = new byte[32];
        bytes[0] = 0x21;
        bytes[1] = 0x42;
        bytes[2] = 0x44;
        bytes[3] = 0x4E;
        WriteUInt16(bytes, 8, contentType);
        WriteUInt16(bytes, 10, version);
        WriteUInt16(bytes, 12, clientVersion);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }
}

