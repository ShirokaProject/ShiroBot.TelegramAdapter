using System.Text;
using ShiroBot.SDK.Models;
using ShiroBot.Telegram.Model;
using Sdk = ShiroBot.SDK.Models;

namespace ShiroBot.TelegramAdapter;

/// <summary>Telegram Bot API 模型 ↔ ShiroBot 核心模型映射。</summary>
public sealed class TelegramMapper
{
    public const string PlatformId = "telegram";

    internal string? SelfId { get; set; }
    internal BoundedCache<TgUser> Users { get; } = new();
    internal BoundedCache<TgChat> Chats { get; } = new();
    internal BoundedCache<(string ChannelId, string FileId)> Uploads { get; } = new();
    public static ShiroBot.Model.Telegram.TelegramUser ToPlatformUser(TgUser user) => new()
    {
        UserId = user.Id, FirstName = user.FirstName, LastName = user.LastName,
        Username = user.Username, IsBot = user.IsBot
    };

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

    public MessageEvent ToMessageEvent(TgMessage message)
    {
        Chats.Set(message.Chat.Id.ToString(), message.Chat);
        if (message.From is { } from) Users.Set(from.Id.ToString(), from);
        return new MessageEvent
        {
            Platform = PlatformId, SelfId = SelfId, Raw = message,
            MessageId = message.MessageId.ToString(), Channel = ToChannel(message.Chat, message.MessageThreadId),
            Sender = message.SenderChat is { } senderChat
                ? new Sdk.User(senderChat.Id.ToString()) { Name = senderChat.Title ?? senderChat.Username }
                : message.From is { } user ? ToUser(user)
                    : new Sdk.User(message.Chat.Id.ToString()) { Name = message.Chat.Title },
            Segments = ToSegments(message), Timestamp = DateTimeOffset.FromUnixTimeSeconds(message.Date)
        };
    }

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

        if (message.Animation is { } animation)
            segments.Add(new Sdk.FileSegment(ResolveFileUrl(animation.FileId))
                { ResourceId = animation.FileId, FileName = animation.FileName, FileSize = animation.FileSize });
        if (message.VideoNote is { } note)
            segments.Add(new VideoSegment(ResolveFileUrl(note.FileId)) { ResourceId = note.FileId });

        if (message.Sticker is { } sticker)
        {
            segments.Add(new EmojiSegment("tgfile://" + sticker.FileId) { Name = sticker.Emoji });
        }

        if (message.AdditionalData is { } unknown)
            foreach (var (kind, value) in unknown)
                segments.Add(new RawSegment(PlatformId, kind, value.Clone()));
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

    public static OutgoingPlan ToOutgoingPlan(IReadOnlyList<MessageSegment> segments)
    {
        var markdown = segments.Any(s => s is MarkdownSegment || s is MentionSegment m && long.TryParse(m.UserId, out _));
        var text = new StringBuilder();
        string? replyTo = null;
        string? sticker = null;
        var attachments = new List<ResourceSegment>();
        string Plain(string value) => markdown ? EscapeMarkdown(value) : value;
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case TextSegment t: text.Append(Plain(t.Text)); break;
                case MarkdownSegment m: text.Append(m.Content); break;
                case MentionSegment mention when long.TryParse(mention.UserId, out var userId):
                    text.Append('[').Append(EscapeMarkdown(mention.DisplayName ?? mention.UserId))
                        .Append("](tg://user?id=").Append(userId).Append(')');
                    break;
                case MentionSegment mention:
                    text.Append(Plain("@" + mention.UserId.TrimStart('@'))); break;
                case QuoteSegment quote: replyTo = quote.MessageId; break;
                case EmojiSegment emoji when emoji.Id.StartsWith("tgfile://", StringComparison.Ordinal):
                    if (sticker is not null) throw new NotSupportedException("每条消息只能发送一个贴纸。");
                    sticker = emoji.Id[9..]; break;
                case EmojiSegment emoji when !string.IsNullOrEmpty(emoji.Name): text.Append(Plain(emoji.Name)); break;
                case EmojiSegment emoji when emoji.Id.Any(c => c > 127): text.Append(Plain(emoji.Id)); break;
                case EmojiSegment emoji:
                    if (sticker is not null) throw new NotSupportedException("每条消息只能发送一个贴纸。");
                    sticker = emoji.Id; break;
                case ResourceSegment resource: attachments.Add(resource); break;
                default: throw new NotSupportedException($"Telegram 不支持原生发送 {segment.GetType().Name}。");
            }
        }
        if (sticker is not null && (text.Length != 0 || attachments.Count != 0))
            throw new NotSupportedException("Telegram 贴纸不能与正文或媒体隐式拆分发送。");
        return new(text.ToString(), replyTo, attachments, markdown ? "MarkdownV2" : null, sticker);
    }

    internal static string EscapeMarkdown(string value) => string.Concat(value.Select(c =>
        "\\_*[]()~`>#+-=|{}.!".Contains(c) ? "\\" + c : c.ToString()));

    public sealed record OutgoingPlan(string Text, string? ReplyToMessageId,
        IReadOnlyList<ResourceSegment> Attachments, string? ParseMode = null, string? StickerId = null);

    // ─── 工具 ───

    /// <summary>Channel.Id 可能是 "chatId" 或 "chatId:threadId"。</summary>
    public static (long ChatId, long? ThreadId) ParseChannelId(string channelId)
    {
        var parts = channelId.Split(':', 2);
        if (!long.TryParse(parts[0], out var chatId))
        {
            throw new ArgumentException($"Telegram 平台的 Channel.Id 必须是数字或 '数字:话题ID',实际为 '{channelId}'。");
        }

        long? threadId = null;
        if (parts.Length == 2)
        {
            if (!long.TryParse(parts[1], out var thread) || thread <= 0)
                throw new ArgumentException("Telegram 话题 ID 必须是正整数。", nameof(channelId));
            threadId = thread;
        }
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
