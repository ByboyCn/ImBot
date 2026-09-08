using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImBot.Core;
using ImBot.OneBot.Api;

namespace ImBot.Plugins;

/// <summary>
/// OneBot 中心：action 分发（HTTP 与 WS 共用）+ 事件分发（WS 广播 + webhook POST）。
/// </summary>
public sealed class OneBotHub : IOneBotApi
{
    private readonly IContext _ctx;
    private readonly OneBotConfig _cfg;
    private static readonly HttpClient Http = new();

    private readonly ConcurrentDictionary<Guid, WsClient> _wsClients = new();
    private readonly ConcurrentDictionary<string, byte[]> _files = new();        // file_id -> bytes（演示级存储）
    private readonly ConcurrentDictionary<string, (long size, List<byte[]> parts)> _transfers = new();
    private long _messageIdCounter;

    public static readonly string[] SupportedActions =
    [
        "get_version", "get_status", "get_supported_actions", "get_self_info",
        "get_user_info", "get_user_list",
        "send_private_message", "get_friend_list",
        "get_group_info", "get_group_list", "get_group_member_info", "get_group_member_list",
        "set_group_name", "leave_group",
        "get_guild_list", "get_guild_info", "get_channel_list", "get_channel_info",
        "send_channel_message", "get_guild_member_list", "get_guild_member_info", "mute_guild_member",
        "send_message", "delete_message", "get_message", "get_latest_messages",
        "upload_file", "upload_file_fragmented_prepare", "upload_file_fragmented_transfer",
        "upload_file_fragmented_finish", "upload_file_fragmented_abort",
        "download_file", "send_like",
    ];

    public OneBotHub(IContext ctx, OneBotConfig cfg) { _ctx = ctx; _cfg = cfg; }

    // ==================== action 分发（HTTP/WS 共用）====================

    public async Task<OneBotResponse> InvokeAsync(string action, JsonElement? parameters)
    {
        object? data; string? errorMessage; int retcode;
        try
        {
            data = await InvokeCoreAsync(action, parameters);
            retcode = 0; errorMessage = null;
        }
        catch (OneBotNotImplementedException ex)
        {
            data = null; retcode = 10003; errorMessage = ex.Message;   // 不支持
        }
        catch (Exception ex)
        {
            data = null; retcode = 10001; errorMessage = ex.Message;   // 参数/内部错误
        }
        return new OneBotResponse(retcode == 0 ? "ok" : "failed", data, retcode, errorMessage);
    }

