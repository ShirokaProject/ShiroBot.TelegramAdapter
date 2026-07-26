using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.Telegram.Model;
using Sdk = ShiroBot.SDK.Models;

namespace ShiroBot.TelegramAdapter.Services;

public sealed class TelegramMessageService(TelegramClient client) : IMessageService
{
    public async Task<SentMessage> SendMessageAsync(Sdk.Channel channel, IReadOnlyList<MessageSegment> segments)
    {
        var (chatId, threadId) = TelegramMapper.ParseChannelId(channel.Id);
        var plan = TelegramMapper.ToOutgoingPlan(segments);
        TgMessage? sent = null;

        // 附件逐个发送;第一个附件携带文本作为 caption
        var caption = plan.Text;
        foreach (var attachment in plan.Attachments)
        {
            sent = await SendAttachmentAsync(chatId, threadId, attachment, caption, plan.ReplyToMessageId);
            caption = string.Empty;
        }

        if (plan.Attachments.Count == 0 || !string.IsNullOrWhiteSpace(caption) && plan.Attachments.Count > 1)
        {
            if (!string.IsNullOrWhiteSpace(caption) || plan.Attachments.Count == 0)
            {
                sent = await client.CallAsync<TgMessage>("sendMessage", new
                {
                    chat_id = chatId,
                    message_thread_id = threadId,
                    text = string.IsNullOrWhiteSpace(caption) ? plan.Text : caption,
                    reply_to_message_id = ParseNullableId(plan.ReplyToMessageId)
                });
            }
        }

        return sent is null
            ? throw new InvalidOperationException("没有可发送的内容。")
            : new SentMessage(sent.MessageId.ToString())
            {
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(sent.Date)
            };
    }

    public Task DeleteMessageAsync(Sdk.Channel channel, string messageId)
    {
        var (chatId, _) = TelegramMapper.ParseChannelId(channel.Id);
        return client.CallAsync("deleteMessage", new
        {
            chat_id = chatId,
            message_id = TelegramMapper.ParseId(messageId, "messageId")
        });
    }

    public async Task<string> GetResourceUrlAsync(string resourceId)
    {
        var fileId = resourceId.StartsWith("tgfile://", StringComparison.Ordinal)
            ? resourceId["tgfile://".Length..]
            : resourceId;
        var file = await client.CallAsync<TgFile>("getFile", new { file_id = fileId });
        return file.FilePath is null
            ? throw new InvalidOperationException($"Telegram getFile 未返回 file_path: {fileId}")
            : client.GetFileDownloadUrl(file.FilePath);
    }

    private async Task<TgMessage> SendAttachmentAsync(
        long chatId,
        long? threadId,
        ResourceSegment attachment,
        string caption,
        string? replyToMessageId)
    {
        var (method, field) = attachment switch
        {
            ImageSegment => ("sendPhoto", "photo"),
            AudioSegment => ("sendAudio", "audio"),
            VideoSegment => ("sendVideo", "video"),
            _ => ("sendDocument", "document")
        };

        var payload = new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["message_thread_id"] = threadId,
            [field] = await ResolveOutgoingUriAsync(attachment),
            ["caption"] = string.IsNullOrWhiteSpace(caption) ? null : caption,
            ["reply_to_message_id"] = ParseNullableId(replyToMessageId)
        };

        return await client.CallAsync<TgMessage>(method, payload);
    }

    /// <summary>tgfile:// 直接回传 file_id;http(s) 原样;其他形式暂不支持。</summary>
    private Task<string> ResolveOutgoingUriAsync(ResourceSegment resource)
    {
        if (resource.Uri.StartsWith("tgfile://", StringComparison.Ordinal))
        {
            return Task.FromResult(resource.Uri["tgfile://".Length..]);
        }

        if (resource.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            resource.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(resource.Uri);
        }

        throw new NotSupportedException(
            $"Telegram 适配器暂只支持 http(s) URL 或 tgfile://file_id 形式的资源,实际为 '{resource.Uri}'。");
    }

    private static long? ParseNullableId(string? id) =>
        id is null ? null : TelegramMapper.ParseId(id, "replyToMessageId");
}
