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

    private sealed class UriRecorder : HttpMessageHandler
    {
        public Uri? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Last = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true,"result":{"id":1,"is_bot":true,"first_name":"Bot"}}""", Encoding.UTF8, "application/json")
            });
        }
    }
}
