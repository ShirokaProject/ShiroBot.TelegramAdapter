using System.Text.Json.Serialization;

namespace ShiroBot.Telegram.Model;

/// <summary>Telegram Bot API `User`。</summary>
public sealed record TgUser
{
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    [JsonPropertyName("is_bot")]
    public bool IsBot { get; init; }

    [JsonPropertyName("first_name")]
    public required string FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonIgnore]
    public string DisplayName => LastName is null ? FirstName : $"{FirstName} {LastName}";

    /// <summary>未显式建模的平台字段，原样保留。</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}
