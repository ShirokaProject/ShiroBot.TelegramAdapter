using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Models;
using ShiroBot.TelegramAdapter;
using ShiroBot.TelegramAdapter.Services;

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}

var handler = new ApiHandler();
using var client = new TelegramClient("test-token", "https://example.invalid/api/", handler);
IMessageService messages = new TelegramMessageService(client);
IChannelService channels = new TelegramChannelService(client);
IUserService users = new TelegramUserService(client);
var topic = new Channel("-100:42", ChannelType.Thread);
var sent = await messages.SendMessageAsync(topic, new OutgoingMessage
{
    Segments = [new TextSegment("hello"), new QuoteSegment("9")]
});
Check(sent.MessageId == "10", "Sent message ID was lost.");
var request = handler.Requests.Last();
Check(request.Method == "sendMessage" && request.Body.GetProperty("chat_id").GetInt64() == -100
    && request.Body.GetProperty("message_thread_id").GetInt64() == 42
    && request.Body.GetProperty("reply_parameters").GetProperty("message_id").GetInt64() == 9, "Topic/reply payload is wrong.");
await messages.SendMessageAsync(topic, new OutgoingMessage
{
    Segments = [new TextSegment("caption"), new ImageSegment("tgfile://photo-id")]
});
request = handler.Requests.Last();
Check(request.Method == "sendPhoto" && request.Body.GetProperty("photo").GetString() == "photo-id"
    && request.Body.GetProperty("caption").GetString() == "caption", "Media payload is wrong.");
Check(!messages.AssessMessage(topic, new OutgoingMessage
    { Segments = [new ImageSegment("tgfile://one"), new ImageSegment("tgfile://two")] }).IsSupported, "Multiple media must not silently split.");
Check(!messages.AssessMessage(topic, new OutgoingMessage
    { Segments = [new TextSegment(new string('x', 1025)), new ImageSegment("tgfile://one")] }).IsSupported, "Oversize caption accepted.");
Check(messages.AssessMessage(topic, new OutgoingMessage
    { Segments = [new ImageSegment("file:///tmp/image.png")] }).IsSupported, "Local upload support missing.");
var markdown = await messages.SendMessageAsync(topic, new OutgoingMessage
{
    Segments = [new MarkdownSegment("**hello**")], AllowedFallbacks = MessageFallbackOptions.MarkdownAsText
});
Check(markdown.Transformations.Count == 0 && handler.Requests.Last().Body.GetProperty("parse_mode").GetString() == "MarkdownV2", "Native Markdown not sent.");
Check((await users.GetSelfAsync()).Id == "123", "SDK user dispatch failed.");
Check((await channels.GetChannelAsync("-100:42"))?.Type == ChannelType.Thread, "SDK channel dispatch failed.");
Check((await messages.GetResourceUrlAsync("tgfile://file-id")) == "https://example.invalid/api/file/bottest-token/photos/file.jpg", "Custom API download prefix lost.");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    var before = handler.Requests.Count;
    foreach (var operation in new Func<Task>[]
    {
        () => messages.SendMessageAsync(topic, new OutgoingMessage { Segments = [new TextSegment("hello")] }, cancelled.Token),
        () => messages.DeleteMessageAsync(topic, "10", cancelled.Token),
        () => channels.GetChannelAsync("-100", cancelled.Token),
        () => users.GetSelfAsync(cancelled.Token)
    })
    {
        try { await operation(); throw new Exception("Cancellation ignored."); }
        catch (OperationCanceledException) { }
    }
    Check(handler.Requests.Count == before, "Cancelled call reached HTTP transport.");
}

foreach (var identity in new[] { "bot-a", "bot-b" })
{
    using var pollingClient = new TelegramClient("test-token", handler: new ApiHandler());
    var mapper = new TelegramMapper();
    typeof(TelegramMapper).GetProperty("SelfId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mapper, identity);
    var events = new TelegramEventService(mapper);
    events.Attach(pollingClient);
    using var stop = new CancellationTokenSource();
    var received = new List<MessageEvent>();
    events.EventReceived += async e => { await Task.Yield(); received.Add((MessageEvent)e); };
    events.EventReceived += e =>
    {
        Check(received.Count == 1, "Async event handler was not awaited.");
        Check(e.SelfId == identity, "Bot identity leaked between instances.");
        stop.Cancel();
        return Task.CompletedTask;
    };
    await pollingClient.RunLongPollingAsync(stop.Token);
    Check(received[0].Channel.Id == "-100:42", "Inbound topic mapping failed.");
    events.Detach(pollingClient);
}
var adapter = new TelegramAdapter();
var config = new TelegramAdapterConfig { BotToken = "one" };
await adapter.OnConfigLoadedAsync(config, default);
var replacement = new TelegramAdapterConfig { BotToken = "two" };
await adapter.OnConfigChangedAsync(config, replacement, default);
Check(ReferenceEquals(adapter.CurrentConfigValue, replacement), "Configuration update lost.");
Check(((IConfigurableAdapter)adapter).ApplyMode == ConfigApplyMode.RestartComponent, "Configuration must restart polling.");
await adapter.StopAsync();
await adapter.StopAsync();
try { TelegramMapper.ParseChannelId("-100:invalid"); throw new Exception("Invalid topic accepted."); }
catch (ArgumentException) { }
await Coverage.RunAsync();
Console.WriteLine("Verified SDK HTTP dispatch, topic/reply/media payloads, capabilities, cancellation, custom API URLs, polling events, identity isolation and configuration.");

sealed class ApiHandler : HttpMessageHandler
{
    public List<(string Method, JsonElement Body)> Requests { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var method = request.RequestUri!.Segments.Last();
        using var body = JsonDocument.Parse(request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken));
        Requests.Add((method, body.RootElement.Clone()));
        var result = method switch
        {
            "sendMessage" or "sendPhoto" => """{"message_id":10,"date":1,"chat":{"id":-100,"type":"supergroup"}}""",
            "getMe" => """{"id":123,"is_bot":true,"first_name":"Bot","username":"testbot"}""",
            "getChat" => """{"id":-100,"type":"supergroup","title":"Test"}""",
            "getFile" => """{"file_id":"file-id","file_unique_id":"unique","file_path":"photos/file.jpg"}""",
            "getUpdates" => """[{"update_id":1,"message":{"message_id":1,"date":1,"message_thread_id":42,"chat":{"id":-100,"type":"supergroup"},"from":{"id":1,"first_name":"Human"},"text":"ping"}}]""",
            _ => "true"
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json")
        };
    }
}
