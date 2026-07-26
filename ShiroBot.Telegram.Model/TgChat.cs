using System.Text.Json.Serialization;

namespace ShiroBot.Telegram.Model;

/// <summary>Telegram Bot API `Chat`。type: private / group / supergroup / channel。</summary>
public sealed record TgChat
{
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    [JsonPropertyName("is_forum")]
    public bool IsForum { get; init; }

    [JsonIgnore]
    public bool IsPrivate => Type == "private";
}

/// <summary>Telegram Bot API `ChatMember`(简化,涵盖各 status 的公共字段)。</summary>
public sealed record TgChatMember
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("user")]
    public required TgUser User { get; init; }

    [JsonPropertyName("custom_title")]
    public string? CustomTitle { get; init; }

    [JsonPropertyName("until_date")]
    public long? UntilDate { get; init; }
}
