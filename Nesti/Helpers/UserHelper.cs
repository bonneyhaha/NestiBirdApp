using System.Net.Http;
using System.Text.Json;

namespace Nesti.Helpers;

public static class UserHelper
{
    /// <summary>Returns the Windows login name (e.g. "jdoe").</summary>
    public static string GetSystemUsername() =>
        Environment.UserName
        ?? Environment.GetEnvironmentVariable("USERNAME")
        ?? "Guest";

    /// <summary>
    /// Calls the API to get the employee's full display name.
    /// Returns null if the endpoint is not configured or the call fails.
    /// </summary>
    public static async Task<string?> GetFullNameAsync(string username)
    {
        var path    = AppConfig.ApiGetFullnamePath;
        var baseUrl = AppConfig.ApiBaseUrl;

        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(baseUrl))
            return null;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var url  = $"{baseUrl.TrimEnd('/')}{path}?username={Uri.EscapeDataString(username)}";
            var json = await client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("fullName", out var prop))
                return prop.GetString();
        }
        catch { /* fall through to null */ }

        return null;
    }

    /// <summary>
    /// Extracts a friendly first name from a full name string.
    /// Falls back to the longest word if the first word is too short (≤ 2 chars).
    /// </summary>
    public static string GetFirstName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts[0].Length > 2
            ? parts[0]
            : parts.OrderByDescending(p => p.Length).First();
    }

    /// <summary>Returns a time-of-day greeting string.</summary>
    public static string GetGreeting() => DateTime.Now.Hour switch
    {
        < 12 => "Good Morning",
        < 17 => "Good Afternoon",
        _    => "Good Evening"
    };
}
