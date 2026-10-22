using System.Text;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.Telegram.Model;
using Sdk = ShiroBot.SDK.Models;

namespace ShiroBot.TelegramAdapter.Services;

public sealed class TelegramMessageService(TelegramClient client, TelegramMapper? mapper = null,
    TelegramInteractionService? interactions = null) : IMessageService, IFileService
{
    private readonly TelegramMapper _mapper = mapper ?? new();
    public MessageCapabilities GetMessageCapabilities(Sdk.Channel channel) => new()
    {
        NativeFeatures = MessageFeatures.Text | MessageFeatures.Markdown | MessageFeatures.Mention | MessageFeatures.Quote | MessageFeatures.Emoji
            | MessageFeatures.Image | MessageFeatures.Audio | MessageFeatures.Video | MessageFeatures.File
            | MessageFeatures.LinkButtons | MessageFeatures.CallbackButtons
            | (interactions is null ? MessageFeatures.None : MessageFeatures.InteractionReply),
        CanMixMarkdown = true, CanCombineButtonsWithMedia = true,
        MaxMediaSegments = 1, MaxTextLength = 4096, MaxButtonRows = 100, MaxButtonsPerRow = 8, MaxCallbackDataBytes = 64
    };

    private (PreparedMessage Prepared, TelegramMapper.OutgoingPlan Plan, object? Keyboard) Prepare(Sdk.Channel channel, OutgoingMessage message)
    {
        TelegramMapper.ParseChannelId(channel.Id);
        if (message.ReplyTo is { } reply && reply.Channel.Id != channel.Id) throw new ArgumentException("引用来源频道不匹配。");
        if (message.ReplyToInteraction is { } interaction && interaction.Channel.Id != channel.Id) throw new ArgumentException("交互来源频道不匹配。");
        var prepared = MessagePreparation.Prepare(message, GetMessageCapabilities(channel));
        if (message.AllowedFallbacks.HasFlag(MessageFallbackOptions.CardAsMarkdown))
        {
            var converted = prepared.Message.Segments.ToArray();
            var offset = message.ReplyTo is null ? 0 : 1;
            for (var index = 0; index < message.Segments.Count; index++)
                if (message.Segments[index] is CardSegment card)
                    converted[index + offset] = new MarkdownSegment(RenderCardMarkdown(card));
            prepared = prepared with { Message = prepared.Message with { Segments = converted } };
        }
        var plan = TelegramMapper.ToOutgoingPlan(prepared.Message.Segments);
        if (plan.Attachments.Count > 1) throw new NotSupportedException("单条 Telegram 消息只支持一个媒体；不会隐式拆分为多条消息。");
        if (plan.Text.Length > (plan.Attachments.Count == 0 ? 4096 : 1024)) throw new NotSupportedException("Telegram 正文或媒体说明超出长度限制。");
        if (plan.ReplyToMessageId is not null) TelegramMapper.ParseId(plan.ReplyToMessageId, "replyToMessageId");
        foreach (var resource in plan.Attachments)
        {
            ValidateResource(resource.Uri, channel);
            if (resource is VideoSegment { ThumbnailUri: { } thumbnail }) ResourceReader.Validate(thumbnail);
        }
        if (plan.Text.Length == 0 && plan.Attachments.Count == 0 && plan.StickerId is null) throw new ArgumentException("没有可发送的内容。");
        var rows = prepared.Message.Buttons?.Rows.Select(row => row.Buttons.Select(button =>
        {
            if (button.Action is OpenUrlAction url) return (object)new { text = button.Label, url = url.Url };
            var value = ButtonCodec.Encode(button.Id, ((CallbackAction)button.Action).Data);
            if (Encoding.UTF8.GetByteCount(value) > 64) throw new NotSupportedException("编码后的按钮 ID 和回调数据超过 Telegram 的 64 字节限制。");
            return new { text = button.Label, callback_data = value };
        }).ToArray()).ToArray();
        if (rows?.Sum(r => r.Length) > 100) throw new NotSupportedException("Telegram 按钮总数超过 100。");
        return (prepared, plan, rows is null ? null : new { inline_keyboard = rows });
    }

    private static string RenderCardMarkdown(CardSegment card)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(card.Title)) lines.Add("*" + TelegramMapper.EscapeMarkdown(card.Title) + "*");
        if (!string.IsNullOrEmpty(card.Description)) lines.Add(TelegramMapper.EscapeMarkdown(card.Description));
        lines.AddRange(card.Fields.Select(field => TelegramMapper.EscapeMarkdown(field.Name + ": " + field.Value)));
        if (card.ImageUrl is not null) lines.Add(TelegramMapper.EscapeMarkdown(card.ImageUrl));
        if (card.Url is not null) lines.Add(TelegramMapper.EscapeMarkdown(card.Url));
        return string.Join("\n\n", lines);
    }

    public MessageSendAssessment AssessMessage(Sdk.Channel channel, OutgoingMessage message)
    {
        try { var (prepared, _, _) = Prepare(channel, message); return new() { IsSupported = true, Transformations = prepared.Transformations }; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return new() { Issues = [ex.Message] }; }
    }
    public Task<SentMessage> SendMessageAsync(Sdk.Channel channel, IReadOnlyList<MessageSegment> segments, CancellationToken cancellationToken = default) =>
        SendMessageAsync(channel, new OutgoingMessage { Segments = segments }, cancellationToken);

    public async Task<SentMessage> SendMessageAsync(Sdk.Channel channel, OutgoingMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (prepared, plan, keyboard) = Prepare(channel, message);
        var (chatId, threadId) = TelegramMapper.ParseChannelId(channel.Id);
        var reply = plan.ReplyToMessageId;
        if (prepared.Message.ReplyToInteraction is { } interaction)
            reply = await (interactions ?? throw new NotSupportedException("交互服务未启动。")).ReplyTargetAsync(interaction, cancellationToken);
        var payload = new Dictionary<string, object?>
        {
            ["chat_id"] = chatId, ["message_thread_id"] = threadId,
            ["reply_parameters"] = reply is null ? null : new { message_id = TelegramMapper.ParseId(reply, "replyToMessageId") },
            ["reply_markup"] = keyboard, ["parse_mode"] = plan.ParseMode
        };
        TgMessage sent;
        if (plan.StickerId is { } sticker)
        {
            payload["sticker"] = sticker.StartsWith("tgfile://", StringComparison.Ordinal) ? sticker[9..] : sticker;
            payload.Remove("parse_mode");
            sent = await client.CallAsync<TgMessage>("sendSticker", payload, cancellationToken);
        }
        else if (plan.Attachments.Count == 1)
        {
            var resource = plan.Attachments[0];
            var (method, field) = resource switch
            {
                ImageSegment => ("sendPhoto", "photo"), AudioSegment => ("sendAudio", "audio"),
                VideoSegment => ("sendVideo", "video"), _ => ("sendDocument", "document")
            };
            payload["caption"] = string.IsNullOrWhiteSpace(plan.Text) ? null : plan.Text;
            var uri = ResolveResource(resource.Uri, channel);
            var thumbnail = resource is VideoSegment { ThumbnailUri: { } thumbnailUri }
                ? await ResourceReader.ReadAsync(thumbnailUri, "thumbnail.jpg", cancellationToken) : null;
            if (thumbnail is not null && uri.StartsWith("tgfile://", StringComparison.Ordinal))
                uri = await GetResourceUrlAsync(uri, cancellationToken);
            if (uri.StartsWith("tgfile://", StringComparison.Ordinal))
            {
                payload[field] = uri[9..];
                sent = await client.CallAsync<TgMessage>(method, payload, cancellationToken);
            }
            else
            {
                var file = await ResourceReader.ReadAsync(uri, resource.FileName, cancellationToken);
                sent = await client.CallMultipartAsync<TgMessage>(method, payload, field, file, cancellationToken, thumbnail);
            }
        }
        else
        {
            payload["text"] = plan.Text;
            sent = await client.CallAsync<TgMessage>("sendMessage", payload, cancellationToken);
        }
        _mapper.ToMessageEvent(sent);
        return new(sent.MessageId.ToString()) { Timestamp = DateTimeOffset.FromUnixTimeSeconds(sent.Date), Transformations = prepared.Transformations };
    }

    public Task DeleteMessageAsync(Sdk.Channel channel, string messageId, CancellationToken cancellationToken = default)
    {
        var (chatId, _) = TelegramMapper.ParseChannelId(channel.Id);
        return client.CallAsync("deleteMessage", new { chat_id = chatId, message_id = TelegramMapper.ParseId(messageId, "messageId") }, cancellationToken);
    }
    public Task<MessageEvent?> GetMessageAsync(Sdk.Channel channel, string messageId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Telegram Bot API 不支持按 ID 读取任意消息；不会用缓存冒充服务器查询。");
    public Task<IReadOnlyList<MessageEvent>> GetHistoryMessagesAsync(Sdk.Channel channel, string? beforeMessageId = null, int limit = 20,
        CancellationToken cancellationToken = default) => throw new NotSupportedException("Telegram Bot API 不提供历史消息查询。");

    public async Task<string> GetResourceUrlAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        if (Uri.TryCreate(resourceId, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        { cancellationToken.ThrowIfCancellationRequested(); return resourceId; }
        if (resourceId.StartsWith("tgupload://", StringComparison.Ordinal))
        {
            if (!_mapper.Uploads.TryGet(resourceId, out var upload)) throw new KeyNotFoundException("上传凭据不属于当前实例或已过期。");
            resourceId = upload.FileId;
        }
        var id = resourceId.StartsWith("tgfile://", StringComparison.Ordinal) ? resourceId[9..] : resourceId;
        var file = await client.CallAsync<TgFile>("getFile", new { file_id = id }, cancellationToken);
        return file.FilePath is null ? throw new InvalidOperationException("Telegram 未返回 file_path。") : client.GetFileDownloadUrl(file.FilePath);
    }

    public FileCapabilities GetFileCapabilities(Sdk.Channel channel) => new() { CanUpload = true, UploadPublishes = true };
    public async Task<FileUploadResult> UploadAsync(Sdk.Channel channel, FileUploadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        var (chatId, threadId) = TelegramMapper.ParseChannelId(channel.Id);
        var payload = new Dictionary<string, object?> { ["chat_id"] = chatId, ["message_thread_id"] = threadId };
        var uri = ResolveResource(request.Uri, channel);
        TgMessage sent;
        if (uri.StartsWith("tgfile://", StringComparison.Ordinal))
        {
            payload["document"] = uri[9..];
            sent = await client.CallAsync<TgMessage>("sendDocument", payload, cancellationToken);
        }
        else sent = await client.CallMultipartAsync<TgMessage>("sendDocument", payload, "document",
            await ResourceReader.ReadAsync(uri, request.FileName, cancellationToken), cancellationToken);
        var fileId = sent.Document?.FileId ?? throw new InvalidOperationException("Telegram 未返回文档上传凭据。");
        var handle = "tgupload://" + Guid.NewGuid().ToString("N");
        _mapper.Uploads.Set(handle, (channel.Id, fileId));
        return new() { FileId = handle, IsPublished = true };
    }
    private void ValidateResource(string uri, Sdk.Channel channel)
    {
        uri = ResolveResource(uri, channel);
        if (uri.StartsWith("tgfile://", StringComparison.Ordinal))
        { ArgumentException.ThrowIfNullOrWhiteSpace(uri[9..]); return; }
        ResourceReader.Validate(uri);
    }
    private string ResolveResource(string uri, Sdk.Channel channel)
    {
        if (!uri.StartsWith("tgupload://", StringComparison.Ordinal)) return uri;
        if (!_mapper.Uploads.TryGet(uri, out var upload) || upload.ChannelId != channel.Id)
            throw new NotSupportedException("上传凭据不属于当前实例和会话，或已过期。");
        return "tgfile://" + upload.FileId;
    }
}
