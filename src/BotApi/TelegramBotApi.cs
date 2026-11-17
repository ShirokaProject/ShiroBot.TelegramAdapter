using System.Text.Json;
using Api = ShiroBot.Telegram.Model.BotApi;

namespace ShiroBot.TelegramAdapter.BotApi;

/// <summary>
///     The complete Telegram Bot API of the current adapter instance: one typed method per Bot API method
///     (see <see cref="Api.BotApiInfo.Version"/>), typed updates, and an untyped call for anything newer.
///     Obtain it with <c>Context.GetAdapterExtension&lt;ITelegramBotApi&gt;()</c>.
/// </summary>
/// <remarks>
///     The adapter receives updates by long polling. Calling <c>getUpdates</c> yourself steals updates from it,
///     and <c>setWebhook</c> stops polling until <c>deleteWebhook</c> is called.
///     Failures throw <see cref="TelegramApiException"/>.
/// </remarks>
public partial interface ITelegramBotApi
{
    /// <summary>Raised for every update the adapter receives, before it is mapped to SDK events.</summary>
    event Func<Api.Update, Task>? UpdateReceived;

    /// <summary>Calls any Bot API method with snake_case parameters and returns the raw <c>result</c>.</summary>
    /// <param name="method">Method name, e.g. <c>sendMessage</c>.</param>
    /// <param name="parameters">Parameter name/value pairs; values may be Bot API objects, <see cref="Api.InputFile"/> uploads or JSON elements.</param>
    Task<JsonElement> CallAsync(string method, IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>Builds the download URL of a file returned by <see cref="GetFileAsync"/>.</summary>
    string GetFileDownloadUrl(Api.File file);
}

public sealed partial class TelegramBotApi : ITelegramBotApi
{
    private readonly TelegramClient _client;
    private Func<Api.Update, Task>? _updateReceived;

    public TelegramBotApi(TelegramClient client)
    {
        _client = client;
        _client.RawUpdateReceived += OnRawUpdateAsync;
    }

    public event Func<Api.Update, Task>? UpdateReceived
    {
        add => _updateReceived += value;
        remove => _updateReceived -= value;
    }

    public Task<JsonElement> CallAsync(string method, IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        return _client.InvokeAsync(method, parameters?.ToArray() ?? [], cancellationToken);
    }

    public string GetFileDownloadUrl(Api.File file) =>
        _client.GetFileDownloadUrl(file.FilePath ?? throw new ArgumentException("Telegram returned no file_path.", nameof(file)));

    internal void Detach() => _client.RawUpdateReceived -= OnRawUpdateAsync;

    private async Task OnRawUpdateAsync(JsonElement update)
    {
        if (_updateReceived is not { } handlers) return;
        var typed = update.Deserialize<Api.Update>(Api.BotApiJson.Options)!;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<Api.Update, Task>>())
            await handler(typed).ConfigureAwait(false);
    }

    private async Task<T> InvokeAsync<T>(string method, KeyValuePair<string, object?>[] parameters,
        CancellationToken cancellationToken)
    {
        var result = await _client.InvokeAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        return result.Deserialize<T>(Api.BotApiJson.Options)!;
    }

    /// <summary>Edit methods return the edited Message, or True for inline messages.</summary>
    private async Task<Api.Message?> InvokeMessageOrTrueAsync(string method, KeyValuePair<string, object?>[] parameters,
        CancellationToken cancellationToken)
    {
        var result = await _client.InvokeAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        return result.ValueKind == JsonValueKind.True ? null : result.Deserialize<Api.Message>(Api.BotApiJson.Options);
    }
}
