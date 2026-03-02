using System.Text.Json.Serialization;

namespace Nesti.Models;

public class NotificationMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("instanceId")]
    public string? InstanceId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("notificationType")]
    public string? NotificationType { get; set; }

    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }

    /// <summary>Unique key used to suppress duplicate notifications.</summary>
    public string DedupeKey =>
        InstanceId ?? Id ?? $"{Title}|{Message}|{Timestamp}";

    /// <summary>Best available body text.</summary>
    public string Body =>
        !string.IsNullOrWhiteSpace(Message)     ? Message :
        !string.IsNullOrWhiteSpace(Description) ? Description :
        string.Empty;
}
