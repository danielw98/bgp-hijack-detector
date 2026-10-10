using System.IO.Compression;
using BGPWatch.Core.Application;
using BGPWatch.Core.Domain;
using ICSharpCode.SharpZipLib.BZip2;

namespace BGPWatch.Web.Native;

/// <summary>Recorded MRT updates files (RFC 6396), plain, .gz (RIPE RIS) or .bz2 (RouteViews).</summary>
public sealed class MrtArchive : IUpdateArchive
{
    public IReadOnlyList<Announcement> Read(string path)
    {
        return UpdateDecoder.ReadMrt(Bytes(path));
    }

    private static byte[] Bytes(string path)
    {
        using var file = File.OpenRead(path);
        using var stream = Decompressed(file, Path.GetExtension(path));
        using var plain = new MemoryStream();
        stream.CopyTo(plain);
        return plain.ToArray();
    }

    private static Stream Decompressed(Stream file, string extension)
    {
        return extension switch
        {
            ".gz" => new GZipStream(file, CompressionMode.Decompress),
            ".bz2" => new BZip2InputStream(file),
            _ => file,
        };
    }
}
