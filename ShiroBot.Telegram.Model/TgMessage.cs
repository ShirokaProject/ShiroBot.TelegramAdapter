using System.Text.Json.Serialization;

namespace ShiroBot.Telegram.Model;

/// <summary>Telegram Bot API `Message`(常用字段子集)。</summary>
public sealed record TgMessage
{
    [JsonPropertyName("message_id")]
    public required long MessageId { get; init; }

    [JsonPropertyName("message_thread_id")]
    public long? MessageThreadId { get; init; }

    [JsonPropertyName("sender_chat")]
    public TgChat? SenderChat { get; init; }

    [JsonPropertyName("animation")]
    public TgDocument? Animation { get; init; }

    [JsonPropertyName("video_note")]
    public TgVideo? VideoNote { get; init; }

    [JsonPropertyName("from")]
    public TgUser? From { get; init; }

    [JsonPropertyName("chat")]
    public required TgChat Chat { get; init; }

    [JsonPropertyName("date")]
    public long Date { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("caption")]
    public string? Caption { get; init; }

    [JsonPropertyName("entities")]
    public IReadOnlyList<TgMessageEntity>? Entities { get; init; }

    [JsonPropertyName("caption_entities")]
    public IReadOnlyList<TgMessageEntity>? CaptionEntities { get; init; }

    [JsonPropertyName("reply_to_message")]
    public TgMessage? ReplyToMessage { get; init; }

    [JsonPropertyName("photo")]
    public IReadOnlyList<TgPhotoSize>? Photo { get; init; }

    [JsonPropertyName("audio")]
    public TgAudio? Audio { get; init; }

    [JsonPropertyName("voice")]
    public TgVoice? Voice { get; init; }

    [JsonPropertyName("video")]
    public TgVideo? Video { get; init; }

    [JsonPropertyName("document")]
    public TgDocument? Document { get; init; }

    [JsonPropertyName("sticker")]
    public TgSticker? Sticker { get; init; }

    [JsonPropertyName("new_chat_members")]
    public IReadOnlyList<TgUser>? NewChatMembers { get; init; }

    [JsonPropertyName("left_chat_member")]
    public TgUser? LeftChatMember { get; init; }

    /// <summary>未显式建模的平台字段，原样保留。</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}

/// <summary>`MessageEntity` — text 内的标记(mention/bot_command/text_mention 等)。</summary>
public sealed record TgMessageEntity
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("length")]
    public int Length { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("user")]
    public TgUser? User { get; init; }

    /// <summary>未显式建模的平台字段，原样保留。</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}

public sealed record TgPhotoSize
{
    [JsonPropertyName("file_id")]
    public required string FileId { get; init; }

    [JsonPropertyName("file_unique_id")]
    public required string FileUniqueId { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("file_size")]
    public long? FileSize { get; init; }

    /// <summary>未显式建模的平台字段，原样保留。</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}

public sealed record TgAudio
{
    [JsonPropertyName("file_id")]
    public required string FileId { get; init; }

    [JsonPropertyName("duration")]
    public int Duration { get; init; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; init; }

    [JsonPropertyName("file_size")]
    public long? FileSize { get; init; }

    /// <summary>未显式建模的平台字段，原样保留。</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}

public sealed record TgVoice
{
    [JsonPropertyName("file_id")]
    public required string FileId { get; init; }

    [JsonPropertyName("duration")]
    public int Duration { get; init; }

    [JsonPropertyName("file_size")]
    public long? FileSize { get; init; }

    /// <summary>未显式建模的平台字段，原样保留。</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}

public sealed record TgVideo
{
    [JsonPropertyName("file_id")]
    public required string FileId { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("duration")]
    public int Duration { get; init; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; init; }

    [JsonPropertyName("file_size")]
    public long? FileSize { get; init; }

    /// <summary>未显式建模的平台字段，原样保留。</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}

public sealed record TgDocument
{
    [JsonPropertyName("file_id")]
    public required string FileId { get; init; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; init; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; init; }

    [JsonPropertyName("file_size")]
    public long? FileSize { get; init; }

    /// <summary>未显式建模的平台字段，原样保留。</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}

public sealed record TgSticker
{
    [JsonPropertyName("file_id")]
    public required string FileId { get; init; }

    [JsonPropertyName("emoji")]
    public string? Emoji { get; init; }

    [JsonPropertyName("set_name")]
    public string? SetName { get; init; }

    /// <summary>未显式建模的平台字段，原样保留。</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}

/// <summary>getFile 返回的 `File`。</summary>
public sealed record TgFile
{
    [JsonPropertyName("file_id")]
    public required string FileId { get; init; }

    [JsonPropertyName("file_path")]
    public string? FilePath { get; init; }

    [JsonPropertyName("file_size")]
    public long? FileSize { get; init; }

    /// <summary>未显式建模的平台字段，原样保留。</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; init; }
}
