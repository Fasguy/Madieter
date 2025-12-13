using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using CG.Web.MegaApiClient;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Madieter.Controllers;

[Route("dl")]
public class MegaController : Controller
{
	private static DateTime? _bandwidthLimited;

	private static readonly string[] _sizeSuffixes =
	[
		"bytes",
		"KB",
		"MB",
		"GB",
		"TB",
		"PB",
		"EB",
		"ZB",
		"YB"
	];

	[HttpGet("mega/{*url}")]
	public async Task MegaLink(string url)
	{
		if (string.IsNullOrEmpty(url)) throw new ArgumentNullException(nameof(url));

		Console.WriteLine("#############################################");

		if (HasBandwidthExceeded()) return;

		Console.WriteLine($"Received request for {url}");

		MegaLink link = new(new Uri(url));

		if (!link.Valid)
		{
			Console.WriteLine("The Mega link could not be normalized.");

			HttpContext.Response.StatusCode = 400;
			return;
		}

		Console.WriteLine($"Normalized link to {link.Normalized}");

		HttpContext context = HttpContext;
		MegaApiClient client = new();

		Credentials.CreateFile();

		if (!await HandleMegaLogin(client, context)) return;

		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

		context.RequestAborted = cts.Token;
		Stream? bodyStream = null;
		Stream? cacheStream = null;

		try
		{
			if (link.Type == Madieter.MegaLink.LinkType.Folder)
			{
				IEnumerable<INode> nodes = (await client.GetNodesFromLinkAsync(link.Normalized)).ToArray();
				IEnumerable<INode> files = nodes.Where(x => x.Type == NodeType.File);

				INode root = nodes.First(x => x.Type == NodeType.Root);
				long finalSize = CalculateZipSize(nodes);
				Console.WriteLine($"Downloading {root.Name} (~{finalSize} bytes / ~{SizeSuffix(finalSize, 2)})");

				context.Features.Get<IHttpResponseBodyFeature>()!.DisableBuffering();
				context.Response.ContentType = "application/octet-stream";
				context.Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{root.Name}.zip\"");
				context.Response.Headers.Append("Content-Length", finalSize.ToString());

				bodyStream = context.Response.BodyWriter.AsStream();
				context.Response.StatusCode = 200;
				await context.Response.StartAsync(cts.Token);

				string cacheDirectory = Path.Combine("cache", link.Id);

				Directory.CreateDirectory(cacheDirectory);

				using (ZipArchive archive = new(bodyStream, ZipArchiveMode.Create, true))
				{
					foreach (INode file in files)
					{
						ZipArchiveEntry zipEntry = archive.CreateEntry(GetParents(file, nodes) + "/" + file.Name, CompressionLevel.NoCompression);

						await using Stream entryStream = zipEntry.Open();

						string cachePath = Path.Combine("cache", link.Id, file.Fingerprint);

						if (CachingStream.TryGetCache(cachePath, file.Size, out cacheStream))
						{
							Debug.Assert(cacheStream != null, nameof(cacheStream) + " != null");
							await cacheStream.CopyToAsync(entryStream, 1024 * 64, cts.Token);
							await cacheStream.DisposeAsync();
						}
						else
						{
							cacheStream = new CachingStream(cachePath, entryStream);
							await client.DownloadFileAsync(file, cacheStream);
							await cacheStream.DisposeAsync();
						}
					}
				}

				try
				{
					Directory.Delete(cacheDirectory, true);
				}
				catch (Exception ex)
				{
					Console.WriteLine("Failed to remove caching directory:");
					Console.WriteLine(ex);
				}

				await context.Response.CompleteAsync();
			}
			else
			{
				INode node = await client.GetNodeFromLinkAsync(link.Normalized);
				Console.WriteLine($"Downloading {node.Name} ({node.Size} bytes / ~{SizeSuffix(node.Size, 2)})");

				context.Features.Get<IHttpResponseBodyFeature>()!.DisableBuffering();
				context.Response.ContentType = "application/octet-stream";
				context.Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{node.Name}\"");
				context.Response.Headers.Append("Content-Length", node.Size.ToString());
				bodyStream = context.Response.BodyWriter.AsStream();
				context.Response.StatusCode = 200;
				await context.Response.StartAsync(cts.Token);

				await client.DownloadFileAsync(link.Normalized, bodyStream);

				await context.Response.CompleteAsync();
			}
		}
		catch (ApiException ex)
		{
			switch (ex.ApiResultCode)
			{
				case ApiResultCode.ResourceNotExists:
					Console.WriteLine("The requested resource doesn't exist.");

					CancelRequest(404);
					return;
				case ApiResultCode.ToManyRequestsForThisResource:
					Console.WriteLine("The resource has been requested too many times.");

					//CancelRequest(429);
					// Despite its name, this API response seems to be often used for files that have been blocked for legal reasons.
					// Response code changed to 451.
					CancelRequest(451);
					return;
				case ApiResultCode.ResourceExpired:
					Console.WriteLine("The requested resource has expired.");

					CancelRequest(410);
					return;
				case ApiResultCode.RequestFailedRetry:
					Console.WriteLine("The request failed. Retry after some time.");

					CancelRequest(503);
					return;
				case ApiResultCode.ResourceAdministrativelyBlocked:
					Console.WriteLine("The requested resource has been removed for violating terms of service.");

					CancelRequest(451);
					return;
			}

			throw;
		}
		catch (AggregateException ex)
		{
			foreach (Exception innerException in ex.InnerExceptions)
				switch (innerException)
				{
					case HttpRequestException httpRequestException:
						if (httpRequestException.StatusCode == (HttpStatusCode?)509)
						{
							Console.WriteLine("Bandwidth limit exceeded.");

							_bandwidthLimited = DateTime.Now;

							CancelRequest(509, true);
							return;
						}

						break;
				}

			throw;
		}
		catch (InvalidOperationException ex)
		{
			if (ex.Message.StartsWith("Response Content-Length mismatch"))
			{
				Console.WriteLine($"Content-Length mismatch. Simulating unavailability. ({ex.Message})");

				CancelRequest(503, true);
			}
		}
		catch (Exception ex)
		{
			await cts.CancelAsync();
			throw;
		}
		finally
		{
			if (bodyStream != null)
				try
				{
					await bodyStream.DisposeAsync();
				}
				catch (InvalidOperationException ex)
				{
					if (ex.Message.StartsWith("Response Content-Length mismatch"))
					{
						//ignore
					}
					else
					{
						throw;
					}
				}

			if (cacheStream != null)
				try
				{
					await cacheStream.DisposeAsync();
				}
				catch (ObjectDisposedException)
				{
					//Already disposed. Ignore.
				}
				catch (Exception ex)
				{
					Console.WriteLine("Failed to dispose caching stream!");
					Console.WriteLine(ex);
				}

			await client.LogoutAsync();
			Console.WriteLine("Request ended.");
		}

		return;

		void CancelRequest(int responseCode, bool abort = false)
		{
			try
			{
				context.Response.StatusCode = responseCode;
			}
			catch (InvalidOperationException ex)
			{
				if (ex.Message.StartsWith("StatusCode cannot be set because the response has already started."))
					//ignore
					return;

				throw;
			}
			finally
			{
				//cts.Cancel();
				if (abort)
				{
					Console.WriteLine("Aborting request.");
					context.Abort();
				}
			}
		}
	}

	private static async Task<bool> HandleMegaLogin(MegaApiClient client, HttpContext context)
	{
		try
		{
			await client.LoginAnonymousAsync();
		}
		catch (ApiException ex)
		{
			switch (ex.ApiResultCode)
			{
				case ApiResultCode.RequestFailedRetry:
					Console.WriteLine("The request failed. Retry after some time.");

					context.Response.StatusCode = 503;
					return false;
			}

			throw;
		}
		catch (HttpRequestException ex)
		{
			if (ex.StatusCode == HttpStatusCode.PaymentRequired)
			{
				Console.WriteLine("Mega is returning 402 - Payment Required. Trying to log in as account.");

				try
				{
					if (Credentials.TryRead(out Credentials? value))
					{
						await client.LoginAsync(value!.Email, value.Password, value.MfaKey);
					}
					else
					{
						Console.WriteLine("User hasn't provided login credentials. Responding as server error.");

						context.Response.StatusCode = 503;
						return false;
					}
				}
				catch (HttpRequestException ex2)
				{
					if (ex2.StatusCode == HttpStatusCode.PaymentRequired)
					{
						Console.WriteLine("Mega is still returning 402 - Payment Required. Responding as server error.");

						context.Response.StatusCode = 503;
						return false;
					}
				}
			}
		}

		return true;
	}

	private bool HasBandwidthExceeded()
	{
		// Yes, I know that just manually enforcing a bandwidth limit is not a good idea.
		// It does the job well enough.
		if (DateTime.Now - _bandwidthLimited < TimeSpan.FromHours(1))
		{
			Console.WriteLine("Bandwidth has been limited. Request will not be handled.");
			HttpContext.Response.StatusCode = 509;
			return true;
		}

		return false;
	}

	private static long CalculateZipSize(IEnumerable<INode> items)
	{
		//zip_size = num_of_files * (30 + 16 + 46) + 2 * total_length_of_filenames + total_size_of_files + 22

		const int endOfCentralDirectoryRecord = 22;

		//Files
		const int localFileHeader = 30;
		const int dataDescriptor = 16;
		const int centralDirectoryFileHeader = 46;

		//Zip64
		const int endOfCentralDirectoryRecord64 = 56;
		const int endOfCentralDirectoryLocator = 20;

		// ReSharper disable once PossibleMultipleEnumeration
		IEnumerable<INode> files = items.Where(x => x.Type == NodeType.File).ToArray();
		long totalSizeOfFiles = 0;
		long totalLengthOfFileNames = 0;
		long totalZip64FileBytes = 0;

		bool zip64 = false;

		int numOfFiles = files.Count();

		if (numOfFiles > 65535) zip64 = true;

		long offsetOfLocalHeader = 0;

		foreach (INode file in files)
		{
			bool isZip64File = false;

			if (file.Size > uint.MaxValue)
			{
				zip64 = isZip64File = true;
				totalZip64FileBytes += 8; //Uncompressed Size Zip64 Field
				totalZip64FileBytes += 8; //Compressed Size Zip64 Field
			}

			if (offsetOfLocalHeader > uint.MaxValue)
			{
				zip64 = isZip64File = true;
				totalZip64FileBytes += 8; //Local Header Offset Zip64 Field
			}

			if (isZip64File) totalZip64FileBytes += 4;

			totalSizeOfFiles += file.Size;

			// ReSharper disable once PossibleMultipleEnumeration
			int fileNameLength = Encoding.UTF8.GetByteCount(GetParents(file, items) + "/" + file.Name);

			offsetOfLocalHeader += localFileHeader + dataDescriptor + file.Size + fileNameLength;

			totalLengthOfFileNames += fileNameLength;
		}

		long totalSize = numOfFiles * (localFileHeader + dataDescriptor + centralDirectoryFileHeader) + 2 * totalLengthOfFileNames + totalSizeOfFiles + totalZip64FileBytes;

		if (totalSize > uint.MaxValue) zip64 = true;

		if (zip64)
		{
			totalSize += endOfCentralDirectoryRecord64 + endOfCentralDirectoryLocator;

			if (numOfFiles == 1)
				// For a reason that I don't really understand, if it's a Zip64 file and there is only one file in total, then the zip grows an additional 8 bytes.
				// I have worked my way through the zip creation process with the debugger, but can not seem to figure out what causes this.
				totalSize += 8;
		}

		totalSize += endOfCentralDirectoryRecord;

		return totalSize;
	}

	private static string GetParents(INode node, IEnumerable<INode> nodes)
	{
		List<string> parents = [];
		while (node.ParentId != null)
		{
			// ReSharper disable once PossibleMultipleEnumeration
			INode parentNode = nodes.Single(x => x.Id == node.ParentId);
			parents.Insert(0, parentNode.Name);
			node = parentNode;
		}

		return string.Join('/', parents);
	}

	//https://stackoverflow.com/a/14488941 - JLRishe
	private static string SizeSuffix(long value, int decimalPlaces = 1)
	{
		if (decimalPlaces < 0) throw new ArgumentOutOfRangeException(nameof(decimalPlaces));

		switch (value)
		{
			case < 0:
				return "-" + SizeSuffix(-value, decimalPlaces);
			case 0:
				return string.Format("{0:n" + decimalPlaces + "} bytes", 0);
		}

		// mag is 0 for bytes, 1 for KB, 2, for MB, etc.
		int mag = (int)Math.Log(value, 1024);

		// 1L << (mag * 10) == 2 ^ (10 * mag) 
		// [i.e. the number of bytes in the unit corresponding to mag]
		decimal adjustedSize = (decimal)value / (1L << (mag * 10));

		// make adjustment when the value is large enough that
		// it would round up to 1000 or more
		if (Math.Round(adjustedSize, decimalPlaces) >= 1000)
		{
			mag += 1;
			adjustedSize /= 1024;
		}

		return string.Format("{0:n" + decimalPlaces + "} {1}", adjustedSize, _sizeSuffixes[mag]);
	}
}