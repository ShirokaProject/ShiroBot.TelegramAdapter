using System.Text;
using ShiroBot.SDK.Models;
using ShiroBot.Telegram.Model;
using Sdk = ShiroBot.SDK.Models;

namespace ShiroBot.TelegramAdapter;

/// <summary>Telegram Bot API 模型 ↔ ShiroBot 核心模型映射。</summary>
public static class TelegramMapper
{
    public const string PlatformId = "telegram";

    /// <summary>由适配器在启动时填充,用于把 file_id 解析为下载 URL。</summary>
    internal static TelegramClient? Client { get; set; }

    internal static string? SelfId { get; set; }

    // ─── Channel / User / Member ───

    public static Sdk.Channel ToChannel(TgChat chat, long? threadId = null) =>
        chat.IsPrivate
            ? Sdk.Channel.Direct(chat.Id.ToString())
            : threadId is null
                ? new Sdk.Channel(chat.Id.ToString(), ChannelType.Group)
                {
                    Name = chat.Title ?? chat.Username,
                    GuildId = chat.Id.ToString()
                }
                : new Sdk.Channel($"{chat.Id}:{threadId}", ChannelType.Thread)
                {
                    Name = chat.Title ?? chat.Username,
                    GuildId = chat.Id.ToString()
                };

    public static Sdk.User ToUser(TgUser user) => new(user.Id.ToString())
    {
        Name = user.Username ?? user.DisplayName,
        IsBot = user.IsBot
    };

    public static Member ToMember(TgChatMember member) => new(ToUser(member.User))
    {
        Nick = member.CustomTitle,
        Role = member.Status switch
        {
            "creator" => MemberRole.Owner,
            "administrator" => MemberRole.Admin,
            _ => MemberRole.Member
        }
    };

    // ─── 入站消息 ───

    public static MessageEvent ToMessageEvent(TgMessage message) => new()
    {
        Platform = PlatformId,
        SelfId = SelfId,
        Raw = message,
        MessageId = message.MessageId.ToString(),
        Channel = ToChannel(message.Chat, message.MessageThreadId),
        Sender = message.From is null
            ? new Sdk.User(message.Chat.Id.ToString()) { Name = message.Chat.Title }
            : ToUser(message.From),
        Segments = ToSegments(message),
        Timestamp = DateTimeOffset.FromUnixTimeSeconds(message.Date)
    };

    public static IReadOnlyList<MessageSegment> ToSegments(TgMessage message)
    {
        var segments = new List<MessageSegment>();

        if (message.ReplyToMessage is { } reply)
        {
            segments.Add(new QuoteSegment(reply.MessageId.ToString()));
        }

        var text = message.Text ?? message.Caption;
        var entities = message.Entities ?? message.CaptionEntities;
        if (!string.IsNullOrEmpty(text))
        {
            segments.AddRange(ParseTextEntities(text, entities));
        }

        if (message.Photo is { Count: > 0 } photos)
        {
            var best = photos[^1]; // 最大尺寸
            segments.Add(new ImageSegment(ResolveFileUrl(best.FileId))
            {
                ResourceId = best.FileId,
                Width = best.Width,
                Height = best.Height
            });
        }

        if (message.Voice is { } voice)
        {
            segments.Add(new AudioSegment(ResolveFileUrl(voice.FileId))
            {
                ResourceId = voice.FileId,
                Duration = TimeSpan.FromSeconds(voice.Duration)
            });
        }

        if (message.Audio is { } audio)
        {
            segments.Add(new AudioSegment(ResolveFileUrl(audio.FileId))
            {
                ResourceId = audio.FileId,
                FileName = audio.FileName,
                Duration = TimeSpan.FromSeconds(audio.Duration)
            });
        }

        if (message.Video is { } video)
        {
            segments.Add(new VideoSegment(ResolveFileUrl(video.FileId))
            {
                ResourceId = video.FileId,
                FileName = video.FileName
            });
        }

        if (message.Document is { } document)
        {
            segments.Add(new Sdk.FileSegment(ResolveFileUrl(document.FileId))
            {
                ResourceId = document.FileId,
                FileName = document.FileName,
                FileSize = document.FileSize
            });
        }

        if (message.Sticker is { } sticker)
        {
            segments.Add(new EmojiSegment(sticker.FileId) { Name = sticker.Emoji });
        }

        return segments;
    }

