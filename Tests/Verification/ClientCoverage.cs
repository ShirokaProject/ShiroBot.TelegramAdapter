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

        await LargeFilesAsync(token);

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

    /// <summary>官方服务器拒绝超过 50 MB 的文件；自建服务器流式上传本地大文件，远程大文件经临时文件上传后删除。</summary>
    private static async Task LargeFilesAsync(string token)
    {
        const long size = 60L * 1024 * 1024;
        var path = Path.Combine(Path.GetTempPath(), "shirobot-verify-" + Guid.NewGuid().ToString("N") + ".mp4");
        await using (var file = File.Create(path)) file.SetLength(size);
        try
        {
            var video = new ShiroBot.SDK.Models.VideoSegment(path);
            var channel = new ShiroBot.SDK.Models.Channel("-100", ShiroBot.SDK.Models.ChannelType.Group);

            using (var official = new TelegramClient(token, handler: new UploadCounter(0)))
            {
                try
                {
                    await new ShiroBot.TelegramAdapter.Services.TelegramMessageService(official).SendMessageAsync(channel, [video]);
                    throw new Exception("60 MB upload accepted by the official server.");
                }
                catch (NotSupportedException error)
                {
                    Check(error.Message.Contains("60 MB") && error.Message.Contains("API Base URL"), "Oversize message is not actionable: " + error.Message);
                }
            }

            var local = new UploadCounter(size);
            using (var client = new TelegramClient(token, "http://127.0.0.1:8081", local))
                await new ShiroBot.TelegramAdapter.Services.TelegramMessageService(client).SendMessageAsync(channel, [video]);
            Check(local.UploadedBytes >= size && local.Methods.Contains("sendVideo"), "Large local file not uploaded to the local server.");

            var temporaryBefore = Directory.GetFiles(Path.GetTempPath(), "shirobot-telegram-*").Length;
            var remote = new UploadCounter(size);
            using (var client = new TelegramClient(token, "http://127.0.0.1:8081", remote))
                await new ShiroBot.TelegramAdapter.Services.TelegramMessageService(client).SendMessageAsync(channel,
                    [new ShiroBot.SDK.Models.VideoSegment("https://cdn.example/big.mp4")]);
            Check(remote.UploadedBytes >= size, "Large remote file not uploaded.");
            Check(Directory.GetFiles(Path.GetTempPath(), "shirobot-telegram-*").Length == temporaryBefore, "Temporary download not deleted.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>为远程下载返回指定大小的内容，统计上传的字节数而不缓存请求体。</summary>
    private sealed class UploadCounter(long downloadSize) : HttpMessageHandler
    {
        public long UploadedBytes { get; private set; }
        public List<string> Methods { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host == "cdn.example")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ZeroStream(downloadSize)) };
            Methods.Add(request.RequestUri.Segments.Last());
            if (request.Content is not null)
            {
                await using var body = await request.Content.ReadAsStreamAsync(token);
                var buffer = new byte[81920];
                int count;
                while ((count = await body.ReadAsync(buffer, token)) != 0) UploadedBytes += count;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true,"result":{"message_id":1,"date":1,"chat":{"id":-100,"type":"supergroup"}}}""",
                    Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ZeroStream(long length) : Stream
    {
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = (int)Math.Min(count, length - _position);
            Array.Clear(buffer, offset, read);
            _position += read;
            return read;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
