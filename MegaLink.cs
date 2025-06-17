using System.Text.RegularExpressions;

namespace Madieter;

public partial class MegaLink
{
	private static readonly string[] _folderValues =
	[
		"folder",
		"F"
	];

	public Uri? Normalized { get; }

	public LinkType Type { get; }

	public bool Valid { get; }

	public string Id { get; } = string.Empty;

	public string Key { get; } = string.Empty;

	public MegaLink(Uri uri)
	{
		if (TryGetPartsFromUri(uri, out string? id, out string? key, out bool isFolder))
		{
			//TODO: Just remove all non-base64 characters
			key = key?.Trim(')');
			Normalized = new Uri($"https://mega.nz/{(isFolder ? "folder" : "file")}/{id}!{key}");
			Type = isFolder ? LinkType.Folder : LinkType.File;
			Valid = true;
			Id = id!;
			Key = key!;
		}
	}

	private static bool TryGetPartsFromUri(Uri uri, out string? id, out string? key, out bool isFolder)
	{
		return
			//Modern Uri
			MatchRegex(ModernUriRegex(), out id, out key, out isFolder)
			//Legacy Uri
			|| MatchRegex(LegacyUriRegex(), out id, out key, out isFolder);

		bool MatchRegex(Regex uriRegex, out string? id, out string? key, out bool isFolder)
		{
			Match match = uriRegex.Match(uri.PathAndQuery + uri.Fragment);
			if (match.Success)
			{
				id = match.Groups["id"].Value;
				key = match.Groups["key"].Value;
				isFolder = _folderValues.Contains(match.Groups["type"].Value);
				return true;
			}

			id = null;
			key = null;
			isFolder = false;
			return false;
		}
	}
	
	public enum LinkType
	{
		Unknown,
		File,
		Folder
	}

    [GeneratedRegex("/(?<type>(file|folder))/(?<id>[^#]+)(#|!)(?<key>[^$/]+)")]
    private static partial Regex ModernUriRegex();
    [GeneratedRegex(@"#?(?<type>F?)!(?<id>[^!]+)!(?<key>[^$!\?]+)")]
    private static partial Regex LegacyUriRegex();
}