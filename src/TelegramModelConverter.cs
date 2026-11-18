using System.Globalization;
using System.Text.Json;
using ShiroBot.Model.Telegram;
using ShiroBot.SDK.Models;
using Api = ShiroBot.Telegram.Model.BotApi;

namespace ShiroBot.TelegramAdapter;

/// <summary>Bot API 类型与宿主共享的 <c>ShiroBot.Model.Telegram</c> 契约之间的转换。</summary>
internal static class TelegramModelConverter
{
    public static DateTimeOffset Time(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

    private static DateTimeOffset? OptionalTime(long? unixSeconds) =>
        unixSeconds is > 0 ? Time(unixSeconds.Value) : null;

    public static long? UnixTime(DateTimeOffset? value) => value?.ToUnixTimeSeconds();

    // ─── 入站 ───

    public static TelegramUser User(Api.User user) => new()
    {
        UserId = user.Id, FirstName = user.FirstName, LastName = user.LastName, Username = user.Username,
        IsBot = user.IsBot, LanguageCode = user.LanguageCode, IsPremium = user.IsPremium == true
    };

    public static TelegramChatType ChatType(string type) => type switch
    {
        "private" => TelegramChatType.Private,
        "group" => TelegramChatType.Group,
        "supergroup" => TelegramChatType.Supergroup,
        "channel" => TelegramChatType.Channel,
        _ => throw new NotSupportedException("未知的 Telegram 会话类型：" + type)
    };

    public static TelegramChat Chat(Api.Chat chat) => new()
    {
        ChatId = chat.Id, Type = ChatType(chat.Type), Title = chat.Title, Username = chat.Username,
        FirstName = chat.FirstName, LastName = chat.LastName, IsForum = chat.IsForum == true,
        IsDirectMessages = chat.IsDirectMessages == true
    };

    public static TelegramChat Chat(Api.ChatFullInfo chat) => new()
    {
        ChatId = chat.Id, Type = ChatType(chat.Type), Title = chat.Title, Username = chat.Username,
        FirstName = chat.FirstName, LastName = chat.LastName, IsForum = chat.IsForum == true,
        IsDirectMessages = chat.IsDirectMessages == true, Description = chat.Description, Bio = chat.Bio,
        InviteLink = chat.InviteLink, Permissions = chat.Permissions is { } permissions ? Permissions(permissions) : null,
        SlowModeDelay = chat.SlowModeDelay is { } delay ? TimeSpan.FromSeconds(delay) : null,
        LinkedChatId = chat.LinkedChatId
    };

    /// <summary>Bot API 会话对应的 SDK 频道，话题使用 <c>chatId:threadId</c>。</summary>
    public static Channel Channel(Api.Chat chat, long? threadId)
    {
        var id = chat.Id.ToString(CultureInfo.InvariantCulture);
        if (chat.Type == "private") return SDK.Models.Channel.Direct(id);
        return threadId is { } thread
            ? new Channel($"{id}:{thread.ToString(CultureInfo.InvariantCulture)}", ChannelType.Thread) { Name = chat.Title, GuildId = id }
            : new Channel(id, ChannelType.Group) { Name = chat.Title ?? chat.Username, GuildId = id };
    }

    public static Channel Channel(Api.Message message) =>
        Channel(message.Chat, message.IsTopicMessage == true ? message.MessageThreadId : null);

    public static TelegramChatPermissions Permissions(Api.ChatPermissions value) => new()
    {
        CanSendMessages = value.CanSendMessages, CanSendAudios = value.CanSendAudios,
        CanSendDocuments = value.CanSendDocuments, CanSendPhotos = value.CanSendPhotos,
        CanSendVideos = value.CanSendVideos, CanSendVideoNotes = value.CanSendVideoNotes,
        CanSendVoiceNotes = value.CanSendVoiceNotes, CanSendPolls = value.CanSendPolls,
        CanSendOtherMessages = value.CanSendOtherMessages, CanAddWebPagePreviews = value.CanAddWebPagePreviews,
        CanReactToMessages = value.CanReactToMessages, CanChangeInfo = value.CanChangeInfo,
        CanInviteUsers = value.CanInviteUsers, CanPinMessages = value.CanPinMessages,
        CanManageTopics = value.CanManageTopics
    };

    public static TelegramChatMember Member(Api.ChatMember member) => member switch
    {
        Api.ChatMemberOwner owner => new TelegramChatMember
        {
            User = User(owner.User), Status = TelegramMemberStatus.Owner, CustomTitle = owner.CustomTitle,
            IsAnonymous = owner.IsAnonymous, IsMember = true
        },
        Api.ChatMemberAdministrator admin => new TelegramChatMember
        {
            User = User(admin.User), Status = TelegramMemberStatus.Administrator, CustomTitle = admin.CustomTitle,
            IsAnonymous = admin.IsAnonymous, IsMember = true, CanBeEdited = admin.CanBeEdited,
            AdminRights = new TelegramAdminRights
            {
                IsAnonymous = admin.IsAnonymous, CanManageChat = admin.CanManageChat,
                CanDeleteMessages = admin.CanDeleteMessages, CanManageVideoChats = admin.CanManageVideoChats,
                CanRestrictMembers = admin.CanRestrictMembers, CanPromoteMembers = admin.CanPromoteMembers,
                CanChangeInfo = admin.CanChangeInfo, CanInviteUsers = admin.CanInviteUsers,
                CanPostStories = admin.CanPostStories, CanEditStories = admin.CanEditStories,
                CanDeleteStories = admin.CanDeleteStories, CanPostMessages = admin.CanPostMessages == true,
                CanEditMessages = admin.CanEditMessages == true, CanPinMessages = admin.CanPinMessages == true,
                CanManageTopics = admin.CanManageTopics == true,
                CanManageDirectMessages = admin.CanManageDirectMessages == true
            }
        },
        Api.ChatMemberRestricted restricted => new TelegramChatMember
        {
            User = User(restricted.User), Status = TelegramMemberStatus.Restricted, IsMember = restricted.IsMember,
            UntilDate = OptionalTime(restricted.UntilDate),
            Permissions = new TelegramChatPermissions
            {
                CanSendMessages = restricted.CanSendMessages, CanSendAudios = restricted.CanSendAudios,
                CanSendDocuments = restricted.CanSendDocuments, CanSendPhotos = restricted.CanSendPhotos,
                CanSendVideos = restricted.CanSendVideos, CanSendVideoNotes = restricted.CanSendVideoNotes,
                CanSendVoiceNotes = restricted.CanSendVoiceNotes, CanSendPolls = restricted.CanSendPolls,
                CanSendOtherMessages = restricted.CanSendOtherMessages,
                CanAddWebPagePreviews = restricted.CanAddWebPagePreviews,
                CanReactToMessages = restricted.CanReactToMessages, CanChangeInfo = restricted.CanChangeInfo,
                CanInviteUsers = restricted.CanInviteUsers, CanPinMessages = restricted.CanPinMessages,
                CanManageTopics = restricted.CanManageTopics
            }
        },
        Api.ChatMemberBanned banned => new TelegramChatMember
        {
            User = User(banned.User), Status = TelegramMemberStatus.Banned, UntilDate = OptionalTime(banned.UntilDate)
        },
        Api.ChatMemberLeft left => new TelegramChatMember { User = User(left.User), Status = TelegramMemberStatus.Left },
        _ => new TelegramChatMember { User = User(member.User), Status = TelegramMemberStatus.Member, IsMember = true }
    };

    public static TelegramInviteLink InviteLink(Api.ChatInviteLink link) => new()
    {
        Link = link.InviteLink, Name = link.Name, Creator = link.Creator is { } creator ? User(creator) : null,
        CreatesJoinRequest = link.CreatesJoinRequest, IsPrimary = link.IsPrimary, IsRevoked = link.IsRevoked,
        ExpireDate = OptionalTime(link.ExpireDate), MemberLimit = (int?)link.MemberLimit,
        PendingJoinRequestCount = (int?)link.PendingJoinRequestCount
    };

    public static TelegramForumTopic Topic(Api.ForumTopic topic) => new()
    {
        ThreadId = topic.MessageThreadId, Name = topic.Name, IconColor = (int)topic.IconColor,
        IconCustomEmojiId = topic.IconCustomEmojiId
    };

    public static TelegramLocation Location(Api.Location location) => new(location.Latitude, location.Longitude)
    {
        HorizontalAccuracy = location.HorizontalAccuracy,
        LivePeriod = location.LivePeriod is { } period ? TimeSpan.FromSeconds(period) : null,
        Heading = (int?)location.Heading
    };

    public static TelegramPoll Poll(Api.Poll poll) => new()
    {
        PollId = poll.Id, Question = poll.Question,
        Options = poll.Options.Select(option => new TelegramPollOption(option.Text, (int)option.VoterCount)).ToArray(),
        TotalVoterCount = (int)poll.TotalVoterCount, IsClosed = poll.IsClosed, IsAnonymous = poll.IsAnonymous,
        Type = poll.Type == "quiz" ? TelegramPollType.Quiz : TelegramPollType.Regular,
        AllowsMultipleAnswers = poll.AllowsMultipleAnswers,
        CorrectOptionIds = poll.CorrectOptionIds?.Select(id => (int)id).ToArray(), Explanation = poll.Explanation,
        CloseDate = OptionalTime(poll.CloseDate)
    };

    public static TelegramForwardOrigin? ForwardOrigin(Api.MessageOrigin? origin) => origin switch
    {
        Api.MessageOriginUser user => new TelegramForwardOrigin
            { Kind = TelegramForwardOriginKind.User, Date = Time(user.Date), User = User(user.SenderUser) },
        Api.MessageOriginHiddenUser hidden => new TelegramForwardOrigin
            { Kind = TelegramForwardOriginKind.HiddenUser, Date = Time(hidden.Date), SenderName = hidden.SenderUserName },
        Api.MessageOriginChat chat => new TelegramForwardOrigin
        {
            Kind = TelegramForwardOriginKind.Chat, Date = Time(chat.Date), Chat = Chat(chat.SenderChat),
            AuthorSignature = chat.AuthorSignature
        },
        Api.MessageOriginChannel channel => new TelegramForwardOrigin
        {
            Kind = TelegramForwardOriginKind.Channel, Date = Time(channel.Date), Chat = Chat(channel.Chat),
            MessageId = channel.MessageId, AuthorSignature = channel.AuthorSignature
        },
        _ => null
    };

    public static TelegramMessage Message(Api.Message message, JsonElement json) => new()
    {
        MessageId = message.MessageId, Chat = Chat(message.Chat),
        ThreadId = message.IsTopicMessage == true ? message.MessageThreadId : null,
        From = message.From is { } from ? User(from) : null,
        SenderChat = message.SenderChat is { } sender ? Chat(sender) : null, Date = Time(message.Date),
        EditDate = OptionalTime(message.EditDate), MediaGroupId = message.MediaGroupId,
        ForwardOrigin = ForwardOrigin(message.ForwardOrigin), IsAutomaticForward = message.IsAutomaticForward == true,
        ViaBot = message.ViaBot is { } via ? User(via) : null, AuthorSignature = message.AuthorSignature,
        BusinessConnectionId = message.BusinessConnectionId, Json = json
    };

    /// <summary>入站消息中没有通用段的 Telegram 内容。</summary>
    public static IEnumerable<MessageSegment> NativeSegments(Api.Message message)
    {
        const string platform = TelegramMapper.PlatformId;
        if (message.Venue is { } venue)
            yield return new RawSegment(platform, TelegramSegmentKinds.Venue,
                new TelegramVenue(Location(venue.Location), venue.Title, venue.Address)
                    { FoursquareId = venue.FoursquareId, GooglePlaceId = venue.GooglePlaceId });
        else if (message.Location is { } location)
            yield return new RawSegment(platform, TelegramSegmentKinds.Location, Location(location));
        if (message.Contact is { } contact)
            yield return new RawSegment(platform, TelegramSegmentKinds.Contact,
                new TelegramContact(contact.PhoneNumber, contact.FirstName)
                    { LastName = contact.LastName, UserId = contact.UserId, VCard = contact.Vcard });
        if (message.Dice is { } dice)
            yield return new RawSegment(platform, TelegramSegmentKinds.Dice, new TelegramDice(dice.Emoji, (int)dice.Value));
        if (message.Poll is { } poll)
            yield return new RawSegment(platform, TelegramSegmentKinds.Poll, Poll(poll));
        if (message.SuccessfulPayment is { } payment)
            yield return new RawSegment(platform, TelegramSegmentKinds.SuccessfulPayment, new TelegramSuccessfulPayment
            {
                Currency = payment.Currency, TotalAmount = payment.TotalAmount, InvoicePayload = payment.InvoicePayload,
                TelegramPaymentChargeId = payment.TelegramPaymentChargeId,
                ProviderPaymentChargeId = payment.ProviderPaymentChargeId, ShippingOptionId = payment.ShippingOptionId,
                SubscriptionExpirationDate = OptionalTime(payment.SubscriptionExpirationDate)
            });
        var text = message.Text ?? message.Caption;
        var entities = message.Entities ?? message.CaptionEntities;
        // 只保留真正的格式；话题标签、命令、链接等由 Telegram 自动识别，纯文本已足够表达。
        if (text is not null && entities?.Any(entity => FormattingEntities.Contains(entity.Type)) == true)
            yield return new RawSegment(platform, TelegramSegmentKinds.FormattedText, new TelegramFormattedText(text,
                entities.Select(entity => new TelegramTextEntity(entity.Type, (int)entity.Offset, (int)entity.Length)
                {
                    Url = entity.Url, User = entity.User is { } user ? User(user) : null, Language = entity.Language,
                    CustomEmojiId = entity.CustomEmojiId
                }).ToArray()));
    }

    /// <summary>发送者设置的格式实体；不含 Telegram 自动识别的 mention、hashtag、bot_command、url 等。</summary>
    private static readonly IReadOnlySet<string> FormattingEntities = new HashSet<string>(StringComparer.Ordinal)
    {
        "bold", "italic", "underline", "strikethrough", "spoiler", "code", "pre", "text_link", "text_mention",
        "custom_emoji", "blockquote", "expandable_blockquote", "date_time"
    };

    /// <summary>已转换为类型化段或 <see cref="TelegramMessage"/> 资料的字段，不再作为原始段上报。</summary>
    public static readonly IReadOnlySet<string> ModeledMessageFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "location", "venue", "contact", "dice", "poll", "successful_payment", "edit_date", "media_group_id",
        "forward_origin", "is_automatic_forward", "via_bot", "author_signature", "business_connection_id",
        "is_topic_message", "has_protected_content", "is_from_offline", "link_preview_options", "reply_markup",
        "sender_boost_count", "effect_id", "show_caption_above_media"
    };

