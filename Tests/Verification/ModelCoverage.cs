using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShiroBot.Model.Telegram;
using ShiroBot.SDK.Models;
using ShiroBot.TelegramAdapter;
using ShiroBot.TelegramAdapter.Services;

/// <summary>宿主共享 ShiroBot.Model.Telegram 契约的实现。</summary>
internal static class ModelCoverage
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static async Task Throws<T>(Func<Task> action, string message) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception(message);
    }

    public static async Task RunAsync()
    {
        foreach (var contract in new[] { typeof(ITelegramChatApi), typeof(ITelegramMessageApi), typeof(ITelegramInlineApi),
                     typeof(ITelegramPaymentApi), typeof(ITelegramBotProfileApi), typeof(ITelegramRawApi) })
            Check(typeof(TelegramModelApi).GetInterfaceMap(contract).TargetMethods.All(m => m.DeclaringType == typeof(TelegramModelApi)),
                $"TelegramModelApi inherits an unimplemented default member of {contract.Name}.");

        var http = new Transport();
        using var client = new TelegramClient("test-token", handler: http);
        var mapper = new TelegramMapper();
        var interactions = new TelegramInteractionService(client, autoAnswer: false);
        var model = new TelegramModelApi(client, mapper, interactions);
        var group = new Channel("-100:42", ChannelType.Thread);

        // 原生内容：投票、键盘、引用。
        var sent = await model.SendAsync(group, new TelegramPollContent("Q?", ["a", "b"])
        {
            Type = TelegramPollType.Quiz, CorrectOptionIds = [1], IsAnonymous = false
        }, new TelegramSendOptions
        {
            ReplyToMessageId = "9", DisableNotification = true,
            ReplyMarkup = new TelegramReplyKeyboard([[new TelegramKeyboardButton("Hi") { RequestPollType = TelegramPollType.Quiz }]])
        });
        var body = http.Json;
        Check(http.Method == "sendPoll" && body.GetProperty("chat_id").GetInt64() == -100
              && body.GetProperty("message_thread_id").GetInt64() == 42 && body.GetProperty("type").GetString() == "quiz"
              && body.GetProperty("options")[1].GetProperty("text").GetString() == "b"
              && body.GetProperty("correct_option_ids")[0].GetInt32() == 1
              && body.GetProperty("reply_parameters").GetProperty("message_id").GetInt64() == 9
              && body.GetProperty("reply_markup").GetProperty("keyboard")[0][0].GetProperty("request_poll").GetProperty("type").GetString() == "quiz"
              && body.GetProperty("reply_markup").GetProperty("resize_keyboard").GetBoolean()
              && body.GetProperty("disable_notification").GetBoolean(), "sendPoll payload is wrong.");
        Check(sent is [{ MessageId: "10", Channel.Id: "-100:42" }], "Sent message not mapped to the topic channel.");

        // 相册：本地内容通过 attach:// 上传，file_id 原样发送。
        var album = await model.SendAsync(group, new TelegramMediaGroupContent([
            new TelegramMediaItem(TelegramMediaKind.Photo, "base64://aGVsbG8=") { FileName = "a.jpg", HasSpoiler = true },
            new TelegramMediaItem(TelegramMediaKind.Photo, "tgfile://known") { Caption = "two" }
        ]));
        Check(http.Method == "sendMediaGroup" && http.Multipart!.Contains("attach://shirobot_file0")
              && http.Multipart.Contains("filename=a.jpg") && http.Multipart.Contains("\"media\":\"known\"")
              && album.Count == 2 && album[0].MediaGroupId == "album", "Media group upload is wrong.");

        await Throws<NotSupportedException>(() => model.SendAsync(group,
            new TelegramInvoiceContent("t", "d", "p", "XTR", [new("x", 1)]),
            new TelegramSendOptions { ReplyMarkup = new TelegramForceReply() }), "Invoice accepted a non-inline keyboard.");
        await Throws<ArgumentException>(() => model.SendAsync(group, new TelegramTextContent("x"), new TelegramSendOptions
        {
            ReplyMarkup = new TelegramInlineKeyboard([[new TelegramInlineButton("both") { Url = "https://a", CallbackData = "b" }]])
        }), "Inline button with two actions accepted.");

        await model.SendAsync(group, new TelegramInvoiceContent("Coffee", "A cup", "order-1", "XTR", [new("Coffee", 5)]),
            new TelegramSendOptions { ReplyMarkup = new TelegramInlineKeyboard([[new TelegramInlineButton("Pay 5 ⭐") { Pay = true }]]) });
        Check(http.Method == "sendInvoice" && http.Json.GetProperty("currency").GetString() == "XTR"
              && http.Json.GetProperty("reply_markup").GetProperty("inline_keyboard")[0][0].GetProperty("pay").GetBoolean(),
            "Stars invoice payload is wrong.");

        // 会话管理。
        http.Results["getChatMember"] = """{"status":"administrator","user":{"id":7,"is_bot":false,"first_name":"A","language_code":"zh-hans"},"can_be_edited":true,"is_anonymous":false,"can_manage_chat":true,"can_delete_messages":true,"can_manage_video_chats":false,"can_restrict_members":true,"can_promote_members":false,"can_change_info":true,"can_invite_users":true,"can_post_stories":false,"can_edit_stories":false,"can_delete_stories":false,"can_send_welcome_messages":false,"can_pin_messages":true,"custom_title":"Mod"}""";
        var member = await model.GetMemberAsync("-100:42", "7");
        Check(member is { Status: TelegramMemberStatus.Administrator, CustomTitle: "Mod", CanBeEdited: true, AdminRights.CanRestrictMembers: true, AdminRights.CanPinMessages: true }
              && member.User.LanguageCode == "zh-hans" && http.Json.GetProperty("chat_id").GetInt64() == -100, "Administrator mapping is wrong.");
        http.Results["getChatMember"] = """{"status":"restricted","user":{"id":8,"is_bot":false,"first_name":"R"},"is_member":true,"can_send_messages":false,"can_send_audios":false,"can_send_documents":false,"can_send_photos":false,"can_send_videos":false,"can_send_video_notes":false,"can_send_voice_notes":false,"can_send_polls":false,"can_send_other_messages":false,"can_add_web_page_previews":false,"can_react_to_messages":true,"can_edit_tag":false,"can_change_info":false,"can_invite_users":true,"can_pin_messages":false,"can_manage_topics":false,"until_date":0}""";
        member = await model.GetMemberAsync("@group", "8");
        Check(member is { Status: TelegramMemberStatus.Restricted, IsMember: true, UntilDate: null, Permissions.CanReactToMessages: true }
              && http.Json.GetProperty("chat_id").GetString() == "@group", "Restricted mapping or username chat ID is wrong.");

        await model.RestrictMemberAsync("-100", "8", TelegramChatPermissions.ReadOnly, DateTimeOffset.FromUnixTimeSeconds(2_000_000_000));
        Check(http.Method == "restrictChatMember" && http.Json.GetProperty("until_date").GetInt64() == 2_000_000_000
              && !http.Json.GetProperty("permissions").GetProperty("can_send_messages").GetBoolean()
              && !http.Json.GetProperty("permissions").TryGetProperty("can_invite_users", out _), "Restriction payload is wrong.");
        await model.UnpinAllMessagesAsync("-100:42");
        Check(http.Method == "unpinAllForumTopicMessages" && http.Json.GetProperty("message_thread_id").GetInt64() == 42,
            "Unpinning a topic unpinned the whole chat.");
        await model.SetPhotoAsync("-100", "base64://aGVsbG8=");
        Check(http.Method == "setChatPhoto" && http.Multipart!.Contains("name=photo"), "Chat photo not uploaded.");
        http.Results["createForumTopic"] = """{"message_thread_id":77,"name":"T","icon_color":7322096}""";
        Check((await model.CreateTopicAsync("-100", "T")).ThreadId == 77, "Topic not mapped.");

        await model.SetCommandsAsync([new TelegramBotCommand("start", "Start")],
            new TelegramCommandScope(TelegramCommandScopeKind.ChatMember) { ChatId = "-100", UserId = "7" }, "zh");
        Check(http.Method == "setMyCommands" && http.Json.GetProperty("scope").GetProperty("type").GetString() == "chat_member"
              && http.Json.GetProperty("scope").GetProperty("user_id").GetInt64() == 7, "Command scope is wrong.");

        await model.AnswerInlineQueryAsync("q", [
            new TelegramInlineArticle("1", "Title", "*hi*") { ParseMode = TelegramParseMode.MarkdownV2 },
            new TelegramInlinePhoto("2", "https://example.org/a.jpg"),
            new TelegramInlinePhoto("3", "tgfile://cached")
        ], new TelegramInlineAnswerOptions { CacheTime = TimeSpan.Zero, IsPersonal = true });
        var results = http.Json.GetProperty("results");
        Check(results[0].GetProperty("input_message_content").GetProperty("parse_mode").GetString() == "MarkdownV2"
              && results[1].GetProperty("thumbnail_url").GetString() == "https://example.org/a.jpg"
              && results[2].GetProperty("photo_file_id").GetString() == "cached" && http.Json.GetProperty("cache_time").GetInt32() == 0,
            "Inline results are wrong.");

        await model.AnswerShippingQueryAsync("s", true, [new TelegramShippingOption("std", "Standard", [new("ship", 100)])]);
        Check(http.Json.GetProperty("shipping_options")[0].GetProperty("prices")[0].GetProperty("amount").GetInt64() == 100,
            "Shipping options lost.");

        // 原始调用。
        await using (var stream = new MemoryStream("img"u8.ToArray()))
        {
            var raw = await model.CallAsync("sendPhoto", new JsonObject { ["chat_id"] = 1, ["photo"] = "attach://cover" },
                [new TelegramUploadFile("cover", "cover.jpg", stream)]);
            Check(http.Method == "sendPhoto" && http.Multipart!.Contains("attach://cover") && http.Multipart.Contains("name=cover; filename=cover.jpg")
                  && raw.GetProperty("message_id").GetInt64() == 10, "Raw multipart call is wrong.");
        }
        Check(model.BotApiVersion == "10.4", "Raw API version missing.");

        // 事件与入站内容。
        var events = new TelegramEventService(mapper, () => interactions);
        var received = new List<BotEvent>();
        events.EventReceived += e => { received.Add(e); return Task.CompletedTask; };
        async Task Emit(string json)
        {
            using var document = JsonDocument.Parse(json);
            await (Task)typeof(TelegramEventService).GetMethod("OnRawUpdateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(events, [document.RootElement.Clone()])!;
        }

        await Emit("""
            {"update_id":1,"message":{"message_id":5,"date":100,"chat":{"id":-100,"type":"supergroup","is_forum":true},"message_thread_id":42,"is_topic_message":true,
             "from":{"id":7,"is_bot":false,"first_name":"A","is_premium":true},"media_group_id":"g","venue":{"location":{"latitude":1.5,"longitude":2.5},"title":"Cafe","address":"Street"},
             "location":{"latitude":1.5,"longitude":2.5},"forward_origin":{"type":"hidden_user","date":90,"sender_user_name":"Anon"}}}
            """);
        var located = (MessageEvent)received.Last();
        Check(located.Raw is TelegramMessage { MediaGroupId: "g", ThreadId: 42, From.IsPremium: true, Chat.IsForum: true,
                  ForwardOrigin: { Kind: TelegramForwardOriginKind.HiddenUser, SenderName: "Anon" } }
              && located.Channel.Id == "-100:42", "TelegramMessage metadata is wrong.");
        var raws = located.Segments.OfType<RawSegment>().ToArray();
        Check(raws is [{ Kind: TelegramSegmentKinds.Venue, Payload: TelegramVenue { Title: "Cafe", Location.Latitude: 1.5 } }],
            "Venue must be the only native segment: " + string.Join(", ", raws.Select(r => r.Kind)));

        await Emit("""{"update_id":2,"message":{"message_id":6,"date":1,"chat":{"id":1,"type":"private"},"from":{"id":7,"is_bot":false,"first_name":"A"},"text":"bold @x","entities":[{"type":"bold","offset":0,"length":4},{"type":"mention","offset":5,"length":2}]}}""");
        var formatted = ((MessageEvent)received.Last()).Segments.OfType<RawSegment>().Single();
        Check(formatted.Payload is TelegramFormattedText { Text: "bold @x", Entities: [{ Type: "bold" }, { Type: "mention" }] },
            "Formatted text not kept.");

        await Emit("""{"update_id":21,"message":{"message_id":7,"date":1,"chat":{"id":1,"type":"private"},"from":{"id":7,"is_bot":false,"first_name":"A"},"text":"#shiro /start https://a.b","entities":[{"type":"hashtag","offset":0,"length":6},{"type":"bot_command","offset":7,"length":6},{"type":"url","offset":14,"length":9}]}}""");
        Check(!((MessageEvent)received.Last()).Segments.OfType<RawSegment>().Any(), "Auto-detected entities must not create a formatted text segment.");

        // @username 提及解析为数字 ID：插件用 HasMention(SelfId) 判断是否被提及。
        await Emit("""{"update_id":22,"message":{"message_id":8,"date":1,"chat":{"id":-100,"type":"supergroup"},"from":{"id":4242,"is_bot":true,"first_name":"Bot","username":"MyShiroBot"},"text":"hi"}}""");
        await Emit("""{"update_id":23,"message":{"message_id":9,"date":1,"chat":{"id":-100,"type":"supergroup"},"from":{"id":7,"is_bot":false,"first_name":"A"},"text":"@myshirobot hello @stranger","entities":[{"type":"mention","offset":0,"length":11},{"type":"mention","offset":18,"length":9}]}}""");
        var mentioned = (MessageEvent)received.Last();
        Check(mentioned.HasMention("4242") && mentioned.Segments.OfType<MentionSegment>().First() is { DisplayName: "Bot" }
              && mentioned.Segments.OfType<MentionSegment>().Last() is { UserId: "stranger", DisplayName: "stranger" },
            "Username mentions not resolved: " + string.Join(", ", mentioned.Segments.OfType<MentionSegment>().Select(m => m.UserId + "/" + m.DisplayName)));

        await Emit("""{"update_id":3,"edited_message":{"message_id":6,"date":1,"edit_date":50,"chat":{"id":1,"type":"private"},"text":"new"}}""");
        Check(received.Last() is PlatformEvent { Kind: TelegramEventKinds.MessageEdited, Raw: TelegramMessageEdited
              { MessageId: "6", IsChannelPost: false, Segments: [TextSegment { Text: "new" }], UpdateId: 3 } edited }
              && edited.Time == DateTimeOffset.FromUnixTimeSeconds(50), "Edited message payload is wrong.");

        await Emit("""{"update_id":4,"chat_join_request":{"chat":{"id":-100,"type":"supergroup"},"from":{"id":9,"is_bot":false,"first_name":"J"},"user_chat_id":9,"date":5,"bio":"hi"}}""");
        Check(received.Last() is PlatformEvent { Kind: TelegramEventKinds.ChatJoinRequest, Raw: TelegramChatJoinRequest { User.UserId: 9, Bio: "hi" } },
            "Join request payload is wrong.");

        await Emit("""{"update_id":5,"my_chat_member":{"chat":{"id":-100,"type":"supergroup"},"from":{"id":7,"is_bot":false,"first_name":"A"},"date":5,"old_chat_member":{"status":"left","user":{"id":123,"is_bot":true,"first_name":"Bot"}},"new_chat_member":{"status":"member","user":{"id":123,"is_bot":true,"first_name":"Bot"}}}}""");
        Check(received.Last() is PlatformEvent { Kind: TelegramEventKinds.MyChatMemberUpdated, Raw: TelegramChatMemberUpdated
            { OldMember.Status: TelegramMemberStatus.Left, NewMember.Status: TelegramMemberStatus.Member, Actor.UserId: 7 } }, "Bot member update is wrong.");

        await Emit("""{"update_id":6,"inline_query":{"id":"iq","from":{"id":7,"is_bot":false,"first_name":"A"},"query":"cats","offset":"","chat_type":"sender"}}""");
        Check(received.Last() is PlatformEvent { Kind: TelegramEventKinds.InlineQuery, Raw: TelegramInlineQuery { QueryId: "iq", Query: "cats", ChatType: null } },
            "Inline query payload is wrong.");

        await Emit("""{"update_id":7,"poll_answer":{"poll_id":"p","user":{"id":7,"is_bot":false,"first_name":"A"},"option_ids":[0,2],"option_persistent_ids":["a","c"]}}""");
        Check(received.Last() is PlatformEvent { Raw: TelegramPollAnswer { PollId: "p", OptionIds: [0, 2] } }, "Poll answer payload is wrong.");

        await Emit("""{"update_id":8,"pre_checkout_query":{"id":"pc","from":{"id":7,"is_bot":false,"first_name":"A"},"currency":"XTR","total_amount":5,"invoice_payload":"order"}}""");
        Check(received.Last() is PlatformEvent { Raw: TelegramPreCheckoutQuery { QueryId: "pc", TotalAmount: 5, InvoicePayload: "order" } },
            "Pre-checkout payload is wrong.");

        await Emit("""{"update_id":9,"removed_chat_boost":{"chat":{"id":-100,"type":"channel"},"boost_id":"b","remove_date":5,"source":{"source":"premium","user":{"id":7,"is_bot":false,"first_name":"A"}}}}""");
        Check(received.Last() is PlatformEvent { Raw: TelegramChatBoost { IsRemoved: true, Source: "premium", User.UserId: 7 } }, "Boost payload is wrong.");

        await Emit("""{"update_id":10,"future_update":{"keep":true}}""");
        Check(received.Last() is PlatformEvent { Kind: "future_update", Raw: JsonElement }, "Unknown update lost.");

        // 关闭自动确认后由插件附带提示回答回调。
        var answers = http.Calls("answerCallbackQuery");
        await Emit("""{"update_id":11,"callback_query":{"id":"cb","from":{"id":7,"is_bot":false,"first_name":"A"},"chat_instance":"c","data":"x","message":{"message_id":1,"date":1,"chat":{"id":1,"type":"private"}}}}""");
        Check(received.Last() is InteractionEvent { IsAutomaticallyAcknowledged: false, Acknowledgement: InteractionAcknowledgement.Pending }
              && http.Calls("answerCallbackQuery") == answers && !model.AutoAnswersCallbacks, "Callback answered automatically.");
        await model.AnswerCallbackAsync("cb", new TelegramCallbackAnswer { Text = "Done", ShowAlert = true });
        Check(http.Json.GetProperty("text").GetString() == "Done" && http.Json.GetProperty("show_alert").GetBoolean(), "Callback hint lost.");
        await model.AnswerCallbackAsync("cb", new TelegramCallbackAnswer { Text = "again" });
        Check(http.Calls("answerCallbackQuery") == answers + 1, "Callback answered twice.");

        Console.WriteLine("Telegram Model 1.1: chat/message/inline/payment/profile/raw APIs, native segments and typed events verified.");
    }

    private sealed class Transport : HttpMessageHandler
    {
        private readonly List<string> _methods = [];
        public Dictionary<string, string> Results { get; } = new()
        {
            ["sendMediaGroup"] = """[{"message_id":11,"date":1,"chat":{"id":-100,"type":"supergroup"},"media_group_id":"album"},{"message_id":12,"date":1,"chat":{"id":-100,"type":"supergroup"},"media_group_id":"album"}]"""
        };
        public string? Method { get; private set; }
        public JsonElement Json { get; private set; }
        public string? Multipart { get; private set; }
        public int Calls(string method) => _methods.Count(m => m == method);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Method = request.RequestUri!.Segments.Last();
            _methods.Add(Method);
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(token);
            if (request.Content is MultipartFormDataContent) Multipart = body;
            else Json = JsonDocument.Parse(body).RootElement.Clone();
            var result = Results.TryGetValue(Method, out var configured) ? configured : Method switch
            {
                "sendPoll" or "sendMessage" or "sendInvoice" or "sendPhoto" =>
                    """{"message_id":10,"date":1,"message_thread_id":42,"is_topic_message":true,"chat":{"id":-100,"type":"supergroup"}}""",
                _ => "true"
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"ok\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json") };
        }
    }
}