    private async Task<object?> InvokeCoreAsync(string action, JsonElement? p)
    {
        string? Str(string name) => p?.TryGetProperty(name, out var e) is true ? e.GetString() : null;
        long Num(string name) => p is not null && p.Value.TryGetProperty(name, out var e) && e.TryGetInt64(out var n) ? n : 0;
        bool Has(string name) => p?.TryGetProperty(name, out _) is true;

        switch (action)
        {
            case "get_version": return await GetVersionAsync();
            case "get_status": return await GetStatusAsync();
            case "get_supported_actions": return await GetSupportedActionsAsync();
            case "get_self_info": return await GetSelfInfoAsync();
            case "get_user_info": return await GetUserInfoAsync(Str("user_id") ?? "", Str("self_id"));
            case "get_user_list": return await GetUserListAsync();
            case "send_private_message": return await SendPrivateMessageAsync(Str("user_id") ?? "", ParseMessage(p!.Value));
            case "get_friend_list": return await GetFriendListAsync();
            case "get_group_info": return await GetGroupInfoAsync(Str("group_id") ?? "", Has("no_cache") && p!.Value.GetProperty("no_cache").GetBoolean());
            case "get_group_list": return await GetGroupListAsync();
            case "get_group_member_info": return await GetGroupMemberInfoAsync(Str("group_id") ?? "", Str("user_id") ?? "");
            case "get_group_member_list": return await GetGroupMemberListAsync(Str("group_id") ?? "");
            case "set_group_name": await SetGroupNameAsync(Str("group_id") ?? "", Str("group_name") ?? ""); return null;
            case "leave_group": await LeaveGroupAsync(Str("group_id") ?? ""); return null;
            case "get_guild_list": return await GetGuildListAsync();
            case "get_guild_info": return await GetGuildInfoAsync(Str("guild_id") ?? "");
            case "get_channel_list": return await GetChannelListAsync(Str("guild_id") ?? "");
            case "get_channel_info": return await GetChannelInfoAsync(Str("channel_id") ?? "");
            case "send_channel_message": return await SendChannelMessageAsync(Str("channel_id") ?? "", ParseMessage(p!.Value));
            case "get_guild_member_list": return await GetGuildMemberListAsync(Str("guild_id") ?? "", Str("next_token"));
            case "get_guild_member_info": return await GetGuildMemberInfoAsync(Str("guild_id") ?? "", Str("user_id") ?? "");
            case "mute_guild_member": await MuteGuildMemberAsync(Str("guild_id") ?? "", Str("user_id") ?? "", Num("duration")); return null;
            case "send_message": return await SendMessageAsync(ParseSendRequest(p!.Value));
            case "delete_message": await DeleteMessageAsync(Str("message_id") ?? ""); return null;
            case "get_message": return await GetMessageAsync(Str("message_id") ?? "");
            case "get_latest_messages": return await GetLatestMessagesAsync(Str("detail_type") ?? "", Str("target_id") ?? "", (int)Num("count"));
            case "upload_file": return await UploadFileAsync(Str("type") ?? "", Str("name") ?? "", Num("size"), p?.TryGetProperty("data", out var d) is true ? d.GetBytesFromBase64() : null, Str("url"));
            case "upload_file_fragmented_prepare": return await UploadFileFragmentedPrepareAsync(Str("type") ?? "", Str("name") ?? "", Num("size"));
            case "upload_file_fragmented_transfer": await UploadFileFragmentedTransferAsync(Str("transfer_id") ?? "", (int)Num("offset"), p?.TryGetProperty("data", out var d2) is true ? d2.GetBytesFromBase64() : []); return null;
            case "upload_file_fragmented_finish": return await UploadFileFragmentedFinishAsync(Str("transfer_id") ?? "");
            case "upload_file_fragmented_abort": await UploadFileFragmentedAbortAsync(Str("transfer_id") ?? ""); return null;
            case "download_file": return Convert.ToBase64String(await DownloadFileAsync(Str("file_id") ?? ""));
            case "send_like": await SendLikeAsync(Str("user_id") ?? "", (int)Num("times")); return null;
            default: throw new OneBotNotImplementedException(action);
        }
    }

    private static OneBotMessageSegment[] ParseMessage(JsonElement p)
    {
        if (!p.TryGetProperty("message", out var m)) return [];
        return m.Deserialize<OneBotMessageSegment[]>() ?? [];
    }

    private static OneBotSendMessageRequest ParseSendRequest(JsonElement p)
    {
        string? S(string n) => p.TryGetProperty(n, out var e) ? e.GetString() : null;
        return new OneBotSendMessageRequest(
            S("detail_type") ?? "private", S("user_id"), S("group_id"), S("guild_id"), S("channel_id"),
            ParseMessage(p));
    }

    private static string TextOf(IEnumerable<OneBotMessageSegment> segments) =>
        string.Concat(segments.Select(s => s.ExtractText() ?? ""));

    private IAdapter? Adapter() => _ctx.Resolve<IAdapter>();

    // ==================== IOneBotApi 实现 ====================

    public Task<OneBotVersion> GetVersionAsync() =>
        Task.FromResult(new OneBotVersion("ImBot", typeof(OneBotHub).Assembly.GetName().Version?.ToString(3) ?? "1.0.0", "12"));

