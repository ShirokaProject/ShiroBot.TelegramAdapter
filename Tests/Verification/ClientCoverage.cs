using System.Net;
using System.Text;
using ShiroBot.TelegramAdapter;

/// <summary>真实格式的 Token（含冒号）、自建 API 地址与代理设置。</summary>
internal static class ClientCoverage
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    public static async Task RunAsync()
    {
        var handler = new UriRecorder();
        const string token = "9239778336:AAFq-Example_Token";
        using (var client = new TelegramClient(token, handler: handler))
        {
            await client.CallAsync<System.Text.Json.JsonElement>("getMe");
            Check(handler.Last?.AbsoluteUri == $"https://api.telegram.org/bot{token}/getMe",
                "Token with a colon must stay in the path: " + handler.Last);
            Check(client.GetFileDownloadUrl("photos/a.jpg") == $"https://api.telegram.org/file/bot{token}/photos/a.jpg",
                "File download URL is wrong.");
        }
        using (var client = new TelegramClient(" " + token + " ", "http://127.0.0.1:8081/custom/", handler))
        {
            await client.CallAsync<System.Text.Json.JsonElement>("getMe");
            Check(handler.Last?.AbsoluteUri == $"http://127.0.0.1:8081/custom/bot{token}/getMe", "Custom API root or token trimming lost.");
        }

        // 纯文本发送不能带显式 null 参数（Telegram 拒绝 "parse_mode": null）。
        using (var client = new TelegramClient(token, handler: handler))
        {
            var messages = new ShiroBot.TelegramAdapter.Services.TelegramMessageService(client);
            await messages.SendMessageAsync(new ShiroBot.SDK.Models.Channel("-1004332876562", ShiroBot.SDK.Models.ChannelType.Group),
                [new ShiroBot.SDK.Models.TextSegment("status")]);
            using var body = System.Text.Json.JsonDocument.Parse(handler.Body!);
            var nulls = body.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == System.Text.Json.JsonValueKind.Null)
                .Select(p => p.Name).ToArray();
            Check(handler.Last!.AbsolutePath.EndsWith("/sendMessage") && nulls.Length == 0
                  && body.RootElement.GetProperty("text").GetString() == "status",
                "sendMessage sent explicit nulls: " + string.Join(", ", nulls));
        }

        // 错误参数保留限流等待时间与迁移后的 chat_id。
        using (var client = new TelegramClient(token, handler: new ErrorHandler()))
        {
            try
            {
                await client.InvokeAsync("sendMessage", new System.Text.Json.Nodes.JsonObject { ["chat_id"] = 1, ["text"] = "x" });
                throw new Exception("Bot API error ignored.");
            }
            catch (TelegramApiException error)
            {
                Check(error.ErrorCode == 429 && error.RetryAfter == TimeSpan.FromSeconds(3) && error.MigrateToChatId == -1001,
                    "Bot API error parameters lost.");
            }
        }

        // 长轮询订阅全部 Update 种类，原始 Update 先于 SDK 事件触发。
        var polling = new UriRecorder();
        using (var client = new TelegramClient(token, handler: polling))
        {
            using var stop = new CancellationTokenSource();
            string? raw = null;
            client.RawUpdateReceived += update => { raw = update.GetRawText(); stop.Cancel(); return Task.CompletedTask; };
            try { await client.RunLongPollingAsync(stop.Token); }
            catch (OperationCanceledException) { }
            using var request = System.Text.Json.JsonDocument.Parse(polling.Body!);
            var allowed = request.RootElement.GetProperty("allowed_updates").EnumerateArray().Select(u => u.GetString()).ToArray();
            Check(allowed.Length == TelegramClient.UpdateTypes.Count && allowed.Contains("widget_query") && allowed.Contains("business_message")
                  && raw?.Contains("\"update_id\":9") == true, "Polling does not subscribe to every update kind.");
        }

        var proxy = TelegramClient.CreateProxy("socks5://user:p%40ss@127.0.0.1:7891")!;
        var credentials = (NetworkCredential)proxy.Credentials!;
        Check(proxy.Address!.AbsoluteUri == "socks5://127.0.0.1:7891/" && credentials.UserName == "user" && credentials.Password == "p@ss",
            "SOCKS proxy with credentials not parsed.");
        Check(TelegramClient.CreateProxy("http://127.0.0.1:7890")!.Address!.Port == 7890 && TelegramClient.CreateProxy("  ") is null,
            "HTTP proxy or empty proxy wrong.");
        foreach (var bad in new[] { "127.0.0.1:7890", "ftp://proxy", "socks5://" })
        {
            try { TelegramClient.CreateProxy(bad); throw new Exception("Invalid proxy accepted: " + bad); }
            catch (ArgumentException) { }
        }
        try { using var _ = new TelegramClient("bad token/with space"); throw new Exception("Malformed token accepted."); }
        catch (ArgumentException) { }
        Console.WriteLine("Telegram client: real token URLs, custom API root and proxy settings verified.");
    }

    private sealed class ErrorHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"ok":false,"error_code":429,"description":"Too Many Requests","parameters":{"retry_after":3,"migrate_to_chat_id":-1001}}""",
                    Encoding.UTF8, "application/json")
            });
    }

    private sealed class UriRecorder : HttpMessageHandler
    {
        public Uri? Last { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Last = request.RequestUri;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
            var path = request.RequestUri!.AbsolutePath;
            var result = path.EndsWith("/sendMessage")
                ? """{"message_id":1,"date":1,"chat":{"id":-1004332876562,"type":"supergroup"}}"""
                : path.EndsWith("/getUpdates")
                    ? """[{"update_id":9,"message":{"message_id":1,"date":1,"chat":{"id":1,"type":"private"},"text":"hi"}}]"""
                    : """{"id":1,"is_bot":true,"first_name":"Bot"}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json")
            };
        }
    }
}
