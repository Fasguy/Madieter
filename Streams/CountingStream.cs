namespace Madieter.Streams;

public class CountingStream : Stream
{
	private long _length;

	public override bool CanRead => false;
	public override bool CanWrite => true;
	public override bool CanSeek => false;
	public override long Length => _length;

	public override long Position
	{
		get => _length;
		set => throw new NotSupportedException();
	}

	public override void Write(byte[] buffer, int offset, int count) => _length += count;

	public override void Write(ReadOnlySpan<byte> buffer) => _length += buffer.Length;

	public override void Flush()
	{
	}

	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
}