    public static TelegramSentMessage Sent(Api.Message message) => new()
    {
        MessageId = message.MessageId.ToString(CultureInfo.InvariantCulture), Channel = Channel(message),
        Timestamp = Time(message.Date), MediaGroupId = message.MediaGroupId
    };

    // ─── 出站 ───

    public static string? ParseMode(TelegramParseMode mode) => mode switch
    {
        TelegramParseMode.MarkdownV2 => "MarkdownV2",
        TelegramParseMode.Html => "HTML",
        _ => null
    };

    public static Api.ChatPermissions Permissions(TelegramChatPermissions value) => new()
    {
        CanSendMessages = value.CanSendMessages, CanSendAudios = value.CanSendAudios,
        CanSendDocuments = value.CanSendDocuments, CanSendPhotos = value.CanSendPhotos,
        CanSendVideos = value.CanSendVideos, CanSendVideoNotes = value.CanSendVideoNotes,
        CanSendVoiceNotes = value.CanSendVoiceNotes, CanSendPolls = value.CanSendPolls,
        CanSendOtherMessages = value.CanSendOtherMessages, CanAddWebPagePreviews = value.CanAddWebPagePreviews,
        CanReactToMessages = value.CanReactToMessages, CanChangeInfo = value.CanChangeInfo,
        CanInviteUsers = value.CanInviteUsers, CanPinMessages = value.CanPinMessages,
        CanManageTopics = value.CanManageTopics
    };

