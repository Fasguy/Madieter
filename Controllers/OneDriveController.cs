using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Madieter.Controllers;

[Route("dl")]
public class OneDriveController : Controller
{
	[HttpGet("onedrive/{*url}")]
	public Task OneDriveLink(string url)
	{
		// TODO: This currently doesn't work for some links.
		// Those will throw a "Microsoft.Vroom.Exceptions.UnauthenticatedVroomException" as a response.
		// It can be mitigated by sending a "Prefer" header with the value "redeemSharingLink", but that then requires authentication, when not accessed through a browser.
		Console.WriteLine($"Received request for {url}");
		url += HttpContext.Request.QueryString;
		if (string.IsNullOrEmpty(url)) throw new ArgumentNullException(nameof(url));

		string base64Value = Convert.ToBase64String(Encoding.UTF8.GetBytes(url));
		string encodedUrl = "u!" + base64Value.TrimEnd('=').Replace('/', '_').Replace('+', '-');

		string resultUrl = $"https://api.onedrive.com/v1.0/shares/{encodedUrl}/root/content";

		Console.WriteLine("Redirecting to " + resultUrl);

		HttpContext.Response.Redirect(resultUrl);

		return Task.CompletedTask;
	}
}