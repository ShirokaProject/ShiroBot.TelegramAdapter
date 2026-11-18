using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShiroBot.Model.Telegram;
using ShiroBot.SDK.Models;
using ShiroBot.TelegramAdapter.BotApi;
using Api = ShiroBot.Telegram.Model.BotApi;
using static ShiroBot.TelegramAdapter.TelegramModelConverter;

namespace ShiroBot.TelegramAdapter.Services;

/// <summary>宿主共享 Telegram Model 扩展接口的实现，插件无需引用本适配器程序集。</summary>
public sealed class TelegramModelApi(TelegramBotApi api, TelegramClient client, TelegramMapper mapper,
    TelegramInteractionService? interactions = null)
    : ITelegramChatApi, ITelegramMessageApi, ITelegramInlineApi, ITelegramPaymentApi, ITelegramBotProfileApi,
        ITelegramRawApi
{
    private static long User(string userId) => TelegramMapper.ParseId(userId, "userId");
    private static long Message(string messageId) => TelegramMapper.ParseId(messageId, "messageId");
    private static Api.ChatId Target(string chatId) => ParseChat(chatId).Chat;

    // ─── ITelegramChatApi ───

    public async Task<TelegramChat> GetChatAsync(string chatId, CancellationToken cancellationToken = default) =>
        Chat(await api.GetChatAsync(Target(chatId), cancellationToken));

    public async Task<int> GetMemberCountAsync(string chatId, CancellationToken cancellationToken = default) =>
        (int)await api.GetChatMemberCountAsync(Target(chatId), cancellationToken);

    public async Task<TelegramChatMember> GetMemberAsync(string chatId, string userId,
        CancellationToken cancellationToken = default)
    {
        var member = await api.GetChatMemberAsync(Target(chatId), User(userId), cancellationToken);
        return Member(member);
    }

    public async Task<IReadOnlyList<TelegramChatMember>> GetAdministratorsAsync(string chatId,
        CancellationToken cancellationToken = default) =>
        (await api.GetChatAdministratorsAsync(Target(chatId), cancellationToken: cancellationToken)).Select(Member).ToArray();

    public Task BanMemberAsync(string chatId, string userId, DateTimeOffset? until = null, bool revokeMessages = false,
        CancellationToken cancellationToken = default) =>
        api.BanChatMemberAsync(Target(chatId), User(userId), UnixTime(until), revokeMessages ? true : null, cancellationToken);

    public Task UnbanMemberAsync(string chatId, string userId, bool onlyIfBanned = true,
        CancellationToken cancellationToken = default) =>
        api.UnbanChatMemberAsync(Target(chatId), User(userId), onlyIfBanned, cancellationToken);

    public Task RestrictMemberAsync(string chatId, string userId, TelegramChatPermissions permissions,
        DateTimeOffset? until = null, CancellationToken cancellationToken = default) =>
        api.RestrictChatMemberAsync(Target(chatId), User(userId), Permissions(permissions), true, UnixTime(until),
            cancellationToken);

    public Task PromoteMemberAsync(string chatId, string userId, TelegramAdminRights rights,
        CancellationToken cancellationToken = default) =>
        api.PromoteChatMemberAsync(Target(chatId), User(userId), rights.IsAnonymous, rights.CanManageChat,
            rights.CanDeleteMessages, rights.CanManageVideoChats, rights.CanRestrictMembers, rights.CanPromoteMembers,
            rights.CanChangeInfo, rights.CanInviteUsers, rights.CanPostStories, rights.CanEditStories,
            rights.CanDeleteStories, rights.CanPostMessages, rights.CanEditMessages, rights.CanPinMessages,
            rights.CanManageTopics, rights.CanManageDirectMessages, cancellationToken: cancellationToken);

    public Task SetAdministratorTitleAsync(string chatId, string userId, string title,
        CancellationToken cancellationToken = default) =>
        api.SetChatAdministratorCustomTitleAsync(Target(chatId), User(userId), title, cancellationToken);

    public Task SetPermissionsAsync(string chatId, TelegramChatPermissions permissions,
        CancellationToken cancellationToken = default) =>
        api.SetChatPermissionsAsync(Target(chatId), Permissions(permissions), true, cancellationToken);

    public Task SetTitleAsync(string chatId, string title, CancellationToken cancellationToken = default) =>
        api.SetChatTitleAsync(Target(chatId), title, cancellationToken);

    public Task SetDescriptionAsync(string chatId, string? description, CancellationToken cancellationToken = default) =>
        api.SetChatDescriptionAsync(Target(chatId), description ?? string.Empty, cancellationToken);

    public async Task SetPhotoAsync(string chatId, string imageUri, CancellationToken cancellationToken = default)
    {
        // setChatPhoto 只接受上传内容，不接受 file_id 或地址。
        var file = await ResourceReader.ReadAsync(imageUri, "photo.jpg", cancellationToken);
        await api.SetChatPhotoAsync(Target(chatId), Api.InputFile.FromBytes(file.Bytes, file.FileName), cancellationToken);
    }

    public Task DeletePhotoAsync(string chatId, CancellationToken cancellationToken = default) =>
        api.DeleteChatPhotoAsync(Target(chatId), cancellationToken);

    public Task PinMessageAsync(string chatId, string messageId, bool disableNotification = false,
        CancellationToken cancellationToken = default) =>
        api.PinChatMessageAsync(Target(chatId), Message(messageId), disableNotification: disableNotification, cancellationToken: cancellationToken);

    public Task UnpinMessageAsync(string chatId, string? messageId = null, CancellationToken cancellationToken = default) =>
        api.UnpinChatMessageAsync(Target(chatId), messageId: messageId is null ? null : Message(messageId),
            cancellationToken: cancellationToken);

    public Task UnpinAllMessagesAsync(string chatId, CancellationToken cancellationToken = default)
    {
        var (chat, thread) = ParseChat(chatId);
        return thread is { } topic
            ? api.UnpinAllForumTopicMessagesAsync(chat, topic, cancellationToken)
            : api.UnpinAllChatMessagesAsync(chat, cancellationToken);
    }

    public Task<string> ExportInviteLinkAsync(string chatId, CancellationToken cancellationToken = default) =>
        api.ExportChatInviteLinkAsync(Target(chatId), cancellationToken);

    public async Task<TelegramInviteLink> CreateInviteLinkAsync(string chatId, TelegramInviteLinkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new TelegramInviteLinkOptions();
        return InviteLink(await api.CreateChatInviteLinkAsync(Target(chatId), options.Name, UnixTime(options.ExpireDate),
            options.MemberLimit, options.CreatesJoinRequest ? true : null, cancellationToken));
    }

    public async Task<TelegramInviteLink> EditInviteLinkAsync(string chatId, string link, TelegramInviteLinkOptions options,
        CancellationToken cancellationToken = default) =>
        InviteLink(await api.EditChatInviteLinkAsync(Target(chatId), link, options.Name, UnixTime(options.ExpireDate),
            options.MemberLimit, options.CreatesJoinRequest, cancellationToken));

    public async Task<TelegramInviteLink> RevokeInviteLinkAsync(string chatId, string link,
        CancellationToken cancellationToken = default) =>
        InviteLink(await api.RevokeChatInviteLinkAsync(Target(chatId), link, cancellationToken));

    public Task ApproveJoinRequestAsync(string chatId, string userId, CancellationToken cancellationToken = default) =>
        api.ApproveChatJoinRequestAsync(Target(chatId), User(userId), cancellationToken);

    public Task DeclineJoinRequestAsync(string chatId, string userId, CancellationToken cancellationToken = default) =>
        api.DeclineChatJoinRequestAsync(Target(chatId), User(userId), cancellationToken);

    public Task LeaveChatAsync(string chatId, CancellationToken cancellationToken = default) =>
        api.LeaveChatAsync(Target(chatId), cancellationToken);

    public async Task<TelegramForumTopic> CreateTopicAsync(string chatId, string name, int? iconColor = null,
        string? iconCustomEmojiId = null, CancellationToken cancellationToken = default) =>
        Topic(await api.CreateForumTopicAsync(Target(chatId), name, iconColor, iconCustomEmojiId, cancellationToken));

    public Task EditTopicAsync(string chatId, long threadId, string? name = null, string? iconCustomEmojiId = null,
        CancellationToken cancellationToken = default) =>
        api.EditForumTopicAsync(Target(chatId), threadId, name, iconCustomEmojiId, cancellationToken);

    public Task CloseTopicAsync(string chatId, long threadId, CancellationToken cancellationToken = default) =>
        api.CloseForumTopicAsync(Target(chatId), threadId, cancellationToken);

    public Task ReopenTopicAsync(string chatId, long threadId, CancellationToken cancellationToken = default) =>
        api.ReopenForumTopicAsync(Target(chatId), threadId, cancellationToken);

    public Task DeleteTopicAsync(string chatId, long threadId, CancellationToken cancellationToken = default) =>
        api.DeleteForumTopicAsync(Target(chatId), threadId, cancellationToken);

    // ─── ITelegramMessageApi ───

    public bool AutoAnswersCallbacks => interactions?.AutoAnswer ?? true;

    public async Task<IReadOnlyList<TelegramSentMessage>> SendAsync(Channel channel, TelegramOutgoingContent content,
        TelegramSendOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new TelegramSendOptions();
        var (chat, thread) = ParseChat(channel.Id);
        var reply = options.ReplyToMessageId is { } replyTo
            ? new Api.ReplyParameters { MessageId = Message(replyTo), AllowSendingWithoutReply = options.AllowSendingWithoutReply }
            : null;
        var markup = ReplyMarkup(options.ReplyMarkup);
        bool? silent = options.DisableNotification ? true : null;
        bool? protect = options.ProtectContent ? true : null;
        bool? paid = options.AllowPaidBroadcast ? true : null;
        var business = options.BusinessConnectionId;
        var effect = options.MessageEffectId;

        Api.Message sent;
        switch (content)
        {
            case TelegramTextContent text:
                sent = await api.SendMessageAsync(text.Text, business, chat, thread, parseMode: ParseMode(text.ParseMode),
                    linkPreviewOptions: text.DisableLinkPreview ? new Api.LinkPreviewOptions { IsDisabled = true } : null,
                    disableNotification: silent, protectContent: protect, allowPaidBroadcast: paid,
                    messageEffectId: effect, replyParameters: reply, replyMarkup: markup,
                    cancellationToken: cancellationToken);
                break;
            case TelegramPollContent poll:
                sent = await api.SendPollAsync(chat, poll.Question,
                    poll.Options.Select(option => new Api.InputPollOption { Text = option }).ToArray(), business, thread,
                    isAnonymous: poll.IsAnonymous, type: poll.Type == TelegramPollType.Quiz ? "quiz" : "regular",
                    allowsMultipleAnswers: poll.AllowsMultipleAnswers ? true : null,
                    correctOptionIds: poll.CorrectOptionIds?.Select(id => (long)id).ToArray(), explanation: poll.Explanation,
                    openPeriod: poll.OpenPeriod is { } period ? (long)period.TotalSeconds : null,
                    closeDate: UnixTime(poll.CloseDate), disableNotification: silent, protectContent: protect,
                    allowPaidBroadcast: paid, messageEffectId: effect, replyParameters: reply, replyMarkup: markup,
                    cancellationToken: cancellationToken);
                break;
            case TelegramLocationContent location:
                sent = await api.SendLocationAsync(location.Latitude, location.Longitude, business, chat, thread,
                    horizontalAccuracy: location.HorizontalAccuracy,
                    livePeriod: location.LivePeriod is { } live ? (long)live.TotalSeconds : null, heading: location.Heading,
                    disableNotification: silent, protectContent: protect, allowPaidBroadcast: paid,
                    messageEffectId: effect, replyParameters: reply, replyMarkup: markup,
                    cancellationToken: cancellationToken);
                break;
            case TelegramVenueContent venue:
                sent = await api.SendVenueAsync(venue.Latitude, venue.Longitude, venue.Title, venue.Address, business, chat,
                    thread, foursquareId: venue.FoursquareId, googlePlaceId: venue.GooglePlaceId,
                    disableNotification: silent, protectContent: protect, allowPaidBroadcast: paid,
                    messageEffectId: effect, replyParameters: reply, replyMarkup: markup,
                    cancellationToken: cancellationToken);
                break;
            case TelegramContactContent contact:
                sent = await api.SendContactAsync(contact.PhoneNumber, contact.FirstName, business, chat, thread,
                    lastName: contact.LastName, vcard: contact.VCard, disableNotification: silent,
                    protectContent: protect, allowPaidBroadcast: paid, messageEffectId: effect,
                    replyParameters: reply, replyMarkup: markup, cancellationToken: cancellationToken);
                break;
            case TelegramDiceContent dice:
                sent = await api.SendDiceAsync(chat, business, thread, emoji: dice.Emoji, disableNotification: silent,
                    protectContent: protect, allowPaidBroadcast: paid, messageEffectId: effect, replyParameters: reply,
                    replyMarkup: markup, cancellationToken: cancellationToken);
                break;
            case TelegramStickerContent sticker:
                sent = await api.SendStickerAsync(await InputFileAsync(sticker.Sticker, "sticker.webp", cancellationToken),
                    business, chat, thread, emoji: sticker.Emoji, disableNotification: silent, protectContent: protect,
                    allowPaidBroadcast: paid, messageEffectId: effect, replyParameters: reply, replyMarkup: markup,
                    cancellationToken: cancellationToken);
                break;
            case TelegramInvoiceContent invoice:
                if (options.ReplyMarkup is not (null or TelegramInlineKeyboard))
                    throw new NotSupportedException("发票只能附带内联键盘。");
                sent = await api.SendInvoiceAsync(chat, invoice.Title, invoice.Description, invoice.Payload, invoice.Currency,
                    invoice.Prices.Select(price => new Api.LabeledPrice { Label = price.Label, Amount = price.Amount }).ToArray(),
                    thread, providerToken: invoice.ProviderToken, photoUrl: invoice.PhotoUrl,
                    needName: invoice.NeedName ? true : null, needPhoneNumber: invoice.NeedPhoneNumber ? true : null,
                    needEmail: invoice.NeedEmail ? true : null,
                    needShippingAddress: invoice.NeedShippingAddress ? true : null,
                    isFlexible: invoice.IsFlexible ? true : null, disableNotification: silent, protectContent: protect,
                    allowPaidBroadcast: paid, messageEffectId: effect, replyParameters: reply,
                    replyMarkup: InlineKeyboard(options.ReplyMarkup as TelegramInlineKeyboard),
                    cancellationToken: cancellationToken);
                break;
            case TelegramMediaGroupContent group:
                if (options.ReplyMarkup is not null) throw new NotSupportedException("Telegram 相册不能附带键盘。");
                if (group.Items.Count is < 2 or > 10) throw new ArgumentException("相册需要 2–10 项。");
                var media = new List<Api.InputMedia>();
                foreach (var item in group.Items)
                    media.Add(await MediaAsync(item, cancellationToken));
                var album = await api.SendMediaGroupAsync(chat, media, business, thread, disableNotification: silent,
                    protectContent: protect, allowPaidBroadcast: paid, messageEffectId: effect, replyParameters: reply,
                    cancellationToken: cancellationToken);
                return album.Select(Sent).ToArray();
            default:
                throw new NotSupportedException("Telegram 不支持发送 " + content.GetType().Name + "。");
        }

        return [Sent(sent)];
    }

    private static async Task<Api.InputMedia> MediaAsync(TelegramMediaItem item, CancellationToken token)
    {
        var file = await InputFileAsync(item.Uri, item.FileName, token);
        var parseMode = ParseMode(item.ParseMode);
        bool? spoiler = item.HasSpoiler ? true : null;
        return item.Kind switch
        {
            TelegramMediaKind.Photo => new Api.InputMediaPhoto { Media = file, Caption = item.Caption, ParseMode = parseMode, HasSpoiler = spoiler },
            TelegramMediaKind.Video => new Api.InputMediaVideo { Media = file, Caption = item.Caption, ParseMode = parseMode, HasSpoiler = spoiler },
            TelegramMediaKind.Audio => new Api.InputMediaAudio { Media = file, Caption = item.Caption, ParseMode = parseMode },
            TelegramMediaKind.Document => new Api.InputMediaDocument { Media = file, Caption = item.Caption, ParseMode = parseMode },
            _ => throw new NotSupportedException("未知的相册媒体类型：" + item.Kind)
        };
    }

    public Task EditTextAsync(Channel channel, string messageId, string text,
        TelegramParseMode parseMode = TelegramParseMode.None, TelegramInlineKeyboard? replyMarkup = null,
        CancellationToken cancellationToken = default) =>
        api.EditMessageTextAsync(chatId: Target(channel.Id), messageId: Message(messageId), text: text,
            parseMode: ParseMode(parseMode), replyMarkup: InlineKeyboard(replyMarkup), cancellationToken: cancellationToken);

    public Task EditCaptionAsync(Channel channel, string messageId, string? caption,
        TelegramParseMode parseMode = TelegramParseMode.None, TelegramInlineKeyboard? replyMarkup = null,
        CancellationToken cancellationToken = default) =>
        api.EditMessageCaptionAsync(chatId: Target(channel.Id), messageId: Message(messageId), caption: caption ?? string.Empty,
            parseMode: ParseMode(parseMode), replyMarkup: InlineKeyboard(replyMarkup), cancellationToken: cancellationToken);

    public Task EditReplyMarkupAsync(Channel channel, string messageId, TelegramInlineKeyboard? replyMarkup,
        CancellationToken cancellationToken = default) =>
        api.EditMessageReplyMarkupAsync(chatId: Target(channel.Id), messageId: Message(messageId),
            replyMarkup: InlineKeyboard(replyMarkup), cancellationToken: cancellationToken);

    public Task EditInlineTextAsync(string inlineMessageId, string text,
        TelegramParseMode parseMode = TelegramParseMode.None, TelegramInlineKeyboard? replyMarkup = null,
        CancellationToken cancellationToken = default) =>
        api.EditMessageTextAsync(inlineMessageId: inlineMessageId, text: text, parseMode: ParseMode(parseMode),
            replyMarkup: InlineKeyboard(replyMarkup), cancellationToken: cancellationToken);

    public async Task<TelegramSentMessage> ForwardAsync(Channel target, Channel source, string messageId,
        bool disableNotification = false, CancellationToken cancellationToken = default)
    {
        var (chat, thread) = ParseChat(target.Id);
        return Sent(await api.ForwardMessageAsync(chat, Target(source.Id), Message(messageId), thread,
            disableNotification: disableNotification ? true : null, cancellationToken: cancellationToken));
    }

    public async Task<string> CopyAsync(Channel target, Channel source, string messageId, TelegramSendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new TelegramSendOptions();
        var (chat, thread) = ParseChat(target.Id);
        var copied = await api.CopyMessageAsync(chat, Target(source.Id), Message(messageId), thread,
            disableNotification: options.DisableNotification ? true : null,
            protectContent: options.ProtectContent ? true : null,
            replyParameters: options.ReplyToMessageId is { } reply
                ? new Api.ReplyParameters { MessageId = Message(reply), AllowSendingWithoutReply = options.AllowSendingWithoutReply }
                : null,
            replyMarkup: ReplyMarkup(options.ReplyMarkup), cancellationToken: cancellationToken);
        return copied.MessageIdValue.ToString(CultureInfo.InvariantCulture);
    }

    public Task DeleteMessagesAsync(Channel channel, IReadOnlyList<string> messageIds,
        CancellationToken cancellationToken = default)
    {
        if (messageIds.Count is < 1 or > 100) throw new ArgumentException("一次删除 1–100 条消息。", nameof(messageIds));
        return api.DeleteMessagesAsync(Target(channel.Id), messageIds.Select(Message).ToArray(), cancellationToken);
    }

    public Task SendChatActionAsync(Channel channel, TelegramChatAction action, CancellationToken cancellationToken = default)
    {
        var (chat, thread) = ParseChat(channel.Id);
        return api.SendChatActionAsync(chat, ChatAction(action), messageThreadId: thread, cancellationToken: cancellationToken);
    }

    public async Task<TelegramPoll> StopPollAsync(Channel channel, string messageId, CancellationToken cancellationToken = default) =>
        Poll(await api.StopPollAsync(Target(channel.Id), Message(messageId), cancellationToken: cancellationToken));

    public Task AnswerCallbackAsync(string interactionId, TelegramCallbackAnswer answer,
        CancellationToken cancellationToken = default) =>
        (interactions ?? throw new NotSupportedException("交互服务未启动。")).AnswerAsync(interactionId, answer, cancellationToken);

    // ─── ITelegramInlineApi ───

    public Task AnswerInlineQueryAsync(string queryId, IReadOnlyList<TelegramInlineResult> results,
        TelegramInlineAnswerOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (results.Count > 50) throw new ArgumentException("inline 查询最多返回 50 项结果。", nameof(results));
        options ??= new TelegramInlineAnswerOptions();
        return api.AnswerInlineQueryAsync(queryId, results.Select(InlineResult).ToArray(),
            options.CacheTime is { } cache ? (long)cache.TotalSeconds : null, options.IsPersonal ? true : null,
            options.NextOffset, cancellationToken: cancellationToken);
    }

    private static Api.InlineQueryResult InlineResult(TelegramInlineResult result) => result switch
    {
        TelegramInlineArticle article => new Api.InlineQueryResultArticle
        {
            Id = article.Id, Title = article.Title,
            InputMessageContent = new Api.InputTextMessageContent
                { MessageText = article.MessageText, ParseMode = ParseMode(article.ParseMode) },
            Description = article.Description, Url = article.Url, ThumbnailUrl = article.ThumbnailUrl,
            ReplyMarkup = InlineKeyboard(article.ReplyMarkup)
        },
        TelegramInlinePhoto photo when Uri.TryCreate(photo.Photo, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" =>
            new Api.InlineQueryResultPhoto
            {
                Id = photo.Id, PhotoUrl = photo.Photo, ThumbnailUrl = photo.ThumbnailUrl ?? photo.Photo,
                Title = photo.Title, Description = photo.Description, Caption = photo.Caption,
                ParseMode = ParseMode(photo.ParseMode), ReplyMarkup = InlineKeyboard(photo.ReplyMarkup)
            },
        TelegramInlinePhoto photo => new Api.InlineQueryResultCachedPhoto
        {
            Id = photo.Id, PhotoFileId = photo.Photo.StartsWith("tgfile://", StringComparison.Ordinal) ? photo.Photo[9..] : photo.Photo,
            Title = photo.Title, Description = photo.Description, Caption = photo.Caption,
            ParseMode = ParseMode(photo.ParseMode), ReplyMarkup = InlineKeyboard(photo.ReplyMarkup)
        },
        TelegramInlineSticker sticker => new Api.InlineQueryResultCachedSticker
        {
            Id = sticker.Id, StickerFileId = sticker.StickerFileId, ReplyMarkup = InlineKeyboard(sticker.ReplyMarkup)
        },
        _ => throw new NotSupportedException("未知的 inline 结果类型：" + result.GetType().Name)
    };

    // ─── ITelegramPaymentApi ───

    public Task AnswerPreCheckoutQueryAsync(string queryId, bool ok, string? errorMessage = null,
        CancellationToken cancellationToken = default) =>
        api.AnswerPreCheckoutQueryAsync(queryId, ok, ok ? null : errorMessage ?? "无法完成付款。", cancellationToken);

    public Task AnswerShippingQueryAsync(string queryId, bool ok, IReadOnlyList<TelegramShippingOption>? options = null,
        string? errorMessage = null, CancellationToken cancellationToken = default) =>
        api.AnswerShippingQueryAsync(queryId, ok,
            ok ? (options ?? throw new ArgumentException("确认收货地址时需要提供收货方式。", nameof(options))).Select(option =>
                new Api.ShippingOption
                {
                    Id = option.Id, Title = option.Title,
                    Prices = option.Prices.Select(price => new Api.LabeledPrice { Label = price.Label, Amount = price.Amount }).ToArray()
                }).ToArray() : null,
            ok ? null : errorMessage ?? "无法配送到该地址。", cancellationToken);

    public Task RefundStarPaymentAsync(string userId, string telegramPaymentChargeId,
        CancellationToken cancellationToken = default) =>
        api.RefundStarPaymentAsync(User(userId), telegramPaymentChargeId, cancellationToken);

    public async Task<long> GetStarBalanceAsync(CancellationToken cancellationToken = default) =>
        (await api.GetMyStarBalanceAsync(cancellationToken)).Amount;

    // ─── ITelegramBotProfileApi ───

    public async Task<TelegramUser> GetMeAsync(CancellationToken cancellationToken = default) =>
        TelegramModelConverter.User(await api.GetMeAsync(cancellationToken));

    public TelegramUser? FindKnownUser(string userId) =>
        mapper.Users.TryGet(userId, out var user) ? TelegramMapper.ToPlatformUser(user) : null;

    public Task SetCommandsAsync(IReadOnlyList<TelegramBotCommand> commands, TelegramCommandScope? scope = null,
        string? languageCode = null, CancellationToken cancellationToken = default) =>
        api.SetMyCommandsAsync(commands.Select(command => new Api.BotCommand
            { Command = command.Command, Description = command.Description }).ToArray(), Scope(scope), languageCode,
            cancellationToken);

    public async Task<IReadOnlyList<TelegramBotCommand>> GetCommandsAsync(TelegramCommandScope? scope = null,
        string? languageCode = null, CancellationToken cancellationToken = default) =>
        (await api.GetMyCommandsAsync(Scope(scope), languageCode, cancellationToken))
        .Select(command => new TelegramBotCommand(command.Command, command.Description)).ToArray();

    public Task DeleteCommandsAsync(TelegramCommandScope? scope = null, string? languageCode = null,
        CancellationToken cancellationToken = default) =>
        api.DeleteMyCommandsAsync(Scope(scope), languageCode, cancellationToken);

    public Task SetBotNameAsync(string? name, string? languageCode = null, CancellationToken cancellationToken = default) =>
        api.SetMyNameAsync(name, languageCode, cancellationToken);

    public Task SetBotDescriptionAsync(string? description, string? languageCode = null,
        CancellationToken cancellationToken = default) =>
        api.SetMyDescriptionAsync(description, languageCode, cancellationToken);

    public Task SetBotShortDescriptionAsync(string? description, string? languageCode = null,
        CancellationToken cancellationToken = default) =>
        api.SetMyShortDescriptionAsync(description, languageCode, cancellationToken);

    // ─── ITelegramRawApi ───

    public string BotApiVersion => Api.BotApiInfo.Version;

    public Task<JsonElement> CallAsync(string method, JsonObject? parameters = null,
        IReadOnlyList<TelegramUploadFile>? files = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        var arguments = new List<KeyValuePair<string, object?>>();
        if (parameters is not null)
            foreach (var (name, value) in parameters)
                arguments.Add(new(name, value?.DeepClone()));
        if (files is not null)
            foreach (var file in files)
                arguments.Add(new(file.Name, Api.InputFile.FromStream(file.Content, file.FileName)));
        return client.InvokeAsync(method, arguments, cancellationToken);
    }

    private Func<JsonElement, Task>? _rawUpdates;

    public event Func<JsonElement, Task>? UpdateReceived
    {
        add
        {
            if (_rawUpdates is null) client.RawUpdateReceived += OnRawUpdateAsync;
            _rawUpdates += value;
        }
        remove
        {
            _rawUpdates -= value;
            if (_rawUpdates is null) client.RawUpdateReceived -= OnRawUpdateAsync;
        }
    }

    private async Task OnRawUpdateAsync(JsonElement update)
    {
        if (_rawUpdates is not { } handlers) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<JsonElement, Task>>())
            await handler(update).ConfigureAwait(false);
    }

    internal void Detach()
    {
        if (_rawUpdates is not null) client.RawUpdateReceived -= OnRawUpdateAsync;
        _rawUpdates = null;
    }
}
