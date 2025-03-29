using System.IO.Compression;

namespace Madieter.Extensions;

public static class ZipArchiveEntryEx
{
    public static bool TryGetCache(this ZipArchiveEntry @this, string cachePath, out Stream? cacheStream)
    {
        if (File.Exists(cachePath))
        {
            cacheStream = new FileStream(cachePath, FileMode.Open, FileAccess.Read);
            return true;
        }

        cacheStream = null;
        return false;
    }
}