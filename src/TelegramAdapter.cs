using ShiroBot.TelegramAdapter.Services;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

[assembly: RequiresShiroBotPackage("shirobot.model.telegram", MinimumVersion = "0.9.4")]

namespace ShiroBot.TelegramAdapter;

[BotAdapter("telegram", Name = "TelegramAdapter", Version = "0.3.0", Author = "ShirokaProject",
    Description = "Telegram Bot API adapter for ShiroBot", Protocol = "telegram-bot-api")]
public sealed class TelegramAdapter : IBotAdapter, IConfigurableAdapter, IConfigurableComponent<TelegramAdapterConfig>
{
    private readonly TelegramMapper _mapper = new();
    private readonly TelegramEventService _events;
    private TelegramClient? _client;
    private TelegramInteractionService? _interactions;
    private TelegramReactionService? _reactions;
    private CancellationTokenSource? _pollingTokenSource;
    private Task? _pollingTask;
    private IMessageService? _message;
    private IChannelService? _channel;
    private IUserService? _user;

    public TelegramAdapter() => _events = new TelegramEventService(_mapper, () => _interactions);

    public string Platform => TelegramMapper.PlatformId;
    public IMessageService Message => _message ?? throw NotStarted();
    public IChannelService Channel => _channel ?? throw NotStarted();
    public IUserService User => _user ?? throw NotStarted();
    public IEventService Event => _events;
    public IConfigContext Config { get; set; } = null!;
    public IConsoleLogger Logger { get; set; } = null!;
    public TelegramAdapterConfig CurrentConfigValue { get; private set; } = new();
    ConfigApplyMode IConfigurableAdapter.ApplyMode => ConfigApplyMode.RestartComponent;

    public Task OnConfigLoadedAsync(TelegramAdapterConfig config, CancellationToken cancellationToken)
    {
        CurrentConfigValue = config;
        return Task.CompletedTask;
    }

    public Task OnConfigChangedAsync(TelegramAdapterConfig previous, TelegramAdapterConfig current, CancellationToken cancellationToken)
    {
        CurrentConfigValue = current;
        return Task.CompletedTask;
    }

    public TService? GetExtension<TService>() where TService : class => this as TService ?? _client as TService ?? _message as TService ?? _user as TService ?? _interactions as TService ?? _reactions as TService;

    public async Task StartAsync()
    {
        if (_pollingTokenSource is not null)
            throw new InvalidOperationException("Telegram 适配器已启动。");
        var config = CurrentConfigValue;
        if (string.IsNullOrWhiteSpace(config.BotToken))
            throw new InvalidOperationException("请在适配器配置中填写 BotToken。");

        try
        {
            _client = new TelegramClient(config.BotToken, config.ApiBaseUrl);
            _interactions = new TelegramInteractionService(_client);
            _reactions = new TelegramReactionService(_client);
            _message = new TelegramMessageService(_client, _mapper, _interactions);
            _channel = new TelegramChannelService(_client, _mapper);
            _user = new TelegramUserService(_client, _mapper);
            var self = await User.GetSelfAsync();
            _mapper.SelfId = self.Id;
            Logger.Success($"Telegram 登录成功 - {self.Name} ({self.Id})");
            _events.Attach(_client);
            _pollingTokenSource = new CancellationTokenSource();
            _pollingTask = RunPollingLoopAsync(_client, _pollingTokenSource.Token);
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        _pollingTokenSource?.Cancel();
        try
        {
            if (_pollingTask is not null) await _pollingTask;
        }
        finally
        {
            if (_client is not null) _events.Detach(_client);
            _pollingTokenSource?.Dispose();
            _client?.Dispose();
            _pollingTokenSource = null;
            _pollingTask = null;
            _client = null;
            _message = null;
            _channel = null;
            _user = null;
            _mapper.SelfId = null;
            _mapper.Users.Clear();
            _mapper.Chats.Clear();
            _mapper.Uploads.Clear();
            _interactions = null;
            _reactions = null;
        }
    }

    private async Task RunPollingLoopAsync(TelegramClient client, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await client.RunLongPollingAsync(token);
                if (!token.IsCancellationRequested) Logger.Warning("Telegram 长轮询 连接已断开，准备重连。");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Logger.Error($"Telegram 长轮询异常: {ex.GetType().Name}: {ex.Message}");
                await _events.PublishAsync(new ShiroBot.SDK.Models.BotOfflineEvent { Platform = Platform, SelfId = _mapper.SelfId, Reason = ex.GetType().Name });
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }

    private static InvalidOperationException NotStarted() => new("Telegram 适配器尚未启动。");
}

[ConfigModel]
public sealed class TelegramAdapterConfig
{
    [ConfigField("Telegram Bot Token。", Label = "Bot Token", Type = "password", Group = "connection", GroupLabel = "连接", Order = 10)]
    public string BotToken { get; set; } = string.Empty;

    [ConfigField("自建 Telegram Bot API Server 地址；留空使用 https://api.telegram.org。",
        Label = "API Base URL", Group = "connection", Order = 20)]
    public string? ApiBaseUrl { get; set; }
}
