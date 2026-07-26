using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;
using ShiroBot.TelegramAdapter.Services;

namespace ShiroBot.TelegramAdapter;

[BotAdapter("telegram",
    Name = "TelegramAdapter",
    Version = "0.1.0",
    Description = "Telegram Bot API adapter for ShiroBot",
    Protocol = "telegram-bot-api")]
public class TelegramAdapter : IBotAdapter
{
    private TelegramClient? _client;
    private readonly TelegramEventService _eventService = new();
    private CancellationTokenSource? _pollingTokenSource;

    private IMessageService? _message;
    private IChannelService? _channel;
    private IUserService? _user;

    public string Platform => TelegramMapper.PlatformId;

    public IMessageService Message => _message ?? throw NotStarted();
    public IChannelService Channel => _channel ?? throw NotStarted();
    public IUserService User => _user ?? throw NotStarted();
    public IEventService Event => _eventService;
    public IConfigContext Config { get; set; } = null!;
    public IConsoleLogger Logger { get; set; } = null!;

    /// <summary>暴露原始 Bot API 客户端作为平台扩展(inline keyboard、answerCallbackQuery 等)。</summary>
    public TService? GetExtension<TService>() where TService : class =>
        this as TService ?? _client as TService;

    public async Task StartAsync()
    {
        var config = Config.Load<TelegramAdapterConfig>();
        Config.Save(config);

        if (string.IsNullOrWhiteSpace(config.BotToken))
        {
            Logger.Error("请在适配器配置中填写 BotToken。");
            throw new InvalidOperationException("Telegram BotToken 未配置。");
        }

        _client = new TelegramClient(config.BotToken, config.ApiBaseUrl);
        TelegramMapper.Client = _client;
        _message = new TelegramMessageService(_client);
        _channel = new TelegramChannelService(_client);
        _user = new TelegramUserService(_client);
        _eventService.Attach(_client);

        Logger.Info("开始连接 Telegram Bot API...");
        var self = await User.GetSelfAsync();
        TelegramMapper.SelfId = self.Id;
        Logger.Success($"Telegram 登录成功 - @{self.Name} ({self.Id})");

        _pollingTokenSource = new CancellationTokenSource();
        _ = Task.Run(() => RunPollingLoopAsync(_pollingTokenSource.Token));
    }

    public Task StopAsync()
    {
        _pollingTokenSource?.Cancel();
        return Task.CompletedTask;
    }

    private async Task RunPollingLoopAsync(CancellationToken token)
    {
        var retryCount = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                retryCount++;
                if (retryCount > 1)
                {
                    Logger.Info($"正在重连 Telegram 长轮询,第 {retryCount} 次。");
                }

                await _client!.RunLongPollingAsync(token);
            }
            catch (TaskCanceledException)
            {
                Logger.Warning("Telegram 长轮询已取消。");
                break;
            }
            catch (Exception ex)
            {
                Logger.Error($"Telegram 长轮询异常: {ex.GetType().Name}: {ex.Message}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static InvalidOperationException NotStarted() =>
        new("Telegram 适配器尚未启动。");
}

public sealed class TelegramAdapterConfig
{
    public string BotToken { get; set; } = string.Empty;

    /// <summary>自建 Bot API Server 时可修改;默认官方 https://api.telegram.org。</summary>
    public string? ApiBaseUrl { get; set; }
}
