using System.Text.Json.Serialization;

namespace ShiroBot.Telegram.Model;

/// <summary>Telegram Bot API `Update`(getUpdates 长轮询返回项)。</summary>
public sealed record TgUpdate
{
    [JsonPropertyName("update_id")]
    public required long UpdateId { get; init; }

    [JsonPropertyName("edited_channel_post")]
    public TgMessage? EditedChannelPost { get; init; }

    [JsonPropertyName("message_reaction")]
    public TgMessageReactionUpdated? MessageReaction { get; init; }

    [JsonPropertyName("message_reaction_count")]
    public TgMessageReactionCountUpdated? MessageReactionCount { get; init; }

    [JsonPropertyName("chat_join_request")]
    public TgChatJoinRequest? ChatJoinRequest { get; init; }

    [JsonPropertyName("message")]
    public TgMessage? Message { get; init; }

    [JsonPropertyName("edited_message")]
    public TgMessage? EditedMessage { get; init; }

    [JsonPropertyName("channel_post")]
    public TgMessage? ChannelPost { get; init; }

    [JsonPropertyName("callback_query")]
    public TgCallbackQuery? CallbackQuery { get; init; }

    [JsonPropertyName("my_chat_member")]
    public TgChatMemberUpdated? MyChatMember { get; init; }

    [JsonPropertyName("chat_member")]
    public TgChatMemberUpdated? ChatMember { get; init; }

    /// <summary>未显式建模的平台字段，原样保留。</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}

/// <summary>`CallbackQuery` — inline keyboard 按钮回调。</summary>
public sealed record TgCallbackQuery
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("from")]
    public required TgUser From { get; init; }

    [JsonPropertyName("message")]
    public TgMessage? Message { get; init; }

    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>未显式建模的平台字段，原样保留。</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}

/// <summary>`ChatMemberUpdated` — 成员状态变化。</summary>
public sealed record TgChatMemberUpdated
{
    [JsonPropertyName("chat")]
    public required TgChat Chat { get; init; }

    [JsonPropertyName("from")]
    public required TgUser From { get; init; }

    [JsonPropertyName("date")]
    public long Date { get; init; }

    [JsonPropertyName("old_chat_member")]
    public required TgChatMember OldChatMember { get; init; }

    [JsonPropertyName("new_chat_member")]
    public required TgChatMember NewChatMember { get; init; }

    /// <summary>未显式建模的平台字段，原样保留。</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}

/// <summary>Bot API 响应包装:{ ok, result, description, error_code }。</summary>
public sealed record TgResponse<T>
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("result")]
    public T? Result { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; init; }

    /// <summary>未显式建模的平台字段，原样保留。</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}

public sealed record TgReactionType
{
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("emoji")] public string? Emoji { get; init; }
    [JsonPropertyName("custom_emoji_id")] public string? CustomEmojiId { get; init; }
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}
public sealed record TgMessageReactionUpdated
{
    [JsonPropertyName("chat")] public required TgChat Chat { get; init; }
    [JsonPropertyName("message_id")] public long MessageId { get; init; }
    [JsonPropertyName("date")] public long Date { get; init; }
    [JsonPropertyName("user")] public TgUser? User { get; init; }
    [JsonPropertyName("actor_chat")] public TgChat? ActorChat { get; init; }
    [JsonPropertyName("old_reaction")] public IReadOnlyList<TgReactionType> OldReaction { get; init; } = [];
    [JsonPropertyName("new_reaction")] public IReadOnlyList<TgReactionType> NewReaction { get; init; } = [];
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}
public sealed record TgReactionCount
{
    [JsonPropertyName("type")] public required TgReactionType Type { get; init; }
    [JsonPropertyName("total_count")] public int TotalCount { get; init; }
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}
public sealed record TgMessageReactionCountUpdated
{
    [JsonPropertyName("chat")] public required TgChat Chat { get; init; }
    [JsonPropertyName("message_id")] public long MessageId { get; init; }
    [JsonPropertyName("date")] public long Date { get; init; }
    [JsonPropertyName("reactions")] public IReadOnlyList<TgReactionCount> Reactions { get; init; } = [];
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}
public sealed record TgChatJoinRequest
{
    [JsonPropertyName("chat")] public required TgChat Chat { get; init; }
    [JsonPropertyName("from")] public required TgUser From { get; init; }
    [JsonPropertyName("date")] public long Date { get; init; }
    [JsonPropertyName("bio")] public string? Bio { get; init; }
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}
