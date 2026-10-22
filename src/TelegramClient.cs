using System.Net.Http.Json;
using System.Text.Json;
using ShiroBot.Telegram.Model;

namespace ShiroBot.TelegramAdapter;

/// <summary>Telegram Bot API HTTP 客户端(长轮询接收 + 方法调用)。</summary>
public sealed class TelegramClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly string _token;
    private readonly Uri _apiRoot;
    private long _updateOffset;

    public event Func<TgUpdate, Task>? UpdateReceived;

    public TelegramClient(string token, string? apiBaseUrl = null, HttpMessageHandler? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _token = token;
        _apiRoot = new Uri((string.IsNullOrWhiteSpace(apiBaseUrl) ? "https://api.telegram.org" : apiBaseUrl.TrimEnd('/')) + "/");
        if (_apiRoot.Scheme is not ("https" or "http"))
            throw new ArgumentException("API Base URL 必须是 HTTP(S) 地址。", nameof(apiBaseUrl));
        _http = (handler is null ? new HttpClient() : new HttpClient(handler));
        _http.BaseAddress = new Uri(_apiRoot, $"bot{token}/");
        _http.Timeout = TimeSpan.FromSeconds(120);
    }

    public void Dispose() => _http.Dispose();

    public string GetFileDownloadUrl(string filePath) =>
        new Uri(_apiRoot, $"file/bot{_token}/{filePath}").AbsoluteUri;

    public async Task<T> CallAsync<T>(string method, object? payload = null, CancellationToken cancellationToken = default)
    {
        using var response = payload is null
            ? await _http.GetAsync(method, cancellationToken)
            : await _http.PostAsJsonAsync(method, payload, JsonOptions, cancellationToken);

        var wrapper = await response.Content.ReadFromJsonAsync<TgResponse<T>>(JsonOptions, cancellationToken)
                      ?? throw new InvalidOperationException($"Telegram API {method} 返回空响应。");

        if (!wrapper.Ok || wrapper.Result is null)
        {
            throw new InvalidOperationException(
                $"Telegram API {method} 失败: [{wrapper.ErrorCode}] {wrapper.Description}");
        }

        return wrapper.Result;
    }

    internal async Task<T> CallMultipartAsync<T>(string method, IReadOnlyDictionary<string, object?> payload,
        string field, ResourceBytes file, CancellationToken token, ResourceBytes? thumbnail = null)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (name, value) in payload)
        {
            if (value is null || name == field) continue;
            var text = value is string str ? str : JsonSerializer.Serialize(value, JsonOptions);
            form.Add(new StringContent(text, System.Text.Encoding.UTF8), name);
        }
        form.Add(new ByteArrayContent(file.Bytes), field, file.FileName);
        if (thumbnail is not null) form.Add(new ByteArrayContent(thumbnail.Bytes), "thumbnail", thumbnail.FileName);
        using var response = await _http.PostAsync(method, form, token);
        var wrapper = await response.Content.ReadFromJsonAsync<TgResponse<T>>(JsonOptions, token)
            ?? throw new InvalidOperationException($"Telegram API {method} 返回空响应。");
        if (!wrapper.Ok || wrapper.Result is null)
            throw new InvalidOperationException($"Telegram API {method} 失败: [{wrapper.ErrorCode}] {wrapper.Description}");
        return wrapper.Result;
    }

    public Task CallAsync(string method, object? payload = null, CancellationToken cancellationToken = default) =>
        CallAsync<JsonElement>(method, payload, cancellationToken);

    /// <summary>getUpdates 长轮询循环。每收到一个 Update 触发 UpdateReceived。</summary>
    public async Task RunLongPollingAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var updates = await CallAsync<IReadOnlyList<TgUpdate>>("getUpdates", new
            {
                offset = _updateOffset,
                timeout = 50,
                allowed_updates = new[] { "message", "edited_message", "channel_post", "edited_channel_post", "business_connection", "business_message", "edited_business_message", "deleted_business_messages", "guest_message", "message_reaction", "message_reaction_count", "inline_query", "chosen_inline_result", "callback_query", "shipping_query", "pre_checkout_query", "purchased_paid_media", "poll", "poll_answer", "my_chat_member", "chat_member", "chat_join_request", "chat_boost", "removed_chat_boost", "managed_bot", "subscription", "stopped_message_generation" }
            }, cancellationToken);

            foreach (var update in updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _updateOffset = Math.Max(_updateOffset, update.UpdateId + 1);

                if (UpdateReceived is { } handler)
                {
                    foreach (var subscriber in handler.GetInvocationList().Cast<Func<TgUpdate, Task>>())
                        await subscriber(update);
                }
            }
        }
    }
}
