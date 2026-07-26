using System.Net.Http.Json;
using System.Text.Json;
using ShiroBot.Telegram.Model;

namespace ShiroBot.TelegramAdapter;

/// <summary>Telegram Bot API HTTP 客户端(长轮询接收 + 方法调用)。</summary>
public sealed class TelegramClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly string _token;
    private long _updateOffset;

    public event Func<TgUpdate, Task>? UpdateReceived;

    public TelegramClient(string token, string? apiBaseUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _token = token;
        _http = new HttpClient
        {
            BaseAddress = new Uri((apiBaseUrl?.TrimEnd('/') ?? "https://api.telegram.org") + $"/bot{token}/"),
            Timeout = TimeSpan.FromSeconds(120)
        };
    }

    public string GetFileDownloadUrl(string filePath) =>
        $"{_http.BaseAddress!.Scheme}://{_http.BaseAddress.Authority}/file/bot{_token}/{filePath}";

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
                allowed_updates = new[] { "message", "edited_message", "channel_post", "callback_query", "my_chat_member", "chat_member" }
            }, cancellationToken);

            foreach (var update in updates)
            {
                _updateOffset = Math.Max(_updateOffset, update.UpdateId + 1);

                if (UpdateReceived is { } handler)
                {
                    await handler(update);
                }
            }
        }
    }
}
