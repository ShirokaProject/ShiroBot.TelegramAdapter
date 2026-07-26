using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.Telegram.Model;
using Sdk = ShiroBot.SDK.Models;

namespace ShiroBot.TelegramAdapter.Services;

public sealed class TelegramChannelService(TelegramClient client) : IChannelService
{
    public async Task<Sdk.Channel?> GetChannelAsync(string channelId)
    {
        var (chatId, threadId) = TelegramMapper.ParseChannelId(channelId);
        var chat = await client.CallAsync<TgChat>("getChat", new { chat_id = chatId });
        return TelegramMapper.ToChannel(chat, threadId);
    }

    public async Task<Member?> GetMemberAsync(string channelId, string userId)
    {
        var (chatId, _) = TelegramMapper.ParseChannelId(channelId);
        var member = await client.CallAsync<TgChatMember>("getChatMember", new
        {
            chat_id = chatId,
            user_id = TelegramMapper.ParseId(userId, "userId")
        });
        return TelegramMapper.ToMember(member);
    }

    public async Task<IReadOnlyList<Member>> GetMembersAsync(string channelId)
    {
        // Bot API 只提供管理员列表,普通成员无法枚举
        var (chatId, _) = TelegramMapper.ParseChannelId(channelId);
        var admins = await client.CallAsync<IReadOnlyList<TgChatMember>>(
            "getChatAdministrators", new { chat_id = chatId });
        return admins.Select(TelegramMapper.ToMember).ToArray();
    }

    public Task SetChannelNameAsync(string channelId, string name)
    {
        var (chatId, _) = TelegramMapper.ParseChannelId(channelId);
        return client.CallAsync("setChatTitle", new { chat_id = chatId, title = name });
    }

    public Task KickMemberAsync(string channelId, string userId)
    {
        var (chatId, _) = TelegramMapper.ParseChannelId(channelId);
        return client.CallAsync("banChatMember", new
        {
            chat_id = chatId,
            user_id = TelegramMapper.ParseId(userId, "userId")
        });
    }

    public Task MuteMemberAsync(string channelId, string userId, TimeSpan duration)
    {
        var (chatId, _) = TelegramMapper.ParseChannelId(channelId);
        return client.CallAsync("restrictChatMember", new
        {
            chat_id = chatId,
            user_id = TelegramMapper.ParseId(userId, "userId"),
            permissions = new { can_send_messages = false },
            until_date = DateTimeOffset.UtcNow.Add(duration).ToUnixTimeSeconds()
        });
    }

    public Task LeaveChannelAsync(string channelId)
    {
        var (chatId, _) = TelegramMapper.ParseChannelId(channelId);
        return client.CallAsync("leaveChat", new { chat_id = chatId });
    }
}