    /// <summary>按 entities 把文本拆为 Text/Mention 段。</summary>
    private static IEnumerable<MessageSegment> ParseTextEntities(string text, IReadOnlyList<TgMessageEntity>? entities)
    {
        var mentions = entities?
            .Where(entity => entity.Type is "mention" or "text_mention")
            .OrderBy(entity => entity.Offset)
            .ToList();

        if (mentions is null || mentions.Count == 0)
        {
            yield return new TextSegment(text);
            yield break;
        }

        var position = 0;
        foreach (var mention in mentions)
        {
            if (mention.Offset > position)
            {
                yield return new TextSegment(text[position..mention.Offset]);
            }

            var mentionText = text.Substring(mention.Offset, mention.Length);
            yield return mention.User is { } user
                ? new MentionSegment(user.Id.ToString()) { DisplayName = user.DisplayName }
                : new MentionSegment(mentionText.TrimStart('@')) { DisplayName = mentionText };

            position = mention.Offset + mention.Length;
        }

        if (position < text.Length)
        {
            yield return new TextSegment(text[position..]);
        }
    }

    private static string ResolveFileUrl(string fileId) => $"tgfile://{fileId}";

    // ─── 出站 ───

    /// <summary>把段序列渲染为发送计划:文本(带 @ 降级)+ 各媒体附件 + reply id。</summary>
    public static OutgoingPlan ToOutgoingPlan(IReadOnlyList<MessageSegment> segments)
    {
        var text = new StringBuilder();
        string? replyTo = null;
        var attachments = new List<ResourceSegment>();

        foreach (var segment in segments)
        {
            switch (segment)
            {
                case TextSegment t:
                    text.Append(t.Text);
                    break;
                case MentionSegment mention:
                    // Telegram 无法在纯文本里 @ 数字 id,降级为 @username 或显示名
                    text.Append('@').Append(mention.DisplayName?.TrimStart('@') ?? mention.UserId);
                    break;
                case MentionAllSegment:
                    text.Append("@all");
                    break;
                case QuoteSegment quote:
                    replyTo = quote.MessageId;
                    break;
                case EmojiSegment emoji:
                    text.Append(emoji.Name ?? string.Empty);
                    break;
                case ResourceSegment resource:
                    attachments.Add(resource);
                    break;
                case RawSegment raw:
                    throw new NotSupportedException($"Telegram 适配器不支持发送 RawSegment(kind={raw.Kind})。");
            }
        }

        return new OutgoingPlan(text.ToString(), replyTo, attachments);
    }

    public sealed record OutgoingPlan(string Text, string? ReplyToMessageId, IReadOnlyList<ResourceSegment> Attachments);

    // ─── 工具 ───

    /// <summary>Channel.Id 可能是 "chatId" 或 "chatId:threadId"。</summary>
    public static (long ChatId, long? ThreadId) ParseChannelId(string channelId)
    {
        var parts = channelId.Split(':', 2);
        if (!long.TryParse(parts[0], out var chatId))
        {
            throw new ArgumentException($"Telegram 平台的 Channel.Id 必须是数字或 '数字:话题ID',实际为 '{channelId}'。");
        }

        long? threadId = parts.Length == 2 && long.TryParse(parts[1], out var thread) ? thread : null;
        return (chatId, threadId);
    }

    public static long ParseId(string id, string what)
    {
        if (!long.TryParse(id, out var value))
        {
            throw new ArgumentException($"Telegram 平台的 {what} 必须是数字,实际为 '{id}'。");
        }

        return value;
    }
}
