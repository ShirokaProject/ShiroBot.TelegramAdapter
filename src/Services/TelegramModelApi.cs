using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShiroBot.Model.Telegram;
using ShiroBot.SDK.Models;
using static ShiroBot.TelegramAdapter.TelegramModelConverter;

namespace ShiroBot.TelegramAdapter.Services;

/// <summary>宿主共享 Telegram Model 扩展接口的实现，插件无需引用本适配器程序集。</summary>
public sealed class TelegramModelApi(TelegramClient client, TelegramMapper mapper, TelegramInteractionService? interactions = null)
    : ITelegramChatApi, ITelegramMessageApi, ITelegramInlineApi, ITelegramPaymentApi, ITelegramBotProfileApi, ITelegramRawApi
{
    private static long User(string userId) => TelegramMapper.ParseId(userId, "userId");
    private static long Message(string messageId) => TelegramMapper.ParseId(messageId, "messageId");
    private static JsonNode Target(string chatId) => ParseChat(chatId).Chat;

    private Task<JsonElement> Call(string method, JsonObject parameters, CancellationToken token,
        IReadOnlyList<TelegramUpload>? uploads = null) =>
        client.InvokeAsync(method, parameters, uploads, token);

    private Task<JsonElement> ChatCall(string method, string chatId, CancellationToken token, JsonObject? extra = null)
    {
        var parameters = extra ?? new JsonObject();
        parameters["chat_id"] = Target(chatId);
        return Call(method, parameters, token);
    }

    // ─── ITelegramChatApi ───

    public async Task<TelegramChat> GetChatAsync(string chatId, CancellationToken cancellationToken = default) =>
        Chat(await ChatCall("getChat", chatId, cancellationToken));

    public async Task<int> GetMemberCountAsync(string chatId, CancellationToken cancellationToken = default) =>
        (await ChatCall("getChatMemberCount", chatId, cancellationToken)).GetInt32();

    public async Task<TelegramChatMember> GetMemberAsync(string chatId, string userId, CancellationToken cancellationToken = default) =>
        Member(await ChatCall("getChatMember", chatId, cancellationToken, new JsonObject { ["user_id"] = User(userId) }));

    public async Task<IReadOnlyList<TelegramChatMember>> GetAdministratorsAsync(string chatId,
        CancellationToken cancellationToken = default) =>
        (await ChatCall("getChatAdministrators", chatId, cancellationToken)).EnumerateArray().Select(Member).ToArray();

    public Task BanMemberAsync(string chatId, string userId, DateTimeOffset? until = null, bool revokeMessages = false,
        CancellationToken cancellationToken = default) =>
        ChatCall("banChatMember", chatId, cancellationToken, new JsonObject
        {
            ["user_id"] = User(userId), ["until_date"] = UnixTime(until), ["revoke_messages"] = revokeMessages ? true : null
        });

    public Task UnbanMemberAsync(string chatId, string userId, bool onlyIfBanned = true, CancellationToken cancellationToken = default) =>
        ChatCall("unbanChatMember", chatId, cancellationToken, new JsonObject { ["user_id"] = User(userId), ["only_if_banned"] = onlyIfBanned });

    public Task RestrictMemberAsync(string chatId, string userId, TelegramChatPermissions permissions, DateTimeOffset? until = null,
        CancellationToken cancellationToken = default) =>
        ChatCall("restrictChatMember", chatId, cancellationToken, new JsonObject
        {
            ["user_id"] = User(userId), ["permissions"] = PermissionsJson(permissions), ["use_independent_chat_permissions"] = true,
            ["until_date"] = UnixTime(until)
        });

    public Task PromoteMemberAsync(string chatId, string userId, TelegramAdminRights rights, CancellationToken cancellationToken = default) =>
        ChatCall("promoteChatMember", chatId, cancellationToken, new JsonObject
        {
            ["user_id"] = User(userId), ["is_anonymous"] = rights.IsAnonymous, ["can_manage_chat"] = rights.CanManageChat,
            ["can_delete_messages"] = rights.CanDeleteMessages, ["can_manage_video_chats"] = rights.CanManageVideoChats,
            ["can_restrict_members"] = rights.CanRestrictMembers, ["can_promote_members"] = rights.CanPromoteMembers,
            ["can_change_info"] = rights.CanChangeInfo, ["can_invite_users"] = rights.CanInviteUsers,
            ["can_post_stories"] = rights.CanPostStories, ["can_edit_stories"] = rights.CanEditStories,
            ["can_delete_stories"] = rights.CanDeleteStories, ["can_post_messages"] = rights.CanPostMessages,
            ["can_edit_messages"] = rights.CanEditMessages, ["can_pin_messages"] = rights.CanPinMessages,
            ["can_manage_topics"] = rights.CanManageTopics, ["can_manage_direct_messages"] = rights.CanManageDirectMessages
        });

    public Task SetAdministratorTitleAsync(string chatId, string userId, string title, CancellationToken cancellationToken = default) =>
        ChatCall("setChatAdministratorCustomTitle", chatId, cancellationToken,
            new JsonObject { ["user_id"] = User(userId), ["custom_title"] = title });

    public Task SetPermissionsAsync(string chatId, TelegramChatPermissions permissions, CancellationToken cancellationToken = default) =>
        ChatCall("setChatPermissions", chatId, cancellationToken,
            new JsonObject { ["permissions"] = PermissionsJson(permissions), ["use_independent_chat_permissions"] = true });

    public Task SetTitleAsync(string chatId, string title, CancellationToken cancellationToken = default) =>
        ChatCall("setChatTitle", chatId, cancellationToken, new JsonObject { ["title"] = title });

    public Task SetDescriptionAsync(string chatId, string? description, CancellationToken cancellationToken = default) =>
        ChatCall("setChatDescription", chatId, cancellationToken, new JsonObject { ["description"] = description ?? string.Empty });

    public async Task SetPhotoAsync(string chatId, string imageUri, CancellationToken cancellationToken = default)
    {
        // setChatPhoto 只接受上传内容，不接受 file_id 或地址。
        var file = await ResourceReader.ReadAsync(imageUri, "photo.jpg", cancellationToken, client.Downloads);
        await Call("setChatPhoto", new JsonObject { ["chat_id"] = Target(chatId) }, cancellationToken,
            [new TelegramUpload("photo", file.FileName, new MemoryStream(file.Bytes))]);
    }

    public Task DeletePhotoAsync(string chatId, CancellationToken cancellationToken = default) =>
        ChatCall("deleteChatPhoto", chatId, cancellationToken);

    public Task PinMessageAsync(string chatId, string messageId, bool disableNotification = false,
        CancellationToken cancellationToken = default) =>
        ChatCall("pinChatMessage", chatId, cancellationToken,
            new JsonObject { ["message_id"] = Message(messageId), ["disable_notification"] = disableNotification ? true : null });

    public Task UnpinMessageAsync(string chatId, string? messageId = null, CancellationToken cancellationToken = default) =>
        ChatCall("unpinChatMessage", chatId, cancellationToken,
            new JsonObject { ["message_id"] = messageId is null ? null : Message(messageId) });

    public Task UnpinAllMessagesAsync(string chatId, CancellationToken cancellationToken = default)
    {
        var (chat, thread) = ParseChat(chatId);
        return thread is { } topic
            ? Call("unpinAllForumTopicMessages", new JsonObject { ["chat_id"] = chat, ["message_thread_id"] = topic }, cancellationToken)
            : Call("unpinAllChatMessages", new JsonObject { ["chat_id"] = chat }, cancellationToken);
    }

    public async Task<string> ExportInviteLinkAsync(string chatId, CancellationToken cancellationToken = default) =>
        (await ChatCall("exportChatInviteLink", chatId, cancellationToken)).GetString()!;

    private static JsonObject InviteOptions(TelegramInviteLinkOptions options) => new()
    {
        ["name"] = options.Name, ["expire_date"] = UnixTime(options.ExpireDate), ["member_limit"] = options.MemberLimit,
        ["creates_join_request"] = options.CreatesJoinRequest ? true : null
    };

    public async Task<TelegramInviteLink> CreateInviteLinkAsync(string chatId, TelegramInviteLinkOptions? options = null,
        CancellationToken cancellationToken = default) =>
        InviteLink(await ChatCall("createChatInviteLink", chatId, cancellationToken, InviteOptions(options ?? new TelegramInviteLinkOptions())));

    public async Task<TelegramInviteLink> EditInviteLinkAsync(string chatId, string link, TelegramInviteLinkOptions options,
        CancellationToken cancellationToken = default)
    {
        var parameters = InviteOptions(options);
        parameters["invite_link"] = link;
        parameters["creates_join_request"] = options.CreatesJoinRequest;
        return InviteLink(await ChatCall("editChatInviteLink", chatId, cancellationToken, parameters));
    }

    public async Task<TelegramInviteLink> RevokeInviteLinkAsync(string chatId, string link, CancellationToken cancellationToken = default) =>
        InviteLink(await ChatCall("revokeChatInviteLink", chatId, cancellationToken, new JsonObject { ["invite_link"] = link }));

    public Task ApproveJoinRequestAsync(string chatId, string userId, CancellationToken cancellationToken = default) =>
        ChatCall("approveChatJoinRequest", chatId, cancellationToken, new JsonObject { ["user_id"] = User(userId) });

    public Task DeclineJoinRequestAsync(string chatId, string userId, CancellationToken cancellationToken = default) =>
        ChatCall("declineChatJoinRequest", chatId, cancellationToken, new JsonObject { ["user_id"] = User(userId) });

    public Task LeaveChatAsync(string chatId, CancellationToken cancellationToken = default) =>
        ChatCall("leaveChat", chatId, cancellationToken);

    public async Task<TelegramForumTopic> CreateTopicAsync(string chatId, string name, int? iconColor = null,
        string? iconCustomEmojiId = null, CancellationToken cancellationToken = default) =>
        Topic(await ChatCall("createForumTopic", chatId, cancellationToken,
            new JsonObject { ["name"] = name, ["icon_color"] = iconColor, ["icon_custom_emoji_id"] = iconCustomEmojiId }));

    public Task EditTopicAsync(string chatId, long threadId, string? name = null, string? iconCustomEmojiId = null,
        CancellationToken cancellationToken = default) =>
        ChatCall("editForumTopic", chatId, cancellationToken,
            new JsonObject { ["message_thread_id"] = threadId, ["name"] = name, ["icon_custom_emoji_id"] = iconCustomEmojiId });

    public Task CloseTopicAsync(string chatId, long threadId, CancellationToken cancellationToken = default) =>
        ChatCall("closeForumTopic", chatId, cancellationToken, new JsonObject { ["message_thread_id"] = threadId });

    public Task ReopenTopicAsync(string chatId, long threadId, CancellationToken cancellationToken = default) =>
        ChatCall("reopenForumTopic", chatId, cancellationToken, new JsonObject { ["message_thread_id"] = threadId });

    public Task DeleteTopicAsync(string chatId, long threadId, CancellationToken cancellationToken = default) =>
        ChatCall("deleteForumTopic", chatId, cancellationToken, new JsonObject { ["message_thread_id"] = threadId });

    // ─── ITelegramMessageApi ───

    public bool AutoAnswersCallbacks => interactions?.AutoAnswer ?? true;

    public async Task<IReadOnlyList<TelegramSentMessage>> SendAsync(Channel channel, TelegramOutgoingContent content,
        TelegramSendOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new TelegramSendOptions();
        var (chat, thread) = ParseChat(channel.Id);
        var parameters = new JsonObject
        {
            ["chat_id"] = chat, ["message_thread_id"] = thread, ["business_connection_id"] = options.BusinessConnectionId,
            ["disable_notification"] = options.DisableNotification ? true : null, ["protect_content"] = options.ProtectContent ? true : null,
            ["allow_paid_broadcast"] = options.AllowPaidBroadcast ? true : null, ["message_effect_id"] = options.MessageEffectId,
            ["reply_parameters"] = options.ReplyToMessageId is { } replyTo
                ? new JsonObject { ["message_id"] = Message(replyTo), ["allow_sending_without_reply"] = options.AllowSendingWithoutReply }
                : null,
            ["reply_markup"] = ReplyMarkup(options.ReplyMarkup)
        };
        var uploads = new List<TelegramUpload>();
        string method;
        switch (content)
        {
            case TelegramTextContent text:
                method = "sendMessage";
                parameters["text"] = text.Text;
                parameters["parse_mode"] = ParseMode(text.ParseMode);
                if (text.DisableLinkPreview) parameters["link_preview_options"] = new JsonObject { ["is_disabled"] = true };
                break;
            case TelegramPollContent poll:
                method = "sendPoll";
                parameters["question"] = poll.Question;
                parameters["options"] = new JsonArray(poll.Options.Select(o => (JsonNode?)new JsonObject { ["text"] = o }).ToArray());
                parameters["is_anonymous"] = poll.IsAnonymous;
                parameters["type"] = poll.Type == TelegramPollType.Quiz ? "quiz" : "regular";
                parameters["allows_multiple_answers"] = poll.AllowsMultipleAnswers ? true : null;
                parameters["correct_option_ids"] = poll.CorrectOptionIds is { } correct
                    ? new JsonArray(correct.Select(id => (JsonNode?)id).ToArray()) : null;
                parameters["explanation"] = poll.Explanation;
                parameters["open_period"] = poll.OpenPeriod is { } period ? (long)period.TotalSeconds : null;
                parameters["close_date"] = UnixTime(poll.CloseDate);
                break;
            case TelegramLocationContent location:
                method = "sendLocation";
                parameters["latitude"] = location.Latitude;
                parameters["longitude"] = location.Longitude;
                parameters["horizontal_accuracy"] = location.HorizontalAccuracy;
                parameters["live_period"] = location.LivePeriod is { } live ? (long)live.TotalSeconds : null;
                parameters["heading"] = location.Heading;
                break;
            case TelegramVenueContent venue:
                method = "sendVenue";
                parameters["latitude"] = venue.Latitude;
                parameters["longitude"] = venue.Longitude;
                parameters["title"] = venue.Title;
                parameters["address"] = venue.Address;
                parameters["foursquare_id"] = venue.FoursquareId;
                parameters["google_place_id"] = venue.GooglePlaceId;
                break;
            case TelegramContactContent contact:
                method = "sendContact";
                parameters["phone_number"] = contact.PhoneNumber;
                parameters["first_name"] = contact.FirstName;
                parameters["last_name"] = contact.LastName;
                parameters["vcard"] = contact.VCard;
                break;
            case TelegramDiceContent dice:
                method = "sendDice";
                parameters["emoji"] = dice.Emoji;
                break;
            case TelegramStickerContent sticker:
                method = "sendSticker";
                parameters["sticker"] = await FileAsync(sticker.Sticker, "sticker.webp", "sticker", null, uploads, client.Downloads,
                    cancellationToken);
                parameters["emoji"] = sticker.Emoji;
                break;
            case TelegramInvoiceContent invoice:
                if (options.ReplyMarkup is not (null or TelegramInlineKeyboard)) throw new NotSupportedException("发票只能附带内联键盘。");
                method = "sendInvoice";
                parameters["title"] = invoice.Title;
                parameters["description"] = invoice.Description;
                parameters["payload"] = invoice.Payload;
                parameters["currency"] = invoice.Currency;
                parameters["prices"] = new JsonArray(invoice.Prices
                    .Select(p => (JsonNode?)new JsonObject { ["label"] = p.Label, ["amount"] = p.Amount }).ToArray());
                parameters["provider_token"] = invoice.ProviderToken;
                parameters["photo_url"] = invoice.PhotoUrl;
                parameters["need_name"] = invoice.NeedName ? true : null;
                parameters["need_phone_number"] = invoice.NeedPhoneNumber ? true : null;
                parameters["need_email"] = invoice.NeedEmail ? true : null;
                parameters["need_shipping_address"] = invoice.NeedShippingAddress ? true : null;
                parameters["is_flexible"] = invoice.IsFlexible ? true : null;
                break;
            case TelegramMediaGroupContent group:
                if (options.ReplyMarkup is not null) throw new NotSupportedException("Telegram 相册不能附带键盘。");
                if (group.Items.Count is < 2 or > 10) throw new ArgumentException("相册需要 2–10 项。");
                method = "sendMediaGroup";
                var media = new JsonArray();
                foreach (var item in group.Items)
                    media.Add(new JsonObject
                    {
                        ["type"] = item.Kind switch
                        {
                            TelegramMediaKind.Photo => "photo", TelegramMediaKind.Video => "video",
                            TelegramMediaKind.Audio => "audio", TelegramMediaKind.Document => "document",
                            _ => throw new NotSupportedException("未知的相册媒体类型：" + item.Kind)
                        },
                        ["media"] = await FileAsync(item.Uri, item.FileName, "media", "shirobot_file" + uploads.Count.ToString(CultureInfo.InvariantCulture),
                            uploads, client.Downloads, cancellationToken),
                        ["caption"] = item.Caption, ["parse_mode"] = ParseMode(item.ParseMode),
                        ["has_spoiler"] = item.HasSpoiler && item.Kind is TelegramMediaKind.Photo or TelegramMediaKind.Video ? true : null
                    });
                parameters["media"] = media;
                var album = await Call(method, parameters, cancellationToken, uploads);
                return album.EnumerateArray().Select(Sent).ToArray();
            default:
                throw new NotSupportedException("Telegram 不支持发送 " + content.GetType().Name + "。");
        }

        return [Sent(await Call(method, parameters, cancellationToken, uploads))];
    }

    private static JsonObject EditTarget(Channel channel, string messageId) =>
        new() { ["chat_id"] = Target(channel.Id), ["message_id"] = Message(messageId) };

    public Task EditTextAsync(Channel channel, string messageId, string text, TelegramParseMode parseMode = TelegramParseMode.None,
        TelegramInlineKeyboard? replyMarkup = null, CancellationToken cancellationToken = default)
    {
        var parameters = EditTarget(channel, messageId);
        parameters["text"] = text;
        parameters["parse_mode"] = ParseMode(parseMode);
        parameters["reply_markup"] = InlineKeyboard(replyMarkup);
        return Call("editMessageText", parameters, cancellationToken);
    }

    public Task EditCaptionAsync(Channel channel, string messageId, string? caption, TelegramParseMode parseMode = TelegramParseMode.None,
        TelegramInlineKeyboard? replyMarkup = null, CancellationToken cancellationToken = default)
    {
        var parameters = EditTarget(channel, messageId);
        parameters["caption"] = caption ?? string.Empty;
        parameters["parse_mode"] = ParseMode(parseMode);
        parameters["reply_markup"] = InlineKeyboard(replyMarkup);
        return Call("editMessageCaption", parameters, cancellationToken);
    }

    public Task EditReplyMarkupAsync(Channel channel, string messageId, TelegramInlineKeyboard? replyMarkup,
        CancellationToken cancellationToken = default)
    {
        var parameters = EditTarget(channel, messageId);
        parameters["reply_markup"] = InlineKeyboard(replyMarkup);
        return Call("editMessageReplyMarkup", parameters, cancellationToken);
    }

    public Task EditInlineTextAsync(string inlineMessageId, string text, TelegramParseMode parseMode = TelegramParseMode.None,
        TelegramInlineKeyboard? replyMarkup = null, CancellationToken cancellationToken = default) =>
        Call("editMessageText", new JsonObject
        {
            ["inline_message_id"] = inlineMessageId, ["text"] = text, ["parse_mode"] = ParseMode(parseMode),
            ["reply_markup"] = InlineKeyboard(replyMarkup)
        }, cancellationToken);

    public async Task<TelegramSentMessage> ForwardAsync(Channel target, Channel source, string messageId, bool disableNotification = false,
        CancellationToken cancellationToken = default)
    {
        var (chat, thread) = ParseChat(target.Id);
        return Sent(await Call("forwardMessage", new JsonObject
        {
            ["chat_id"] = chat, ["message_thread_id"] = thread, ["from_chat_id"] = Target(source.Id), ["message_id"] = Message(messageId),
            ["disable_notification"] = disableNotification ? true : null
        }, cancellationToken));
    }

    public async Task<string> CopyAsync(Channel target, Channel source, string messageId, TelegramSendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new TelegramSendOptions();
        var (chat, thread) = ParseChat(target.Id);
        var copied = await Call("copyMessage", new JsonObject
        {
            ["chat_id"] = chat, ["message_thread_id"] = thread, ["from_chat_id"] = Target(source.Id), ["message_id"] = Message(messageId),
            ["disable_notification"] = options.DisableNotification ? true : null, ["protect_content"] = options.ProtectContent ? true : null,
            ["reply_parameters"] = options.ReplyToMessageId is { } reply
                ? new JsonObject { ["message_id"] = Message(reply), ["allow_sending_without_reply"] = options.AllowSendingWithoutReply }
                : null,
            ["reply_markup"] = ReplyMarkup(options.ReplyMarkup)
        }, cancellationToken);
        return (Long(copied, "message_id") ?? 0).ToString(CultureInfo.InvariantCulture);
    }

    public Task DeleteMessagesAsync(Channel channel, IReadOnlyList<string> messageIds, CancellationToken cancellationToken = default)
    {
        if (messageIds.Count is < 1 or > 100) throw new ArgumentException("一次删除 1–100 条消息。", nameof(messageIds));
        return Call("deleteMessages", new JsonObject
        {
            ["chat_id"] = Target(channel.Id), ["message_ids"] = new JsonArray(messageIds.Select(id => (JsonNode?)Message(id)).ToArray())
        }, cancellationToken);
    }

    public Task SendChatActionAsync(Channel channel, TelegramChatAction action, CancellationToken cancellationToken = default)
    {
        var (chat, thread) = ParseChat(channel.Id);
        return Call("sendChatAction", new JsonObject { ["chat_id"] = chat, ["message_thread_id"] = thread, ["action"] = ChatAction(action) },
            cancellationToken);
    }

    public async Task<TelegramPoll> StopPollAsync(Channel channel, string messageId, CancellationToken cancellationToken = default) =>
        Poll(await Call("stopPoll", EditTarget(channel, messageId), cancellationToken));

    public Task AnswerCallbackAsync(string interactionId, TelegramCallbackAnswer answer, CancellationToken cancellationToken = default) =>
        (interactions ?? throw new NotSupportedException("交互服务未启动。")).AnswerAsync(interactionId, answer, cancellationToken);

    // ─── ITelegramInlineApi ───

    public Task AnswerInlineQueryAsync(string queryId, IReadOnlyList<TelegramInlineResult> results,
        TelegramInlineAnswerOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (results.Count > 50) throw new ArgumentException("inline 查询最多返回 50 项结果。", nameof(results));
        options ??= new TelegramInlineAnswerOptions();
        return Call("answerInlineQuery", new JsonObject
        {
            ["inline_query_id"] = queryId, ["results"] = new JsonArray(results.Select(InlineResult).ToArray()),
            ["cache_time"] = options.CacheTime is { } cache ? (long)cache.TotalSeconds : null,
            ["is_personal"] = options.IsPersonal ? true : null, ["next_offset"] = options.NextOffset
        }, cancellationToken);
    }

    private static JsonNode? InlineResult(TelegramInlineResult result) => result switch
    {
        TelegramInlineArticle article => new JsonObject
        {
            ["type"] = "article", ["id"] = article.Id, ["title"] = article.Title,
            ["input_message_content"] = new JsonObject { ["message_text"] = article.MessageText, ["parse_mode"] = ParseMode(article.ParseMode) },
            ["description"] = article.Description, ["url"] = article.Url, ["thumbnail_url"] = article.ThumbnailUrl,
            ["reply_markup"] = InlineKeyboard(article.ReplyMarkup)
        },
        TelegramInlinePhoto photo when Uri.TryCreate(photo.Photo, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" =>
            new JsonObject
            {
                ["type"] = "photo", ["id"] = photo.Id, ["photo_url"] = photo.Photo, ["thumbnail_url"] = photo.ThumbnailUrl ?? photo.Photo,
                ["title"] = photo.Title, ["description"] = photo.Description, ["caption"] = photo.Caption,
                ["parse_mode"] = ParseMode(photo.ParseMode), ["reply_markup"] = InlineKeyboard(photo.ReplyMarkup)
            },
        TelegramInlinePhoto photo => new JsonObject
        {
            ["type"] = "photo", ["id"] = photo.Id,
            ["photo_file_id"] = photo.Photo.StartsWith("tgfile://", StringComparison.Ordinal) ? photo.Photo[9..] : photo.Photo,
            ["title"] = photo.Title, ["description"] = photo.Description, ["caption"] = photo.Caption,
            ["parse_mode"] = ParseMode(photo.ParseMode), ["reply_markup"] = InlineKeyboard(photo.ReplyMarkup)
        },
        TelegramInlineSticker sticker => new JsonObject
        {
            ["type"] = "sticker", ["id"] = sticker.Id, ["sticker_file_id"] = sticker.StickerFileId,
            ["reply_markup"] = InlineKeyboard(sticker.ReplyMarkup)
        },
        _ => throw new NotSupportedException("未知的 inline 结果类型：" + result.GetType().Name)
    };

    // ─── ITelegramPaymentApi ───

    public Task AnswerPreCheckoutQueryAsync(string queryId, bool ok, string? errorMessage = null,
        CancellationToken cancellationToken = default) =>
        Call("answerPreCheckoutQuery", new JsonObject
        {
            ["pre_checkout_query_id"] = queryId, ["ok"] = ok, ["error_message"] = ok ? null : errorMessage ?? "无法完成付款。"
        }, cancellationToken);

    public Task AnswerShippingQueryAsync(string queryId, bool ok, IReadOnlyList<TelegramShippingOption>? options = null,
        string? errorMessage = null, CancellationToken cancellationToken = default) =>
        Call("answerShippingQuery", new JsonObject
        {
            ["shipping_query_id"] = queryId, ["ok"] = ok,
            ["shipping_options"] = ok
                ? new JsonArray((options ?? throw new ArgumentException("确认收货地址时需要提供收货方式。", nameof(options)))
                    .Select(option => (JsonNode?)new JsonObject
                    {
                        ["id"] = option.Id, ["title"] = option.Title,
                        ["prices"] = new JsonArray(option.Prices
                            .Select(p => (JsonNode?)new JsonObject { ["label"] = p.Label, ["amount"] = p.Amount }).ToArray())
                    }).ToArray())
                : null,
            ["error_message"] = ok ? null : errorMessage ?? "无法配送到该地址。"
        }, cancellationToken);

    public Task RefundStarPaymentAsync(string userId, string telegramPaymentChargeId, CancellationToken cancellationToken = default) =>
        Call("refundStarPayment", new JsonObject { ["user_id"] = User(userId), ["telegram_payment_charge_id"] = telegramPaymentChargeId },
            cancellationToken);

    public async Task<long> GetStarBalanceAsync(CancellationToken cancellationToken = default) =>
        Long(await Call("getMyStarBalance", new JsonObject(), cancellationToken), "amount") ?? 0;

    // ─── ITelegramBotProfileApi ───

    public async Task<TelegramUser> GetMeAsync(CancellationToken cancellationToken = default) =>
        TelegramModelConverter.User(await Call("getMe", new JsonObject(), cancellationToken));

    public TelegramUser? FindKnownUser(string userId) =>
        mapper.Users.TryGet(userId, out var user) ? TelegramMapper.ToPlatformUser(user) : null;

    public Task SetCommandsAsync(IReadOnlyList<TelegramBotCommand> commands, TelegramCommandScope? scope = null, string? languageCode = null,
        CancellationToken cancellationToken = default) =>
        Call("setMyCommands", new JsonObject
        {
            ["commands"] = new JsonArray(commands
                .Select(c => (JsonNode?)new JsonObject { ["command"] = c.Command, ["description"] = c.Description }).ToArray()),
            ["scope"] = Scope(scope), ["language_code"] = languageCode
        }, cancellationToken);

    public async Task<IReadOnlyList<TelegramBotCommand>> GetCommandsAsync(TelegramCommandScope? scope = null, string? languageCode = null,
        CancellationToken cancellationToken = default) =>
        (await Call("getMyCommands", new JsonObject { ["scope"] = Scope(scope), ["language_code"] = languageCode }, cancellationToken))
        .EnumerateArray().Select(c => new TelegramBotCommand(Str(c, "command") ?? "", Str(c, "description") ?? "")).ToArray();

    public Task DeleteCommandsAsync(TelegramCommandScope? scope = null, string? languageCode = null,
        CancellationToken cancellationToken = default) =>
        Call("deleteMyCommands", new JsonObject { ["scope"] = Scope(scope), ["language_code"] = languageCode }, cancellationToken);

    public Task SetBotNameAsync(string? name, string? languageCode = null, CancellationToken cancellationToken = default) =>
        Call("setMyName", new JsonObject { ["name"] = name, ["language_code"] = languageCode }, cancellationToken);

    public Task SetBotDescriptionAsync(string? description, string? languageCode = null, CancellationToken cancellationToken = default) =>
        Call("setMyDescription", new JsonObject { ["description"] = description, ["language_code"] = languageCode }, cancellationToken);

    public Task SetBotShortDescriptionAsync(string? description, string? languageCode = null,
        CancellationToken cancellationToken = default) =>
        Call("setMyShortDescription", new JsonObject { ["short_description"] = description, ["language_code"] = languageCode },
            cancellationToken);

    // ─── ITelegramRawApi ───

    public string BotApiVersion => "10.4";

    public Task<JsonElement> CallAsync(string method, JsonObject? parameters = null, IReadOnlyList<TelegramUploadFile>? files = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        return client.InvokeAsync(method, parameters,
            files?.Select(file => new TelegramUpload(file.Name, file.FileName, file.Content)).ToArray(), cancellationToken);
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