    public static Api.InlineKeyboardMarkup? InlineKeyboard(TelegramInlineKeyboard? keyboard) => keyboard is null
        ? null
        : new Api.InlineKeyboardMarkup
        {
            InlineKeyboard = keyboard.Rows.Select(row => (IReadOnlyList<Api.InlineKeyboardButton>)row.Select(InlineButton).ToArray()).ToArray()
        };

    private static Api.InlineKeyboardButton InlineButton(TelegramInlineButton button)
    {
        var actions = new object?[]
        {
            button.Url, button.CallbackData, button.WebAppUrl, button.SwitchInlineQuery,
            button.SwitchInlineQueryCurrentChat, button.CopyText, button.Pay ? true : null
        }.Count(action => action is not null);
        if (actions != 1) throw new ArgumentException($"内联按钮“{button.Text}”必须且只能设置一种动作。");
        return new Api.InlineKeyboardButton
        {
            Text = button.Text, Url = button.Url, CallbackData = button.CallbackData,
            WebApp = button.WebAppUrl is { } webApp ? new Api.WebAppInfo { Url = webApp } : null,
            SwitchInlineQuery = button.SwitchInlineQuery,
            SwitchInlineQueryCurrentChat = button.SwitchInlineQueryCurrentChat,
            CopyText = button.CopyText is { } copy ? new Api.CopyTextButton { Text = copy } : null,
            Pay = button.Pay ? true : null
        };
    }

