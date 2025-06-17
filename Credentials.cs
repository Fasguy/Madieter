using System.Text.Json;

namespace Madieter;

public class Credentials
{
	public string Email { get; init; }
	public string Password { get; init; }
	public string MfaKey { get; init; }

	public static void CreateFile()
	{
		File.WriteAllText("credentials.json", JsonSerializer.Serialize(new Credentials()));
	}

	public static bool TryRead(out Credentials? result)
	{
		if (File.Exists("credentials.json"))
		{
			string credentialsText = File.ReadAllText("credentials.json");
			result = JsonSerializer.Deserialize<Credentials>(credentialsText);
			return result != null && !string.IsNullOrEmpty(result.Email) && !string.IsNullOrEmpty(result.Password);
		}

		result = null;
		return false;
	}
}