    public Task<OneBotStatus> GetStatusAsync() =>
        Task.FromResult(new OneBotStatus(Good: true, Online: Adapter() != null));

    public Task<IReadOnlyList<string>> GetSupportedActionsAsync() =>
        Task.FromResult<IReadOnlyList<string>>(SupportedActions);

    public Task<OneBotSelfInfo> GetSelfInfoAsync() =>
        Task.FromResult(new OneBotSelfInfo("imbot", "ImBot", "ImBot"));

    public Task<OneBotUser> GetUserInfoAsync(string userId, string? selfId = null)
        => throw new OneBotNotImplementedException("get_user_info");
    public Task<IReadOnlyList<OneBotUser>> GetUserListAsync()
        => throw new OneBotNotImplementedException("get_user_list");

    public async Task<string> SendPrivateMessageAsync(string userId, params OneBotMessageSegment[] message)
        => await SendAsync(userId, message);
    public Task<IReadOnlyList<OneBotFriend>> GetFriendListAsync()
        => throw new OneBotNotImplementedException("get_friend_list");

    public Task<OneBotGroup> GetGroupInfoAsync(string groupId, bool noCache = false)
        => throw new OneBotNotImplementedException("get_group_info");
    public Task<IReadOnlyList<OneBotGroup>> GetGroupListAsync()
        => throw new OneBotNotImplementedException("get_group_list");
    public Task<OneBotGroupMember> GetGroupMemberInfoAsync(string groupId, string userId, bool noCache = false)
        => throw new OneBotNotImplementedException("get_group_member_info");
    public Task<IReadOnlyList<OneBotGroupMember>> GetGroupMemberListAsync(string groupId)
        => throw new OneBotNotImplementedException("get_group_member_list");
    public Task SetGroupNameAsync(string groupId, string groupName)
        => throw new OneBotNotImplementedException("set_group_name");
    public Task LeaveGroupAsync(string groupId)
        => throw new OneBotNotImplementedException("leave_group");

    public Task<IReadOnlyList<OneBotGuild>> GetGuildListAsync()
        => throw new OneBotNotImplementedException("get_guild_list");
    public Task<OneBotGuild> GetGuildInfoAsync(string guildId, bool noCache = false)
        => throw new OneBotNotImplementedException("get_guild_info");
    public Task<IReadOnlyList<OneBotChannel>> GetChannelListAsync(string guildId, bool noCache = false)
        => throw new OneBotNotImplementedException("get_channel_list");
    public Task<OneBotChannelInfo> GetChannelInfoAsync(string channelId, bool noCache = false)
        => throw new OneBotNotImplementedException("get_channel_info");
    public async Task<string> SendChannelMessageAsync(string channelId, params OneBotMessageSegment[] message)
        => await SendAsync(channelId, message);
    public Task<IReadOnlyList<OneBotUser>> GetGuildMemberListAsync(string guildId, string? nextToken = null)
        => throw new OneBotNotImplementedException("get_guild_member_list");
    public Task<OneBotUser> GetGuildMemberInfoAsync(string guildId, string userId, bool noCache = false)
        => throw new OneBotNotImplementedException("get_guild_member_info");
    public Task MuteGuildMemberAsync(string guildId, string userId, long duration)
        => throw new OneBotNotImplementedException("mute_guild_member");

    public async Task<string> SendMessageAsync(OneBotSendMessageRequest request)
    {
        var target = request.UserId ?? request.GroupId ?? request.ChannelId
            ?? throw new ArgumentException("user_id / group_id / channel_id 至少一个");
        return await SendAsync(target, request.Message);
    }

    private async Task<string> SendAsync(string target, OneBotMessageSegment[] message)
    {
        var adapter = Adapter() ?? throw new InvalidOperationException("无在线适配器");
        var id = $"ob{Interlocked.Increment(ref _messageIdCounter)}";
        await adapter.SendTextAsync(target, TextOf(message));
        return id;
    }

