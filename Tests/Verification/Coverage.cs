using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.Telegram.Model;
using ShiroBot.TelegramAdapter;
using ShiroBot.TelegramAdapter.Services;

internal static class Coverage
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Unsupported(Func<Task> action)
    { try { await action(); throw new Exception("Unsupported API pretended to succeed."); } catch (NotSupportedException) { } }
    private static void Declared(Type concrete, params Type[] contracts)
    {
        foreach (var contract in contracts)
        {
            var map = concrete.GetInterfaceMap(contract);
            Check(map.TargetMethods.All(m => m.DeclaringType == concrete), $"{concrete.Name} inherits unimplemented default {contract.Name}.");
        }
    }
    public static async Task RunAsync()
    {
        Declared(typeof(TelegramMessageService), typeof(IMessageService), typeof(IFileService));
        Declared(typeof(TelegramChannelService), typeof(IChannelService));
        Declared(typeof(TelegramUserService), typeof(IUserService));
        Declared(typeof(TelegramInteractionService), typeof(IMessageInteractionService));
        Declared(typeof(TelegramReactionService), typeof(IMessageReactionService));
        var http = new Transport();
        using var client = new TelegramClient("test-token", handler: http);
        var mapper = new TelegramMapper();
        var interactions = new TelegramInteractionService(client);
        var events = new TelegramEventService(mapper, () => interactions);
        var messages = new TelegramMessageService(client, mapper, interactions);
        var users = new TelegramUserService(client, mapper);
        var channels = new TelegramChannelService(client, mapper);
        var adapter = new TelegramAdapter();
        foreach (var (name, service) in new (string, object)[]
        {
            ("_message", messages), ("_user", users), ("_interactions", interactions),
            ("_reactions", new TelegramReactionService(client))
        }) typeof(TelegramAdapter).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(adapter, service);
        Check(adapter.GetExtension<IFileService>() is not null && adapter.GetExtension<IMessageReactionService>() is not null
            && adapter.GetExtension<IMessageInteractionService>() is not null && adapter.GetExtension<ITelegramModelService>() is not null,
            "Optional services are not exposed through the adapter.");
        var target = new Channel("-100:42", ChannelType.Thread);
        var received = new List<BotEvent>();
        events.EventReceived += e => { received.Add(e); return Task.CompletedTask; };
        async Task Emit(string json)
        {
            var update = JsonSerializer.Deserialize<TgUpdate>(json)!;
            await (Task)typeof(TelegramEventService).GetMethod("OnUpdateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(events, [update])!;
        }
        await Emit("""{"update_id":1,"message":{"message_id":1,"date":1,"chat":{"id":-100,"type":"supergroup"},"from":{"id":7,"first_name":"First","last_name":"Last","username":"user"},"text":"hello","future_field":{"keep":true}}}""");
        var incoming = (MessageEvent)received.Last();
        Check(((TgMessage)incoming.Raw!).AdditionalData! ["future_field"].GetProperty("keep").GetBoolean(), "Unknown message fields lost.");
        Check((await users.GetUserAsync("7"))?.Id == "7" && (await users.GetTelegramUserAsync("7")).LastName == "Last", "Shared TelegramUser fields lost.");
        Check((await users.GetTelegramUserAsync()).IsBot, "Self shared Model missing.");
        Check((await users.GetTelegramMemberAsync("-100", "7")).FirstName == "First", "Member shared Model missing.");
        await Unsupported(() => users.GetUserAsync("999"));
        await Unsupported(() => users.GetFriendsAsync());
        await Unsupported(() => users.AcceptFriendRequestAsync("x"));
        await Unsupported(() => users.RejectFriendRequestAsync("x"));
        await Unsupported(() => channels.GetChannelsAsync());
        await Unsupported(() => channels.GetMembersAsync("-100"));
        await Unsupported(() => messages.GetMessageAsync(target, "1"));
        await Unsupported(() => messages.GetHistoryMessagesAsync(target));
        Check((await channels.GetMemberAsync("-100", "7"))?.Role == MemberRole.Admin, "Member role lost.");
        await channels.SetChannelNameAsync("-100", "Name");
        await channels.SetChannelNameAsync("-100:42", "Topic");
        Check(http.LastMethod == "editForumTopic", "Renaming a topic renamed the entire chat.");
        await channels.KickMemberAsync("-100", "7");
        await channels.MuteMemberAsync("-100", "7", TimeSpan.FromMinutes(5));
        await channels.LeaveChannelAsync("-100");
        await messages.DeleteMessageAsync(target, "1");
        Check(messages.GetFileCapabilities(target).UploadPublishes, "Upload publication contract wrong.");
        await messages.SendMessageAsync(target, new OutgoingMessage { Segments = [new VideoSegment("base64://aGVsbG8=")
            { ThumbnailUri = "base64://aGVsbG8=" }] });
        Check(http.LastMultipart?.Contains("thumbnail.jpg") == true, "Video thumbnail was silently discarded.");
        var uploaded = await messages.UploadAsync(target, new() { Uri = "base64://aGVsbG8=", FileName = "hello.txt" });
        Check(uploaded.IsPublished && http.LastMultipart?.Contains("hello.txt") == true && http.LastMultipart.Contains("hello"), "Multipart upload not sent.");
        await messages.SendMessageAsync(target, new OutgoingMessage { Segments = [new FileSegment(uploaded.FileId)] });
        Check(http.LastJson.GetProperty("document").GetString() == "document-id", "Uploaded file handle not reusable.");
        Check(!messages.AssessMessage(new Channel("-101", ChannelType.Group), new() { Segments = [new FileSegment(uploaded.FileId)] }).IsSupported, "Upload credential crossed channels.");
        await messages.SendMessageAsync(target, new OutgoingMessage { Segments = [new MentionSegment("7") { DisplayName = "A_B" }] });
        Check(http.LastJson.GetProperty("text").GetString() == "[A\\_B](tg://user?id=7)", "Native mention not escaped.");
        await messages.SendMessageAsync(target, new OutgoingMessage { Segments = [new EmojiSegment("sticker-id")] });
        Check(http.LastMethod == "sendSticker", "Sticker ID sent as text.");
        var fallback = await messages.SendMessageAsync(target, new OutgoingMessage
        {
            Segments = [new CardSegment { Title = "Card", Fields = [new("Field", "Value")] }],
            AllowedFallbacks = MessageFallbackOptions.CardAsText
        });
        Check(fallback.Transformations.Any(t => t.Kind == MessageTransformationKind.CardToText && t.IsDowngrade), "Card downgrade not reported.");
        Check(!messages.AssessMessage(target, new OutgoingMessage { Segments = [new MentionAllSegment()] }).IsSupported, "False native mention-all support.");
        var button = new MessageButtonLayout([new([new("button", "Click", new CallbackAction("value")), new("link", "Open", new OpenUrlAction("https://example.org"))])]);
        await messages.SendMessageAsync(target, new OutgoingMessage { Segments = [new TextSegment("buttons")], Buttons = button });
        var data = http.LastJson.GetProperty("reply_markup").GetProperty("inline_keyboard")[0][0].GetProperty("callback_data").GetString();
        await Emit(JsonSerializer.Serialize(new { update_id = 2, callback_query = new { id = "callback", from = new { id = 7, first_name = "First" }, data,
            message = new { message_id = 1, date = 1, message_thread_id = 42, chat = new { id = -100, type = "supergroup" } } } }));
        var interaction = (InteractionEvent)received.Last();
        Check(interaction.ButtonId == "button" && interaction.Data == "value" && interaction.IsAutomaticallyAcknowledged && interaction.Channel!.Id == "-100:42", "Callback mapping failed.");
        var count = http.Methods.Count(x => x == "answerCallbackQuery");
        await interactions.AcknowledgeAsync(interaction);
        Check(http.Methods.Count(x => x == "answerCallbackQuery") == count, "Acknowledgement not idempotent.");
        await messages.SendMessageAsync(target, new OutgoingMessage { Segments = [new TextSegment("reply")], ReplyToInteraction = new("instance", target, "callback") });
        Check(http.LastJson.GetProperty("reply_parameters").GetProperty("message_id").GetInt64() == 1, "Interaction reply target lost.");
        await Emit("""{"update_id":3,"message_reaction":{"chat":{"id":-100,"type":"supergroup"},"message_id":1,"old_reaction":[{"type":"emoji","emoji":"👍"}],"new_reaction":[{"type":"custom_emoji","custom_emoji_id":"9"}]}}""");
        Check(received.TakeLast(2).OfType<MessageReactionEvent>().Count() == 2, "Reaction add/remove not mapped.");
        var reactions = new TelegramReactionService(client);
        await reactions.SetReactionAsync(new("instance", target, "1"), new UnicodeReactionEmoji("👍"));
        Check(http.LastMethod == "setMessageReaction", "Reaction API missing.");
        await reactions.SetReactionAsync(new("instance", target, "1"), new PlatformReactionEmoji("9", "instance"), false);
        Check(http.LastJson.GetProperty("reaction").GetArrayLength() == 0, "Reaction removal wrong.");
        await Emit("""{"update_id":4,"edited_channel_post":{"message_id":1,"date":1,"chat":{"id":-100,"type":"channel"},"text":"edited"}}""");
        Check(received.Last() is PlatformEvent { Kind: "message_edited" }, "Edited channel post lost.");
        await Emit("""{"update_id":5,"future_update":{"keep":true}}""");
        Check(received.Last() is PlatformEvent { Kind: "future_update" }, "Unknown update lost.");
        Console.WriteLine("Telegram: every SDK API explicitly implemented; native send/upload/buttons/interaction/reaction/model tests passed.");
    }
    private sealed class Transport : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];
        public string LastMethod { get; private set; } = "";
        public JsonElement LastJson { get; private set; }
        public string? LastMultipart { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            LastMethod = request.RequestUri!.Segments.Last(); Methods.Add(LastMethod);
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(token);
            if (request.Content is MultipartFormDataContent) LastMultipart = body;
            else LastJson = JsonDocument.Parse(body).RootElement.Clone();
            var result = LastMethod switch
            {
                "getMe" => """{"id":123,"is_bot":true,"first_name":"Bot"}""",
                "getChatMember" => """{"status":"administrator","user":{"id":7,"first_name":"First","last_name":"Last","username":"user"}}""",
                "sendDocument" => """{"message_id":10,"date":1,"chat":{"id":-100,"type":"supergroup"},"document":{"file_id":"document-id","file_name":"hello.txt"}}""",
                "sendMessage" or "sendPhoto" or "sendAudio" or "sendVideo" or "sendSticker" => """{"message_id":10,"date":1,"chat":{"id":-100,"type":"supergroup"}}""",
                _ => "true"
            };
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json") };
        }
    }
}
