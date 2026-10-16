using System.Text;
using EastwardLib.Assets;
using ZstdSharp;

internal static class ArchiveStreamingTests
{
    public static void Run()
    {
        var content = Encoding.UTF8.GetBytes("A streaming frame without a declared content size. 한글 日本語");
        using var packed = new MemoryStream();
        using (var stream = new CompressionStream(packed, level: 3, leaveOpen: true))
        {
            stream.Write(content);
        }
        var compressed = packed.ToArray();
        if ((compressed[4] >> 6) != 0 || (compressed[4] & 32) != 0)
        {
            throw new InvalidOperationException("Fixture must omit the Zstd content size");
        }
        if (Decompressor.GetDecompressedSize(compressed) <= (ulong)content.Length)
        {
            throw new InvalidOperationException("Fixture must expose the inferred-bound regression");
        }
        var valid = GArchive.Read("fixture", Archive(compressed, content.Length));
        if (!valid["fixture/message"].AsSpan().SequenceEqual(content))
        {
            throw new InvalidOperationException("Streaming frame must preserve exact content");
        }
        Reject(() => GArchive.Read("fixture", Archive(compressed, content.Length - 1)));
        Reject(() => GArchive.Read("fixture", Archive(compressed, content.Length + 1)));
        var malformed = compressed.ToArray();
        malformed[0] ^= 255;
        Reject(() => GArchive.Read("fixture", Archive(malformed, content.Length)));
        Console.WriteLine("Streaming Zstd archives passed: omitted content size, exact output length, undersized/oversized headers and corrupt frames.");
    }

    private static byte[] Archive(byte[] compressed, int decodedSize)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write(27191);
        writer.Write(1);
        writer.Write(Encoding.UTF8.GetBytes("message\0"));
        writer.Write(8 + 8 + 16);
        writer.Write(2);
        writer.Write(decodedSize);
        writer.Write(compressed.Length);
        writer.Write(compressed);
        return output.ToArray();
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid archive was accepted");
    }
}
