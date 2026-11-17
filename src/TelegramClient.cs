using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShiroBot.Telegram.Model;
using ShiroBot.Telegram.Model.BotApi;

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

    /// <summary>每个 Update 的原始 JSON，先于 <see cref="UpdateReceived"/> 触发。</summary>
    public event Func<JsonElement, Task>? RawUpdateReceived;

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
        cancellationToken.ThrowIfCancellationRequested();
        using var response = payload is null
            ? await _http.GetAsync(method, cancellationToken)
            : await _http.PostAsJsonAsync(method, payload, JsonOptions, cancellationToken);
        var result = await ReadResultAsync(method, response, cancellationToken);
        return result.Deserialize<T>(JsonOptions) ?? throw new TelegramApiException(method, null, "响应结果为空。");
    }

    internal async Task<T> CallMultipartAsync<T>(string method, IReadOnlyDictionary<string, object?> payload,
        string field, ResourceBytes file, CancellationToken token, ResourceBytes? thumbnail = null)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (name, value) in payload)
        {
            if (value is null || name == field) continue;
            var text = value is string str ? str : JsonSerializer.Serialize(value, JsonOptions);
            form.Add(new StringContent(text, Encoding.UTF8), name);
        }
        form.Add(new ByteArrayContent(file.Bytes), field, file.FileName);
        if (thumbnail is not null) form.Add(new ByteArrayContent(thumbnail.Bytes), "thumbnail", thumbnail.FileName);
        using var response = await _http.PostAsync(method, form, token);
        var result = await ReadResultAsync(method, response, token);
        return result.Deserialize<T>(JsonOptions) ?? throw new TelegramApiException(method, null, "响应结果为空。");
    }

    public Task CallAsync(string method, object? payload = null, CancellationToken cancellationToken = default) =>
        CallAsync<JsonElement>(method, payload, cancellationToken);

    /// <summary>
    ///     调用 Bot API 方法并返回原始 result。参数使用生成的 Bot API 类型序列化；包含上传文件时
    ///     自动改用 multipart，嵌套对象中的上传以 attach:// 引用。
    /// </summary>
    public async Task<JsonElement> InvokeAsync(string method, IReadOnlyList<KeyValuePair<string, object?>> parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uploads = new List<KeyValuePair<string, InputFile>>();
        var fields = new List<KeyValuePair<string, JsonNode?>>();
        using (BotApiUploads.Collect(uploads))
        {
            foreach (var (name, value) in parameters)
            {
                if (value is null) continue;
                if (value is InputFile { IsUpload: true } file)
                {
                    uploads.Add(new(name, file));
                    continue;
                }
                fields.Add(new(name, JsonSerializer.SerializeToNode(value, value.GetType(), BotApiJson.Options)));
            }
        }

        HttpContent content;
        if (uploads.Count == 0)
        {
            var body = new JsonObject();
            foreach (var (name, node) in fields) body[name] = node;
            content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        else
        {
            var form = new MultipartFormDataContent();
            foreach (var (name, node) in fields)
            {
                // Multipart fields carry strings as-is and everything else JSON-serialized.
                var text = node is JsonValue scalar && scalar.GetValueKind() == JsonValueKind.String
                    ? scalar.GetValue<string>()
                    : node?.ToJsonString() ?? "null";
                form.Add(new StringContent(text, Encoding.UTF8), name);
            }
            foreach (var (name, file) in uploads) form.Add(file.CreateContent(), name, file.FileName ?? name);
            content = form;
        }

        using (content)
        using (var response = await _http.PostAsync(method, content, cancellationToken))
            return await ReadResultAsync(method, response, cancellationToken);
    }

    private static async Task<JsonElement> ReadResultAsync(string method, HttpResponseMessage response, CancellationToken token)
    {
        JsonElement body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, token);
        }
        catch (JsonException)
        {
            throw new TelegramApiException(method, (int)response.StatusCode, $"HTTP {(int)response.StatusCode} 返回了非 JSON 响应。");
        }

        var isObject = body.ValueKind == JsonValueKind.Object;
        if (isObject && body.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True
            && body.TryGetProperty("result", out var result))
            return result.Clone();

        int? code = isObject && body.TryGetProperty("error_code", out var errorCode) && errorCode.TryGetInt32(out var number)
            ? number : (int)response.StatusCode;
        var description = isObject && body.TryGetProperty("description", out var text) ? text.GetString() : response.ReasonPhrase;
        var parameters = isObject && body.TryGetProperty("parameters", out var raw)
            ? raw.Deserialize<ResponseParameters>(BotApiJson.Options) : null;
        throw new TelegramApiException(method, code, description, parameters);
    }

    /// <summary>getUpdates 长轮询循环。每收到一个 Update 依次触发 RawUpdateReceived 和 UpdateReceived。</summary>
    public async Task RunLongPollingAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var updates = await CallAsync<IReadOnlyList<JsonElement>>("getUpdates", new
            {
                offset = _updateOffset,
                timeout = 50,
                allowed_updates = BotApiInfo.UpdateTypes
            }, cancellationToken);

            foreach (var raw in updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var update = raw.Deserialize<TgUpdate>(JsonOptions)!;
                _updateOffset = Math.Max(_updateOffset, update.UpdateId + 1);

                if (RawUpdateReceived is { } rawHandlers)
                    foreach (var subscriber in rawHandlers.GetInvocationList().Cast<Func<JsonElement, Task>>())
                        await subscriber(raw);
                if (UpdateReceived is { } handlers)
                    foreach (var subscriber in handlers.GetInvocationList().Cast<Func<TgUpdate, Task>>())
                        await subscriber(update);
            }
        }
    }
}

/// <summary>Telegram Bot API 返回 ok=false 或非 JSON 响应。</summary>
public sealed class TelegramApiException(string method, int? errorCode, string? description, ResponseParameters? parameters = null)
    : InvalidOperationException($"Telegram API {method} 失败: [{errorCode}] {description}")
{
    public string Method { get; } = method;
    public int? ErrorCode { get; } = errorCode;
    public string? Description { get; } = description;
    public ResponseParameters? Parameters { get; } = parameters;

    /// <summary>触发限流时需要等待的时间。</summary>
    public TimeSpan? RetryAfter => Parameters?.RetryAfter is { } seconds ? TimeSpan.FromSeconds(seconds) : null;

    /// <summary>群组已升级为超级群组时的新 chat_id。</summary>
    public long? MigrateToChatId => Parameters?.MigrateToChatId;
}
