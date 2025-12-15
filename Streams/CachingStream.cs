namespace Madieter.Streams;

public class CachingStream : Stream
{
	private readonly FileStream _cacheFile;
	private readonly Stream _normalStream;
	private readonly bool _leaveStreamOpen;

	public override bool CanRead => _normalStream.CanRead;
	public override bool CanWrite => _normalStream.CanWrite;
	public override bool CanSeek => _normalStream.CanSeek;
	public override long Length => _normalStream.Length;

	public override long Position
	{
		get => _normalStream.Position;
		set => _normalStream.Position = _cacheFile.Position = value;
	}

	// ReSharper disable once ConvertToPrimaryConstructor
	public CachingStream(string cachePath, Stream normalStream, bool leaveStreamOpen = false)
	{
		_normalStream = normalStream;
		_cacheFile = new FileStream(cachePath + ".tmp", FileMode.Create, FileAccess.Write);
		_leaveStreamOpen = leaveStreamOpen;
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		int value = _normalStream.Read(buffer, offset, count);
		_cacheFile.Position += value;
		return value;
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		_normalStream.Write(buffer, offset, count);
		_cacheFile.Write(buffer, offset, count);
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

	public override void Flush()
	{
		_normalStream.Flush();
		_cacheFile.Flush();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			if (!_leaveStreamOpen)
			{
				_normalStream.Dispose();
			}

			string cacheFileName = _cacheFile.Name;
			_cacheFile.Dispose();
			if (File.Exists(cacheFileName))
			{
				string destCacheFileName = cacheFileName[..^4];
				File.Delete(destCacheFileName);
				File.Move(cacheFileName, destCacheFileName);
			}
		}

		base.Dispose(disposing);
	}

	public static bool TryGetCache(string cachePath, long expectedSize, out Stream? cacheStream)
	{
		if (IsCached(cachePath, expectedSize))
		{
			cacheStream = new FileStream(cachePath, FileMode.Open, FileAccess.Read);
			return true;
		}

		cacheStream = null;
		return false;
	}

	public static bool IsCached(string cachePath, long expectedSize)
	{
		return File.Exists(cachePath) && new FileInfo(cachePath).Length == expectedSize;
	}
}
