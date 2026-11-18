using ShiroBot.TelegramAdapter.BotApi;
using ShiroBot.TelegramAdapter.Services;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

[assembly: RequiresShiroBotPackage("shirobot.model.telegram", MinimumVersion = "1.0.4")]

namespace ShiroBot.TelegramAdapter;

[BotAdapter("telegram", Name = "TelegramAdapter", Version = "0.5.0", Author = "ShirokaProject",
    Description = "Telegram Bot API adapter for ShiroBot", Protocol = "telegram-bot-api")]
public sealed class TelegramAdapter : IBotAdapter, IConfigurableAdapter, IConfigurableComponent<TelegramAdapterConfig>,
    ILifetimeAwareAdapter
{
    private readonly TelegramMapper _mapper = new();
    private readonly TelegramEventService _events;
    private TelegramClient? _client;
    private TelegramInteractionService? _interactions;
    private TelegramReactionService? _reactions;
    private TelegramBotApi? _botApi;
    private TelegramModelApi? _model;
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

    /// <summary>宿主在每次启动前注入；为空时（如测试中直接构造）适配器自行管理轮询任务。</summary>
    public IPluginLifetime Lifetime { get; set; } = null!;
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

    public TService? GetExtension<TService>() where TService : class => this as TService ?? _client as TService ?? _message as TService ?? _user as TService ?? _interactions as TService ?? _reactions as TService ?? _model as TService ?? _botApi as TService;

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
            _interactions = new TelegramInteractionService(_client, config.AutoAnswerCallbacks);
            _botApi = new TelegramBotApi(_client);
            _model = new TelegramModelApi(_botApi, _client, _mapper, _interactions);
            _reactions = new TelegramReactionService(_client);
            _message = new TelegramMessageService(_client, _mapper, _interactions);
            _channel = new TelegramChannelService(_client, _mapper);
            _user = new TelegramUserService(_client, _mapper);
            var self = await User.GetSelfAsync();
            _mapper.SelfId = self.Id;
            Logger.Success($"Telegram 登录成功 - {self.Name} ({self.Id})");
            _events.Attach(_client);
            IPluginLifetime? lifetime = Lifetime;
            _pollingTokenSource = lifetime is null
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(lifetime.Stopping);
            var client = _client;
            var token = _pollingTokenSource.Token;
            // 交给宿主生命周期登记后，停止或热重载时宿主也会等待轮询结束。
            _pollingTask = lifetime is null
                ? RunPollingLoopAsync(client, token)
                : lifetime.Run(_ => RunPollingLoopAsync(client, token));
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
            _botApi?.Detach();
            _model?.Detach();
            _model = null;
            _botApi = null;
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
    [ConfigField("在 Telegram 与 @BotFather 对话，发送 /newbot 创建机器人后获得，格式如 123456789:AA…。每个 Token 同时只能有一个长轮询实例；如曾设置 webhook，需要先删除。",
        Label = "Bot Token", Type = "password", Group = "connection", GroupLabel = "连接", GroupOrder = 10, Order = 10)]
    public string BotToken { get; set; } = string.Empty;

    [ConfigField("留空使用官方服务器 https://api.telegram.org。使用自建 Telegram Bot API Server 时填写其地址，例如 http://127.0.0.1:8081。",
        Label = "API Base URL", Group = "connection", GroupLabel = "连接", GroupOrder = 10, Order = 20)]
    public string? ApiBaseUrl { get; set; }

    [ConfigField("开启时收到按钮回调立即确认，客户端按钮不会转圈。关闭后由插件调用 AnswerCallbackAsync 确认，可附带提示或弹窗；插件未在 10 秒内确认时按钮会一直转圈。",
        Label = "自动确认按钮回调", Group = "behavior", GroupLabel = "行为", GroupOrder = 20, Order = 30)]
    public bool AutoAnswerCallbacks { get; set; } = true;
}