    public static Api.ReplyMarkup? ReplyMarkup(TelegramReplyMarkup? markup) => markup switch
    {
        null => null,
        TelegramInlineKeyboard inline => InlineKeyboard(inline),
        TelegramReplyKeyboard keyboard => new Api.ReplyKeyboardMarkup
        {
            Keyboard = keyboard.Rows.Select(row => (IReadOnlyList<Api.KeyboardButton>)row.Select(button => new Api.KeyboardButton
            {
                Text = button.Text, RequestContact = button.RequestContact ? true : null,
                RequestLocation = button.RequestLocation ? true : null,
                RequestPoll = button.RequestPoll || button.RequestPollType is not null
                    ? new Api.KeyboardButtonPollType { Type = button.RequestPollType switch
                    {
                        TelegramPollType.Quiz => "quiz", TelegramPollType.Regular => "regular", _ => null
                    } }
                    : null,
                WebApp = button.WebAppUrl is { } webApp ? new Api.WebAppInfo { Url = webApp } : null
            }).ToArray()).ToArray(),
            IsPersistent = keyboard.IsPersistent ? true : null, ResizeKeyboard = keyboard.ResizeKeyboard ? true : null,
            OneTimeKeyboard = keyboard.OneTimeKeyboard ? true : null,
            InputFieldPlaceholder = keyboard.InputFieldPlaceholder, Selective = keyboard.Selective ? true : null
        },
        TelegramRemoveKeyboard remove => new Api.ReplyKeyboardRemove { RemoveKeyboard = true, Selective = remove.Selective ? true : null },
        TelegramForceReply force => new Api.ForceReply
        {
            ForceReplyValue = true, InputFieldPlaceholder = force.InputFieldPlaceholder,
            Selective = force.Selective ? true : null
        },
        _ => throw new NotSupportedException("未知的 Telegram 键盘类型：" + markup.GetType().Name)
    };

