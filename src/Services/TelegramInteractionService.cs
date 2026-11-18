using ShiroBot.Model.Telegram;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.Telegram.Model;

namespace ShiroBot.TelegramAdapter.Services;

/// <param name="autoAnswer">收到回调立即发出空确认；关闭后由插件确认，可附带提示。</param>
public sealed class TelegramInteractionService(TelegramClient client, bool autoAnswer = true) : IMessageInteractionService
{
    private sealed class Pending(Channel? channel, string? messageId)
    {
        public Channel? Channel { get; } = channel;
        public string? MessageId { get; } = messageId;
        public DateTimeOffset Expires { get; } = DateTimeOffset.UtcNow.AddMinutes(10);
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public bool Acknowledged { get; set; }
    }
    private readonly BoundedCache<Pending> _pending = new();

    public bool AutoAnswer => autoAnswer;

    internal async Task RegisterAsync(TgCallbackQuery callback)
    {
        _pending.Set(callback.Id, new(callback.Message is { } m ? TelegramMapper.ToChannel(m.Chat, m.MessageThreadId) : null,
            callback.Message?.MessageId.ToString()));
        if (autoAnswer) await AcknowledgeCoreAsync(callback.Id, null, default);
    }

    public Task AcknowledgeAsync(InteractionEvent interaction, CancellationToken cancellationToken = default) =>
        AcknowledgeCoreAsync(interaction.InteractionId, null, cancellationToken);

    /// <summary>带提示确认回调；已经确认过时不再发送，因为 Telegram 每个回调只接受一次回答。</summary>
    public Task AnswerAsync(string interactionId, TelegramCallbackAnswer answer, CancellationToken cancellationToken = default) =>
        AcknowledgeCoreAsync(interactionId, answer, cancellationToken);

    private Pending Get(string id)
    {
        if (!_pending.TryGet(id, out var pending) || pending.Expires <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Telegram 交互已过期或不属于当前适配器实例。");
        return pending;
    }

    private async Task AcknowledgeCoreAsync(string id, TelegramCallbackAnswer? answer, CancellationToken token)
    {
        var pending = Get(id);
        await pending.Gate.WaitAsync(token);
        try
        {
            if (pending.Acknowledged) return;
            await client.CallAsync("answerCallbackQuery", new
            {
                callback_query_id = id, text = answer?.Text, show_alert = answer?.ShowAlert == true ? true : (bool?)null,
                url = answer?.Url, cache_time = answer?.CacheTime is { } cache ? (long?)cache.TotalSeconds : null
            }, token);
            pending.Acknowledged = true;
        }
        finally { pending.Gate.Release(); }
    }

    internal async Task<string?> ReplyTargetAsync(InteractionReference reference, CancellationToken token)
    {
        var pending = Get(reference.InteractionId);
        if (pending.Channel is null || pending.Channel.Id != reference.Channel.Id || pending.Channel.Type != reference.Channel.Type)
            throw new NotSupportedException("该交互没有可发送回复的会话，或会话不匹配。");
        await AcknowledgeCoreAsync(reference.InteractionId, null, token);
        return pending.MessageId;
    }
}
