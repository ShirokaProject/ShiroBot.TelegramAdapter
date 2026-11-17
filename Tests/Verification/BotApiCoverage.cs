using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ShiroBot.TelegramAdapter;
using ShiroBot.TelegramAdapter.BotApi;
using Api = ShiroBot.Telegram.Model.BotApi;

internal static class BotApiCoverage
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static string Pascal(string name) => string.Concat(name.Split('_').Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

    public static async Task RunAsync()
    {
        CheckSpecCoverage();

        var http = new Recorder();
        using var client = new TelegramClient("test-token", handler: http);
        ITelegramBotApi api = new TelegramBotApi(client);

        // JSON request: unions are written with their runtime type, nulls are omitted.
        http.Next = """{"message_id":5,"date":1,"chat":{"id":-100,"type":"supergroup"}}""";
        var sent = await api.SendMessageAsync("hi", chatId: -100, replyMarkup: new Api.InlineKeyboardMarkup
        {
            InlineKeyboard = [[new Api.InlineKeyboardButton { Text = "Go", Url = "https://example.org" }]]
        });
        Check(sent.MessageId == 5 && sent.Chat.Id == -100, "Typed Message result lost.");
        var body = JsonDocument.Parse(http.Body!).RootElement;
        Check(http.Method == "sendMessage" && body.GetProperty("chat_id").GetInt64() == -100 && body.GetProperty("text").GetString() == "hi"
              && body.GetProperty("reply_markup").GetProperty("inline_keyboard")[0][0].GetProperty("url").GetString() == "https://example.org"
              && !body.TryGetProperty("parse_mode", out _), "sendMessage JSON payload is wrong.");

        http.Next = "true";
        await api.SetMessageReactionAsync("@channel", 5, [new Api.ReactionTypeEmoji { Emoji = "👍" }]);
        body = JsonDocument.Parse(http.Body!).RootElement;
        Check(body.GetProperty("chat_id").GetString() == "@channel"
              && body.GetProperty("reaction")[0].GetProperty("type").GetString() == "emoji", "Union discriminator or username chat ID lost.");

        // Top-level upload.
        http.Next = """{"message_id":6,"date":1,"chat":{"id":1,"type":"private"}}""";
        await api.SendPhotoAsync(Api.InputFile.FromBytes("png"u8.ToArray(), "a.png"), chatId: 1, caption: "cap");
        Check(http.Multipart && http.Body!.Contains("name=photo; filename=a.png") && http.Body.Contains("cap"), "Top-level upload not multipart.");

        // Nested uploads use attach://.
        http.Next = """[{"message_id":7,"date":1,"chat":{"id":1,"type":"private"}}]""";
        await api.SendMediaGroupAsync(1, [
            new Api.InputMediaPhoto { Media = Api.InputFile.FromBytes("one"u8.ToArray(), "one.jpg") },
            new Api.InputMediaPhoto { Media = "existing-file-id" }
        ]);
        Check(http.Multipart && http.Body!.Contains("attach://shirobot_file0") && http.Body.Contains("name=shirobot_file0; filename=one.jpg")
              && http.Body.Contains("existing-file-id") && http.Body.Contains("\"type\":\"photo\""), "Nested attach:// upload is wrong.");

        // Union responses.
        http.Next = """{"status":"administrator","user":{"id":7,"is_bot":false,"first_name":"A"},"can_be_edited":false,"is_anonymous":false,"can_manage_chat":true}""";
        var member = await api.GetChatMemberAsync(-100, 7);
        Check(member is Api.ChatMemberAdministrator { CanManageChat: true } && member.User.Id == 7, "ChatMember union not resolved.");
        http.Next = """{"status":"future_status","user":{"id":8,"is_bot":false,"first_name":"B"},"extra":1}""";
        member = await api.GetChatMemberAsync(-100, 8);
        Check(member is Api.ChatMemberUnknown { Status: "future_status" } unknown && unknown.AdditionalData!.ContainsKey("extra"),
            "Unknown union value was not preserved.");

        // Edit methods return Message or True.
        http.Next = "true";
        Check(await api.EditMessageTextAsync(inlineMessageId: "inline", text: "x") is null, "True result of an inline edit was not mapped to null.");

        // Raw call for future methods.
        http.Next = """{"future":true}""";
        var raw = await api.CallAsync("futureMethod", new Dictionary<string, object?> { ["a"] = 1 });
        Check(raw.GetProperty("future").GetBoolean() && http.Method == "futureMethod", "Raw Bot API call failed.");

        // Errors keep Telegram's parameters.
        http.Error = """{"ok":false,"error_code":429,"description":"Too Many Requests","parameters":{"retry_after":3}}""";
        try
        {
            await api.GetMeAsync();
            throw new Exception("Bot API error ignored.");
        }
        catch (TelegramApiException error)
        {
            Check(error.ErrorCode == 429 && error.RetryAfter == TimeSpan.FromSeconds(3), "Bot API error parameters lost.");
        }

        // Rich text, inaccessible messages and every Update kind.
        var update = JsonSerializer.Deserialize<Api.Update>("""
            {"update_id":1,"callback_query":{"id":"q","from":{"id":1,"is_bot":false,"first_name":"A"},"chat_instance":"c",
             "message":{"chat":{"id":1,"type":"private"},"message_id":3,"date":0}}}
            """, Api.BotApiJson.Options)!;
        Check(update.CallbackQuery!.Message is Api.InaccessibleMessage { MessageId: 3 }, "Inaccessible message not resolved.");
        var quote = JsonSerializer.Deserialize<Api.TextQuote>("""
            {"text":"t","position":0,"rich_texts":["plain",[{"type":"bold","text":"b"}],{"type":"italic","text":"i"}]}
            """, Api.BotApiJson.Options)!;
        Check(quote.RichTexts is [Api.RichTextPlain { Text: "plain" }, Api.RichTextList { Items: [Api.RichTextBold] }, Api.RichTextItalic],
            "RichText union not resolved.");
        Check(JsonSerializer.Serialize<Api.RichText>(new Api.RichTextPlain("x"), Api.BotApiJson.Options) == "\"x\"", "Plain RichText not written as a string.");
        Check(Api.BotApiInfo.UpdateTypes.Contains("widget_query") && Api.BotApiInfo.UpdateTypes.Contains("business_message"),
            "allowed_updates misses Bot API update kinds.");

        // Typed updates from the polling loop.
        var polling = new Recorder { Next = """[{"update_id":9,"message":{"message_id":1,"date":1,"chat":{"id":1,"type":"private"},"text":"ping"}}]""" };
        using var pollingClient = new TelegramClient("test-token", handler: polling);
        var pollingApi = new TelegramBotApi(pollingClient);
        using var stop = new CancellationTokenSource();
        Api.Update? typed = null;
        pollingApi.UpdateReceived += u =>
        {
            typed = u;
            stop.Cancel();
            return Task.CompletedTask;
        };
        try { await pollingClient.RunLongPollingAsync(stop.Token); }
        catch (OperationCanceledException) { }
        Check(typed?.Message?.Text == "ping", "Typed update not raised.");
        Check(JsonDocument.Parse(polling.Body!).RootElement.GetProperty("allowed_updates").GetArrayLength() == Api.BotApiInfo.UpdateTypes.Count,
            "Polling does not subscribe to every update kind.");

        Console.WriteLine($"Telegram Bot API {Api.BotApiInfo.Version}: every method and type generated; JSON, multipart, unions, errors and typed updates verified.");
    }

    private static void CheckSpecCoverage()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "scripts/codegen/telegram-bot-api.json")))
            directory = directory.Parent;
        using var spec = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            directory?.FullName ?? throw new Exception("Bot API spec not found."), "scripts/codegen/telegram-bot-api.json")));
        var api = typeof(ITelegramBotApi);
        var missing = new List<string>();
        foreach (var method in spec.RootElement.GetProperty("methods").EnumerateObject())
        {
            var expected = method.Value.TryGetProperty("fields", out var fields) ? fields.GetArrayLength() : 0;
            var found = api.GetMethod(Pascal(method.Name) + "Async");
            if (found is null || found.GetParameters().Length != expected + 1) missing.Add(method.Name);
        }
        var model = typeof(Api.BotApiObject).Assembly;
        foreach (var type in spec.RootElement.GetProperty("types").EnumerateObject())
            if (model.GetType("ShiroBot.Telegram.Model.BotApi." + type.Name) is null)
                missing.Add(type.Name);
        Check(missing.Count == 0, "Bot API members not generated: " + string.Join(", ", missing));
        Check(typeof(TelegramBotApi).GetInterfaceMap(api).TargetMethods.All(m => m.DeclaringType == typeof(TelegramBotApi)),
            "ITelegramBotApi is not fully implemented.");
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public string Next { get; set; } = "true";
        public string? Error { get; set; }
        public string? Method { get; private set; }
        public string? Body { get; private set; }
        public bool Multipart { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Method = request.RequestUri!.Segments.Last();
            Multipart = request.Content is MultipartFormDataContent;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
            var response = Error ?? "{\"ok\":true,\"result\":" + Next + "}";
            var status = Error is null ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests;
            Error = null;
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
