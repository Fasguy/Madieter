using ICSharpCode.SharpZipLib.Checksum;
using System.Reflection;
using ICSharpCode.SharpZipLib.Zip;

namespace Madieter;

public class PatchedZipOutputStream(Stream baseOutputStream) : ZipOutputStream(baseOutputStream)
{
	private static readonly FieldInfo _curEntryField;
	private static readonly FieldInfo _curMethodField;
	private static readonly FieldInfo _sizeField;
	private static readonly FieldInfo _crcField;
	private static readonly FieldInfo _patchDataField;
	private static readonly FieldInfo _offsetField;
	private static readonly MethodInfo _transformEntryNameMethod;
	private static readonly MethodInfo _writeLocalHeaderMethod;

	static PatchedZipOutputStream()
	{
		_curEntryField = typeof(ZipOutputStream).GetField("curEntry", BindingFlags.NonPublic | BindingFlags.Instance)
			?? throw new InvalidOperationException("Could not find curEntry field in ZipOutputStream");
		_curMethodField = typeof(ZipOutputStream).GetField("curMethod", BindingFlags.NonPublic | BindingFlags.Instance)
			?? throw new InvalidOperationException("Could not find curMethod field in ZipOutputStream");
		_sizeField = typeof(ZipOutputStream).GetField("size", BindingFlags.NonPublic | BindingFlags.Instance)
			?? throw new InvalidOperationException("Could not find size field in ZipOutputStream");
		_crcField = typeof(ZipOutputStream).GetField("crc", BindingFlags.NonPublic | BindingFlags.Instance)
			?? throw new InvalidOperationException("Could not find crc field in ZipOutputStream");
		_patchDataField = typeof(ZipOutputStream).GetField("patchData", BindingFlags.NonPublic | BindingFlags.Instance)
			?? throw new InvalidOperationException("Could not find patchData field in ZipOutputStream");
		_offsetField = typeof(ZipOutputStream).GetField("offset", BindingFlags.NonPublic | BindingFlags.Instance)
			?? throw new InvalidOperationException("Could not find offset field in ZipOutputStream");
		_transformEntryNameMethod = typeof(ZipOutputStream).GetMethod("TransformEntryName", BindingFlags.NonPublic | BindingFlags.Instance)
			?? throw new InvalidOperationException("Could not find TransformEntryName method in ZipOutputStream");
		
		Type type = typeof(ZipOutputStream).Assembly.GetType("ICSharpCode.SharpZipLib.Zip.ZipFormat")!;
		_writeLocalHeaderMethod = type.GetMethod("WriteLocalHeader", BindingFlags.NonPublic | BindingFlags.Static)
		    ?? throw new InvalidOperationException("Could not find WriteLocalHeader method in ZipFormat");
	}

	public new void PutNextEntry(ZipEntry entry)
	{
		if (_curEntryField.GetValue(this) != null)
		{
			CloseEntry();
		}

		PutNextEntry(baseOutputStream_, entry);
	}
	
	// This is pretty much the original PutNextEntry method, with things I don't need removed, as well as one important change.
	// This method behaves like the first check of headerInfoAvailable is true, with every subsequent check behaving like it's false.
	// This is the only way I found to make this library actually use the 'STORE' method for entries when using a non-seekable (network) stream.
	// The normal behaviour would switch them to 'DEFLATE NO_COMPRESSION', which wastes resources for no reason.
	private void PutNextEntry(Stream stream, ZipEntry entry, long streamOffset = 0)
	{
		entry.Flags &= (int)GeneralBitFlags.UnicodeText;
		entry.Flags |= 8;
		
		long offset = (long)_offsetField.GetValue(this)!;
		entry.Offset = offset;

		_curMethodField.SetValue(this, CompressionMethod.Stored);

		entry.ForceZip64();
		
		_transformEntryNameMethod.Invoke(this, [entry]);

		object[] parameters = [stream, entry, null!, /*headerInfoAvailable*/false, false, streamOffset, _stringCodec];
		object result = _writeLocalHeaderMethod.Invoke(null, parameters)!;
		offset += (int)result;
		object entryPatchData = parameters[2];
		
		_offsetField.SetValue(this, offset);
		
		_patchDataField.SetValue(this, entryPatchData);

		_curEntryField.SetValue(this, entry);
		_sizeField.SetValue(this, 0L);

		Crc32 crc = (Crc32)_crcField.GetValue(this)!;
		crc.Reset();
	}
}
