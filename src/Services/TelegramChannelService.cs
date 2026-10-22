using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.Telegram.Model;
using Sdk = ShiroBot.SDK.Models;

namespace ShiroBot.TelegramAdapter.Services;

public sealed class TelegramChannelService(TelegramClient client, TelegramMapper? mapper = null) : IChannelService
{
    public async Task<Sdk.Channel?> GetChannelAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var (chatId, threadId) = TelegramMapper.ParseChannelId(channelId);
        var chat = await client.CallAsync<TgChat>("getChat", new { chat_id = chatId }, cancellationToken: cancellationToken);
        return TelegramMapper.ToChannel(chat, threadId);
    }

    public async Task<Member?> GetMemberAsync(string channelId, string userId, CancellationToken cancellationToken = default)
    {
        var (chatId, _) = TelegramMapper.ParseChannelId(channelId);
        var member = await client.CallAsync<TgChatMember>("getChatMember", new
        {
            chat_id = chatId,
            user_id = TelegramMapper.ParseId(userId, "userId")
        }, cancellationToken: cancellationToken);
        mapper?.Users.Set(member.User.Id.ToString(), member.User);
        return TelegramMapper.ToMember(member);
    }

    public Task<IReadOnlyList<Sdk.Channel>> GetChannelsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Telegram Bot API 不提供机器人所有会话的枚举接口。");

    public Task<IReadOnlyList<Member>> GetMembersAsync(string channelId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Telegram Bot API 不提供完整成员列表；管理员列表不等于全体成员，请使用 GetMemberAsync。");

    public Task SetChannelNameAsync(string channelId, string name, CancellationToken cancellationToken = default)
    {
        var (chatId, threadId) = TelegramMapper.ParseChannelId(channelId);
        return threadId is { } topic
            ? client.CallAsync("editForumTopic", new { chat_id = chatId, message_thread_id = topic, name }, cancellationToken)
            : client.CallAsync("setChatTitle", new { chat_id = chatId, title = name }, cancellationToken);
    }

    public async Task KickMemberAsync(string channelId, string userId, CancellationToken cancellationToken = default)
    {
        var (chatId, _) = TelegramMapper.ParseChannelId(channelId);
        await client.CallAsync("banChatMember", new
        {
            chat_id = chatId,
            user_id = TelegramMapper.ParseId(userId, "userId")
        }, cancellationToken: cancellationToken);
        await client.CallAsync("unbanChatMember", new { chat_id = chatId,
            user_id = TelegramMapper.ParseId(userId, "userId"), only_if_banned = true }, cancellationToken);
    }

    public Task MuteMemberAsync(string channelId, string userId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (duration < TimeSpan.FromSeconds(30) || duration > TimeSpan.FromDays(366))
            throw new ArgumentOutOfRangeException(nameof(duration), "Telegram 有限时长禁言必须介于 30 秒和 366 天之间，避免被解释为永久禁言。");
        var (chatId, _) = TelegramMapper.ParseChannelId(channelId);
        return client.CallAsync("restrictChatMember", new
        {
            chat_id = chatId,
            user_id = TelegramMapper.ParseId(userId, "userId"),
            permissions = new { can_send_messages = false },
            until_date = DateTimeOffset.UtcNow.Add(duration).ToUnixTimeSeconds()
        }, cancellationToken: cancellationToken);
    }

    public Task LeaveChannelAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var (chatId, _) = TelegramMapper.ParseChannelId(channelId);
        return client.CallAsync("leaveChat", new { chat_id = chatId }, cancellationToken: cancellationToken);
    }
}
