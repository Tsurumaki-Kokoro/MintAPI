using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ossapi.Auth;

public class TokenStore
{
    private readonly string _directory;

    public TokenStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ossapi-dotnet", "tokens");
        Directory.CreateDirectory(_directory);
    }

    public record StoredToken(
        string AccessToken,
        string? RefreshToken,
        DateTimeOffset ExpiresAt);

    public StoredToken? Load(string key)
    {
        var path = TokenPath(key);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<StoredToken>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Save(string key, StoredToken token)
    {
        var path = TokenPath(key);
        File.WriteAllText(path, JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Delete(string key)
    {
        var path = TokenPath(key);
        if (File.Exists(path)) File.Delete(path);
    }

    private string TokenPath(string key) => Path.Combine(_directory, $"{key}.token.json");

    public static string GenerateKey(OAuthGrant grant, int clientId, string clientSecret,
        IEnumerable<string> scopes, string domain = "osu")
    {
        var input = $"{grant}:{clientId}:{clientSecret}:{string.Join(",", scopes.OrderBy(s => s))}:{domain}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
