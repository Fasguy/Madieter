namespace Madieter.Streams;

internal class FakeDataStream(long size) : Stream
{
	private long _length = size;

	public override bool CanRead => true;
	public override bool CanWrite => false;
	public override bool CanSeek => false;
	public override long Length => _length;

	public override long Position
	{
		get => _length;
		set => throw new NotSupportedException();
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		if (_length <= 0) return 0;
		int n = (int)Math.Min(count, _length);
		_length -= n;
		return n;
	}

	public override void Flush()
	{
	}

	public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
	public override void SetLength(long value) => throw new NotImplementedException();
}
