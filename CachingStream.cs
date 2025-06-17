namespace Madieter;

public class CachingStream : Stream
{
    private readonly Stream _normalStream;
    private readonly FileStream _cacheFile;

    // ReSharper disable once ConvertToPrimaryConstructor
    public CachingStream(string cachePath, Stream normalStream)
    {
        _normalStream = normalStream;
        _cacheFile = new FileStream(cachePath + ".tmp", FileMode.Create, FileAccess.Write);
    }

    public override void Flush()
    {
        _normalStream.Flush();
        _cacheFile.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int value = _normalStream.Read(buffer, offset, count);
        _cacheFile.Position += value;
        return value;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long value = _normalStream.Seek(offset, origin);
        _cacheFile.Position = value;
        return value;
    }

    public override void SetLength(long value)
    {
        _normalStream.SetLength(value);
        _cacheFile.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _normalStream.Write(buffer, offset, count);
        _cacheFile.Write(buffer, offset, count);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            //Don't dispose for SharpZipLib, as we're accessing the zip stream directly.
            //_normalStream.Dispose();
            string cacheFileName = _cacheFile.Name;
            _cacheFile.Dispose();
            if (File.Exists(cacheFileName))
            {
                File.Move(cacheFileName, cacheFileName[..^4]);
            }
        }

        base.Dispose(disposing);
    }

    public override bool CanRead => _normalStream.CanRead;
    public override bool CanSeek => _normalStream.CanSeek;
    public override bool CanWrite => _normalStream.CanWrite;
    public override long Length => _normalStream.Length;

    public override long Position
    {
        get => _normalStream.Position;
        set => _normalStream.Position = _cacheFile.Position = value;
    }
}