    public Task DeleteMessageAsync(string messageId) => throw new OneBotNotImplementedException("delete_message");
    public Task<object> GetMessageAsync(string messageId) => throw new OneBotNotImplementedException("get_message");
    public Task<IReadOnlyList<object>> GetLatestMessagesAsync(string detailType, string targetId, int count)
        => throw new OneBotNotImplementedException("get_latest_messages");

    public async Task<OneBotUploadResult> UploadFileAsync(string type, string name, long size, byte[]? data = null, string? url = null)
    {
        var bytes = data;
        if (bytes == null && url != null) bytes = await Http.GetByteArrayAsync(url);
        var id = $"file{Interlocked.Increment(ref _messageIdCounter)}";
        _files[id] = bytes ?? [];
        return new OneBotUploadResult(id);
    }

    public Task<OneBotTransferHandle> UploadFileFragmentedPrepareAsync(string type, string name, long size)
    {
        var id = $"tr{Interlocked.Increment(ref _messageIdCounter)}";
        _transfers[id] = (size, []);
        return Task.FromResult(new OneBotTransferHandle(id));
    }

    public Task UploadFileFragmentedTransferAsync(string transferId, int offset, byte[] data)
    {
        if (!_transfers.TryGetValue(transferId, out var t)) throw new ArgumentException("transfer_id 不存在");
        t.parts.Add(data);
        return Task.CompletedTask;
    }

    public Task<OneBotUploadResult> UploadFileFragmentedFinishAsync(string transferId)
    {
        if (!_transfers.Remove(transferId, out var t)) throw new ArgumentException("transfer_id 不存在");
        var id = $"file{Interlocked.Increment(ref _messageIdCounter)}";
        _files[id] = t.parts.SelectMany(x => x).ToArray();
        return Task.FromResult(new OneBotUploadResult(id));
    }

    public Task UploadFileFragmentedAbortAsync(string transferId)
    {
        _transfers.Remove(transferId, out _);
        return Task.CompletedTask;
    }

    public Task<byte[]> DownloadFileAsync(string fileId)
        => _files.TryGetValue(fileId, out var b)
            ? Task.FromResult(b)
            : throw new OneBotNotImplementedException("download_file");

    public Task SendLikeAsync(string userId, int times = 1)
        => throw new OneBotNotImplementedException("send_like");

    // ==================== 事件分发 ====================

    internal async Task OnInboundMessage(string platform, InboundMessage m)
    {
        var eventId = Interlocked.Increment(ref _messageIdCounter);
        var evt = new
        {
            id = $"evt{eventId}",
            time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            type = "message",
            detail_type = "private",          // ImBot 的会话模型统一映射为 private
            sub_type = platform,
            @self = new { user_id = "imbot" },
            message_id = $"in{eventId}",
            message = new object[] { new { type = "text", data = new { text = m.Text } } },
            alt_message = m.Text,
            user_id = m.SenderId,
        };
        var json = JsonSerializer.Serialize(evt);
        await BroadcastWsAsync(json);
        await PostWebhooksAsync(json);
    }

    private async Task BroadcastWsAsync(string json)
    {
        foreach (var c in _wsClients.Values.ToList())
            await c.SendAsync(json);
    }

    internal void Register(WsClient client) => _wsClients[client.Id] = client;
    internal void Unregister(WsClient client) => _wsClients.TryRemove(client.Id, out _);

    private async Task PostWebhooksAsync(string json)
    {
        foreach (var url in _cfg.Webhooks)
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                await Http.PostAsync(url, content);
            }
            catch (Exception ex) { Console.WriteLine($"[adapter-onebot] webhook {url} 失败: {ex.Message}"); }
        }
    }
}

/// <summary>标准响应（status/retcode/data/message/echo）。</summary>
public record OneBotResponse(string Status, object? Data, int Retcode, string? Message);