    public static Api.BotCommandScope? Scope(TelegramCommandScope? scope)
    {
        if (scope is null) return null;
        Api.ChatId Chat() => ParseChat(scope.ChatId ?? throw new ArgumentException("该命令范围需要 ChatId。")).Chat;
        return scope.Kind switch
        {
            TelegramCommandScopeKind.Default => new Api.BotCommandScopeDefault(),
            TelegramCommandScopeKind.AllPrivateChats => new Api.BotCommandScopeAllPrivateChats(),
            TelegramCommandScopeKind.AllGroupChats => new Api.BotCommandScopeAllGroupChats(),
            TelegramCommandScopeKind.AllChatAdministrators => new Api.BotCommandScopeAllChatAdministrators(),
            TelegramCommandScopeKind.Chat => new Api.BotCommandScopeChat { ChatId = Chat() },
            TelegramCommandScopeKind.ChatAdministrators => new Api.BotCommandScopeChatAdministrators { ChatId = Chat() },
            TelegramCommandScopeKind.ChatMember => new Api.BotCommandScopeChatMember
            {
                ChatId = Chat(), UserId = TelegramMapper.ParseId(scope.UserId ?? throw new ArgumentException("该命令范围需要 UserId。"), "userId")
            },
            _ => throw new NotSupportedException("未知的命令范围：" + scope.Kind)
        };
    }

    /// <summary>会话参数：数字 ID、@username 或 SDK 频道 ID（chatId:threadId）。</summary>
    public static (Api.ChatId Chat, long? ThreadId) ParseChat(string chatId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        if (chatId.StartsWith('@')) return (chatId, null);
        var (chat, thread) = TelegramMapper.ParseChannelId(chatId);
        return (chat, thread);
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

    /// <summary>资源 URI 转为 Bot API 文件：file_id（可带 tgfile://）和 HTTP(S) 交给 Telegram，本地与 base64 内容由适配器上传。</summary>
    public static async Task<Api.InputFile> InputFileAsync(string uri, string? fileName, CancellationToken token, HttpClient? downloads = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        if (uri.StartsWith("tgfile://", StringComparison.Ordinal)) return Api.InputFile.FromFileId(uri[9..]);
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https")
            return Api.InputFile.FromUrl(uri);
        if (Path.IsPathFullyQualified(uri) || uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || uri.StartsWith("data:", StringComparison.Ordinal) || uri.StartsWith("base64://", StringComparison.Ordinal))
        {
            var file = await ResourceReader.ReadAsync(uri, fileName, token, downloads);
            return Api.InputFile.FromBytes(file.Bytes, file.FileName);
        }
        return Api.InputFile.FromFileId(uri);
    }
}
