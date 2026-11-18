using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShiroBot.Model.Telegram;
using ShiroBot.SDK.Models;

namespace ShiroBot.TelegramAdapter;

/// <summary>Bot API JSON 与宿主共享的 <c>ShiroBot.Model.Telegram</c> 契约之间的转换。</summary>
internal static class TelegramModelConverter
{
    // ─── JSON 读取 ───

    public static bool Has(JsonElement e, string name, out JsonElement value)
    {
        value = default;
        return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null;
    }

    public static JsonElement Prop(JsonElement e, string name) => Has(e, name, out var value) ? value : default;

    public static string? Str(JsonElement e, string name) =>
        Has(e, name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString() : null;

    public static long? Long(JsonElement e, string name) => Has(e, name, out var v) && v.TryGetInt64(out var n) ? n : null;
    public static int Int(JsonElement e, string name) => Has(e, name, out var v) && v.TryGetInt32(out var n) ? n : 0;
    public static double? Double(JsonElement e, string name) => Has(e, name, out var v) && v.TryGetDouble(out var n) ? n : null;
    public static bool Bool(JsonElement e, string name) => Has(e, name, out var v) && v.ValueKind == JsonValueKind.True;

    public static IEnumerable<JsonElement> Items(JsonElement e, string name) =>
        Has(e, name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

    public static DateTimeOffset Time(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

    private static DateTimeOffset? OptionalTime(JsonElement e, string name) => Long(e, name) is { } t and > 0 ? Time(t) : null;

    public static long? UnixTime(DateTimeOffset? value) => value?.ToUnixTimeSeconds();

    // ─── 入站 ───

    public static TelegramUser User(JsonElement user) => new()
    {
        UserId = Long(user, "id") ?? 0, FirstName = Str(user, "first_name") ?? string.Empty, LastName = Str(user, "last_name"),
        Username = Str(user, "username"), IsBot = Bool(user, "is_bot"), LanguageCode = Str(user, "language_code"),
        IsPremium = Bool(user, "is_premium")
    };

    public static TelegramChatType ChatType(string? type) => type switch
    {
        "private" => TelegramChatType.Private,
        "group" => TelegramChatType.Group,
        "supergroup" => TelegramChatType.Supergroup,
        "channel" => TelegramChatType.Channel,
        _ => throw new NotSupportedException("未知的 Telegram 会话类型：" + type)
    };

    /// <summary>Chat 或 ChatFullInfo；后者额外填充资料字段。</summary>
    public static TelegramChat Chat(JsonElement chat) => new()
    {
        ChatId = Long(chat, "id") ?? 0, Type = ChatType(Str(chat, "type")), Title = Str(chat, "title"), Username = Str(chat, "username"),
        FirstName = Str(chat, "first_name"), LastName = Str(chat, "last_name"), IsForum = Bool(chat, "is_forum"),
        IsDirectMessages = Bool(chat, "is_direct_messages"), Description = Str(chat, "description"), Bio = Str(chat, "bio"),
        InviteLink = Str(chat, "invite_link"),
        Permissions = Has(chat, "permissions", out var permissions) ? Permissions(permissions) : null,
        SlowModeDelay = Long(chat, "slow_mode_delay") is { } delay ? TimeSpan.FromSeconds(delay) : null,
        LinkedChatId = Long(chat, "linked_chat_id")
    };

    /// <summary>Bot API 会话对应的 SDK 频道，话题使用 <c>chatId:threadId</c>。</summary>
    public static Channel Channel(JsonElement chat, long? threadId)
    {
        var id = (Long(chat, "id") ?? 0).ToString(CultureInfo.InvariantCulture);
        if (Str(chat, "type") == "private") return SDK.Models.Channel.Direct(id);
        var title = Str(chat, "title");
        return threadId is { } thread
            ? new Channel($"{id}:{thread.ToString(CultureInfo.InvariantCulture)}", ChannelType.Thread) { Name = title, GuildId = id }
            : new Channel(id, ChannelType.Group) { Name = title ?? Str(chat, "username"), GuildId = id };
    }

    private static long? ThreadId(JsonElement message) => Bool(message, "is_topic_message") ? Long(message, "message_thread_id") : null;

    public static Channel MessageChannel(JsonElement message) => Channel(Prop(message, "chat"), ThreadId(message));

    private static readonly string[] PermissionFields =
    [
        "can_send_messages", "can_send_audios", "can_send_documents", "can_send_photos", "can_send_videos", "can_send_video_notes",
        "can_send_voice_notes", "can_send_polls", "can_send_other_messages", "can_add_web_page_previews", "can_react_to_messages",
        "can_change_info", "can_invite_users", "can_pin_messages", "can_manage_topics"
    ];

    private static bool? OptionalBool(JsonElement e, string name) =>
        Has(e, name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    /// <summary>ChatPermissions 或 ChatMemberRestricted（字段名相同）。</summary>
    public static TelegramChatPermissions Permissions(JsonElement value) => new()
    {
        CanSendMessages = OptionalBool(value, "can_send_messages"), CanSendAudios = OptionalBool(value, "can_send_audios"),
        CanSendDocuments = OptionalBool(value, "can_send_documents"), CanSendPhotos = OptionalBool(value, "can_send_photos"),
        CanSendVideos = OptionalBool(value, "can_send_videos"), CanSendVideoNotes = OptionalBool(value, "can_send_video_notes"),
        CanSendVoiceNotes = OptionalBool(value, "can_send_voice_notes"), CanSendPolls = OptionalBool(value, "can_send_polls"),
        CanSendOtherMessages = OptionalBool(value, "can_send_other_messages"),
        CanAddWebPagePreviews = OptionalBool(value, "can_add_web_page_previews"),
        CanReactToMessages = OptionalBool(value, "can_react_to_messages"), CanChangeInfo = OptionalBool(value, "can_change_info"),
        CanInviteUsers = OptionalBool(value, "can_invite_users"), CanPinMessages = OptionalBool(value, "can_pin_messages"),
        CanManageTopics = OptionalBool(value, "can_manage_topics")
    };

    public static TelegramChatMember Member(JsonElement member)
    {
        var user = User(Prop(member, "user"));
        return Str(member, "status") switch
        {
            "creator" => new TelegramChatMember
            {
                User = user, Status = TelegramMemberStatus.Owner, CustomTitle = Str(member, "custom_title"),
                IsAnonymous = Bool(member, "is_anonymous"), IsMember = true
            },
            "administrator" => new TelegramChatMember
            {
                User = user, Status = TelegramMemberStatus.Administrator, CustomTitle = Str(member, "custom_title"),
                IsAnonymous = Bool(member, "is_anonymous"), IsMember = true, CanBeEdited = Bool(member, "can_be_edited"),
                AdminRights = new TelegramAdminRights
                {
                    IsAnonymous = Bool(member, "is_anonymous"), CanManageChat = Bool(member, "can_manage_chat"),
                    CanDeleteMessages = Bool(member, "can_delete_messages"), CanManageVideoChats = Bool(member, "can_manage_video_chats"),
                    CanRestrictMembers = Bool(member, "can_restrict_members"), CanPromoteMembers = Bool(member, "can_promote_members"),
                    CanChangeInfo = Bool(member, "can_change_info"), CanInviteUsers = Bool(member, "can_invite_users"),
                    CanPostStories = Bool(member, "can_post_stories"), CanEditStories = Bool(member, "can_edit_stories"),
                    CanDeleteStories = Bool(member, "can_delete_stories"), CanPostMessages = Bool(member, "can_post_messages"),
                    CanEditMessages = Bool(member, "can_edit_messages"), CanPinMessages = Bool(member, "can_pin_messages"),
                    CanManageTopics = Bool(member, "can_manage_topics"),
                    CanManageDirectMessages = Bool(member, "can_manage_direct_messages")
                }
            },
            "restricted" => new TelegramChatMember
            {
                User = user, Status = TelegramMemberStatus.Restricted, IsMember = Bool(member, "is_member"),
                UntilDate = OptionalTime(member, "until_date"), Permissions = Permissions(member)
            },
            "kicked" => new TelegramChatMember
                { User = user, Status = TelegramMemberStatus.Banned, UntilDate = OptionalTime(member, "until_date") },
            "left" => new TelegramChatMember { User = user, Status = TelegramMemberStatus.Left },
            _ => new TelegramChatMember { User = user, Status = TelegramMemberStatus.Member, IsMember = true }
        };
    }

    public static TelegramInviteLink InviteLink(JsonElement link) => new()
    {
        Link = Str(link, "invite_link") ?? string.Empty, Name = Str(link, "name"),
        Creator = Has(link, "creator", out var creator) ? User(creator) : null,
        CreatesJoinRequest = Bool(link, "creates_join_request"), IsPrimary = Bool(link, "is_primary"), IsRevoked = Bool(link, "is_revoked"),
        ExpireDate = OptionalTime(link, "expire_date"), MemberLimit = (int?)Long(link, "member_limit"),
        PendingJoinRequestCount = (int?)Long(link, "pending_join_request_count")
    };

    public static TelegramForumTopic Topic(JsonElement topic) => new()
    {
        ThreadId = Long(topic, "message_thread_id") ?? 0, Name = Str(topic, "name") ?? string.Empty, IconColor = Int(topic, "icon_color"),
        IconCustomEmojiId = Str(topic, "icon_custom_emoji_id")
    };

    public static TelegramLocation Location(JsonElement location) =>
        new(Double(location, "latitude") ?? 0, Double(location, "longitude") ?? 0)
        {
            HorizontalAccuracy = Double(location, "horizontal_accuracy"),
            LivePeriod = Long(location, "live_period") is { } period ? TimeSpan.FromSeconds(period) : null,
            Heading = (int?)Long(location, "heading")
        };

    public static TelegramPoll Poll(JsonElement poll) => new()
    {
        PollId = Str(poll, "id") ?? string.Empty, Question = Str(poll, "question") ?? string.Empty,
        Options = Items(poll, "options").Select(o => new TelegramPollOption(Str(o, "text") ?? string.Empty, Int(o, "voter_count"))).ToArray(),
        TotalVoterCount = Int(poll, "total_voter_count"), IsClosed = Bool(poll, "is_closed"), IsAnonymous = Bool(poll, "is_anonymous"),
        Type = Str(poll, "type") == "quiz" ? TelegramPollType.Quiz : TelegramPollType.Regular,
        AllowsMultipleAnswers = Bool(poll, "allows_multiple_answers"),
        CorrectOptionIds = Has(poll, "correct_option_ids", out var ids) ? ids.EnumerateArray().Select(i => i.GetInt32()).ToArray() : null,
        Explanation = Str(poll, "explanation"), CloseDate = OptionalTime(poll, "close_date")
    };

    public static TelegramForwardOrigin? ForwardOrigin(JsonElement origin) => Str(origin, "type") switch
    {
        "user" => new TelegramForwardOrigin
            { Kind = TelegramForwardOriginKind.User, Date = Time(Long(origin, "date") ?? 0), User = User(Prop(origin, "sender_user")) },
        "hidden_user" => new TelegramForwardOrigin
        {
            Kind = TelegramForwardOriginKind.HiddenUser, Date = Time(Long(origin, "date") ?? 0),
            SenderName = Str(origin, "sender_user_name")
        },
        "chat" => new TelegramForwardOrigin
        {
            Kind = TelegramForwardOriginKind.Chat, Date = Time(Long(origin, "date") ?? 0), Chat = Chat(Prop(origin, "sender_chat")),
            AuthorSignature = Str(origin, "author_signature")
        },
        "channel" => new TelegramForwardOrigin
        {
            Kind = TelegramForwardOriginKind.Channel, Date = Time(Long(origin, "date") ?? 0), Chat = Chat(Prop(origin, "chat")),
            MessageId = Long(origin, "message_id"), AuthorSignature = Str(origin, "author_signature")
        },
        _ => null
    };

    public static TelegramMessage Message(JsonElement message) => new()
    {
        MessageId = Long(message, "message_id") ?? 0, Chat = Chat(Prop(message, "chat")), ThreadId = ThreadId(message),
        From = Has(message, "from", out var from) ? User(from) : null,
        SenderChat = Has(message, "sender_chat", out var sender) ? Chat(sender) : null, Date = Time(Long(message, "date") ?? 0),
        EditDate = OptionalTime(message, "edit_date"), MediaGroupId = Str(message, "media_group_id"),
        ForwardOrigin = Has(message, "forward_origin", out var origin) ? ForwardOrigin(origin) : null,
        IsAutomaticForward = Bool(message, "is_automatic_forward"), ViaBot = Has(message, "via_bot", out var via) ? User(via) : null,
        AuthorSignature = Str(message, "author_signature"), BusinessConnectionId = Str(message, "business_connection_id"),
        Json = message.Clone()
    };

    /// <summary>发送者设置的格式实体；不含 Telegram 自动识别的 mention、hashtag、bot_command、url 等。</summary>
    private static readonly IReadOnlySet<string> FormattingEntities = new HashSet<string>(StringComparer.Ordinal)
    {
        "bold", "italic", "underline", "strikethrough", "spoiler", "code", "pre", "text_link", "text_mention",
        "custom_emoji", "blockquote", "expandable_blockquote", "date_time"
    };

    /// <summary>入站消息中没有通用段的 Telegram 内容。</summary>
    public static IEnumerable<MessageSegment> NativeSegments(JsonElement message)
    {
        const string platform = TelegramMapper.PlatformId;
        if (Has(message, "venue", out var venue))
            yield return new RawSegment(platform, TelegramSegmentKinds.Venue,
                new TelegramVenue(Location(Prop(venue, "location")), Str(venue, "title") ?? "", Str(venue, "address") ?? "")
                    { FoursquareId = Str(venue, "foursquare_id"), GooglePlaceId = Str(venue, "google_place_id") });
        else if (Has(message, "location", out var location))
            yield return new RawSegment(platform, TelegramSegmentKinds.Location, Location(location));
        if (Has(message, "contact", out var contact))
            yield return new RawSegment(platform, TelegramSegmentKinds.Contact,
                new TelegramContact(Str(contact, "phone_number") ?? "", Str(contact, "first_name") ?? "")
                    { LastName = Str(contact, "last_name"), UserId = Long(contact, "user_id"), VCard = Str(contact, "vcard") });
        if (Has(message, "dice", out var dice))
            yield return new RawSegment(platform, TelegramSegmentKinds.Dice, new TelegramDice(Str(dice, "emoji") ?? "", Int(dice, "value")));
        if (Has(message, "poll", out var poll))
            yield return new RawSegment(platform, TelegramSegmentKinds.Poll, Poll(poll));
        if (Has(message, "successful_payment", out var payment))
            yield return new RawSegment(platform, TelegramSegmentKinds.SuccessfulPayment, new TelegramSuccessfulPayment
            {
                Currency = Str(payment, "currency") ?? "", TotalAmount = Long(payment, "total_amount") ?? 0,
                InvoicePayload = Str(payment, "invoice_payload") ?? "",
                TelegramPaymentChargeId = Str(payment, "telegram_payment_charge_id") ?? "",
                ProviderPaymentChargeId = Str(payment, "provider_payment_charge_id"), ShippingOptionId = Str(payment, "shipping_option_id"),
                SubscriptionExpirationDate = OptionalTime(payment, "subscription_expiration_date")
            });
        var text = Str(message, "text") ?? Str(message, "caption");
        var entities = (Has(message, "entities", out _) ? Items(message, "entities") : Items(message, "caption_entities")).ToArray();
        if (text is not null && entities.Any(entity => FormattingEntities.Contains(Str(entity, "type") ?? "")))
            yield return new RawSegment(platform, TelegramSegmentKinds.FormattedText, new TelegramFormattedText(text,
                entities.Select(entity => new TelegramTextEntity(Str(entity, "type") ?? "", Int(entity, "offset"), Int(entity, "length"))
                {
                    Url = Str(entity, "url"), User = Has(entity, "user", out var user) ? User(user) : null,
                    Language = Str(entity, "language"), CustomEmojiId = Str(entity, "custom_emoji_id")
                }).ToArray()));
    }

    /// <summary>已转换为类型化段或 <see cref="TelegramMessage"/> 资料的字段，不再作为原始段上报。</summary>
    public static readonly IReadOnlySet<string> ModeledMessageFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "location", "venue", "contact", "dice", "poll", "successful_payment", "edit_date", "media_group_id",
        "forward_origin", "is_automatic_forward", "via_bot", "author_signature", "business_connection_id",
        "is_topic_message", "has_protected_content", "is_from_offline", "link_preview_options", "reply_markup",
        "sender_boost_count", "effect_id", "show_caption_above_media"
    };

    public static TelegramSentMessage Sent(JsonElement message) => new()
    {
        MessageId = (Long(message, "message_id") ?? 0).ToString(CultureInfo.InvariantCulture), Channel = MessageChannel(message),
        Timestamp = Time(Long(message, "date") ?? 0), MediaGroupId = Str(message, "media_group_id")
    };

    // ─── 出站 ───

    public static string? ParseMode(TelegramParseMode mode) => mode switch
    {
        TelegramParseMode.MarkdownV2 => "MarkdownV2",
        TelegramParseMode.Html => "HTML",
        _ => null
    };

    public static JsonObject PermissionsJson(TelegramChatPermissions value)
    {
        bool?[] values =
        [
            value.CanSendMessages, value.CanSendAudios, value.CanSendDocuments, value.CanSendPhotos, value.CanSendVideos,
            value.CanSendVideoNotes, value.CanSendVoiceNotes, value.CanSendPolls, value.CanSendOtherMessages,
            value.CanAddWebPagePreviews, value.CanReactToMessages, value.CanChangeInfo, value.CanInviteUsers, value.CanPinMessages,
            value.CanManageTopics
        ];
        var json = new JsonObject();
        for (var i = 0; i < PermissionFields.Length; i++)
            if (values[i] is { } allowed) json[PermissionFields[i]] = allowed;
        return json;
    }

    public static JsonObject? InlineKeyboard(TelegramInlineKeyboard? keyboard) => keyboard is null
        ? null
        : new JsonObject
        {
            ["inline_keyboard"] = new JsonArray(keyboard.Rows.Select(row => (JsonNode?)new JsonArray(row.Select(InlineButton).ToArray())).ToArray())
        };

    private static JsonNode? InlineButton(TelegramInlineButton button)
    {
        var actions = new object?[]
        {
            button.Url, button.CallbackData, button.WebAppUrl, button.SwitchInlineQuery,
            button.SwitchInlineQueryCurrentChat, button.CopyText, button.Pay ? true : null
        }.Count(action => action is not null);
        if (actions != 1) throw new ArgumentException($"内联按钮“{button.Text}”必须且只能设置一种动作。");
        return new JsonObject
        {
            ["text"] = button.Text, ["url"] = button.Url, ["callback_data"] = button.CallbackData,
            ["web_app"] = button.WebAppUrl is { } webApp ? new JsonObject { ["url"] = webApp } : null,
            ["switch_inline_query"] = button.SwitchInlineQuery, ["switch_inline_query_current_chat"] = button.SwitchInlineQueryCurrentChat,
            ["copy_text"] = button.CopyText is { } copy ? new JsonObject { ["text"] = copy } : null, ["pay"] = button.Pay ? true : null
        };
    }

    public static JsonObject? ReplyMarkup(TelegramReplyMarkup? markup) => markup switch
    {
        null => null,
        TelegramInlineKeyboard inline => InlineKeyboard(inline),
        TelegramReplyKeyboard keyboard => new JsonObject
        {
            ["keyboard"] = new JsonArray(keyboard.Rows.Select(row => (JsonNode?)new JsonArray(row.Select(button => (JsonNode?)new JsonObject
            {
                ["text"] = button.Text, ["request_contact"] = button.RequestContact ? true : null,
                ["request_location"] = button.RequestLocation ? true : null,
                ["request_poll"] = button.RequestPoll || button.RequestPollType is not null
                    ? new JsonObject
                    {
                        ["type"] = button.RequestPollType switch
                        {
                            TelegramPollType.Quiz => "quiz", TelegramPollType.Regular => "regular", _ => null
                        }
                    }
                    : null,
                ["web_app"] = button.WebAppUrl is { } webApp ? new JsonObject { ["url"] = webApp } : null
            }).ToArray())).ToArray()),
            ["is_persistent"] = keyboard.IsPersistent ? true : null, ["resize_keyboard"] = keyboard.ResizeKeyboard ? true : null,
            ["one_time_keyboard"] = keyboard.OneTimeKeyboard ? true : null,
            ["input_field_placeholder"] = keyboard.InputFieldPlaceholder, ["selective"] = keyboard.Selective ? true : null
        },
        TelegramRemoveKeyboard remove => new JsonObject { ["remove_keyboard"] = true, ["selective"] = remove.Selective ? true : null },
        TelegramForceReply force => new JsonObject
        {
            ["force_reply"] = true, ["input_field_placeholder"] = force.InputFieldPlaceholder, ["selective"] = force.Selective ? true : null
        },
        _ => throw new NotSupportedException("未知的 Telegram 键盘类型：" + markup.GetType().Name)
    };

    public static JsonObject? Scope(TelegramCommandScope? scope)
    {
        if (scope is null) return null;
        JsonNode Chat() => ParseChat(scope.ChatId ?? throw new ArgumentException("该命令范围需要 ChatId。")).Chat;
        return scope.Kind switch
        {
            TelegramCommandScopeKind.Default => new JsonObject { ["type"] = "default" },
            TelegramCommandScopeKind.AllPrivateChats => new JsonObject { ["type"] = "all_private_chats" },
            TelegramCommandScopeKind.AllGroupChats => new JsonObject { ["type"] = "all_group_chats" },
            TelegramCommandScopeKind.AllChatAdministrators => new JsonObject { ["type"] = "all_chat_administrators" },
            TelegramCommandScopeKind.Chat => new JsonObject { ["type"] = "chat", ["chat_id"] = Chat() },
            TelegramCommandScopeKind.ChatAdministrators => new JsonObject { ["type"] = "chat_administrators", ["chat_id"] = Chat() },
            TelegramCommandScopeKind.ChatMember => new JsonObject
            {
                ["type"] = "chat_member", ["chat_id"] = Chat(),
                ["user_id"] = TelegramMapper.ParseId(scope.UserId ?? throw new ArgumentException("该命令范围需要 UserId。"), "userId")
            },
            _ => throw new NotSupportedException("未知的命令范围：" + scope.Kind)
        };
    }

    /// <summary>会话参数：数字 ID、@username 或 SDK 频道 ID（chatId:threadId）。</summary>
    public static (JsonNode Chat, long? ThreadId) ParseChat(string chatId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        if (chatId.StartsWith('@')) return (JsonValue.Create(chatId), null);
        var (chat, thread) = TelegramMapper.ParseChannelId(chatId);
        return (JsonValue.Create(chat), thread);
    }

    public static string ChatAction(TelegramChatAction action) => action switch
    {
        TelegramChatAction.Typing => "typing",
        TelegramChatAction.UploadPhoto => "upload_photo",
        TelegramChatAction.RecordVideo => "record_video",
        TelegramChatAction.UploadVideo => "upload_video",
        TelegramChatAction.RecordVoice => "record_voice",
        TelegramChatAction.UploadVoice => "upload_voice",
        TelegramChatAction.UploadDocument => "upload_document",
        TelegramChatAction.ChooseSticker => "choose_sticker",
        TelegramChatAction.FindLocation => "find_location",
        TelegramChatAction.RecordVideoNote => "record_video_note",
        TelegramChatAction.UploadVideoNote => "upload_video_note",
        _ => throw new NotSupportedException("未知的会话动作：" + action)
    };

    /// <summary>
    ///     资源 URI 转为 Bot API 文件参数：file_id（可带 tgfile://）和 HTTP(S) 地址交给 Telegram；本地与 base64 内容由适配器上传，
    ///     加入 <paramref name="uploads"/>。<paramref name="attachName"/> 为 null 时作为同名顶层参数上传，返回 null（不写入 JSON）；
    ///     否则返回 attach:// 引用，用于 InputMedia 等嵌套对象。
    /// </summary>
    public static async Task<JsonNode?> FileAsync(string uri, string? fileName, string parameter, string? attachName,
        List<TelegramUpload> uploads, HttpClient? downloads, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        if (uri.StartsWith("tgfile://", StringComparison.Ordinal)) return uri[9..];
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https") return uri;
        if (!(Path.IsPathFullyQualified(uri) || uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
              || uri.StartsWith("data:", StringComparison.Ordinal) || uri.StartsWith("base64://", StringComparison.Ordinal)))
            return uri; // 已有的 file_id
        var file = await ResourceReader.ReadAsync(uri, fileName, token, downloads);
        uploads.Add(new TelegramUpload(attachName ?? parameter, file.FileName, new MemoryStream(file.Bytes)));
        return attachName is null ? null : "attach://" + attachName;
    }
}
