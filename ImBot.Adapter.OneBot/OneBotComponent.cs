using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImBot.Core;
using ImBot.OneBot.Api;

namespace ImBot.Plugins;

/// <summary>
/// OneBot 12 标准接口适配器：
/// - HTTP  : http://127.0.0.1:5700/   （POST {"action","params"} 或 POST /{action}）
/// - WS    : ws://127.0.0.1:5701/ws  （双向：推送事件 / 接收 action 调用）
/// 事件源 = 宿主内全部 IAdapter 的入站消息；动作执行 = 路由到当前存活适配器。
/// 配置：宿主目录 onebot.json { "httpPort": 5700, "wsPort": 5701, "webhooks": ["http://..."] }
/// </summary>
[Plugin("adapter-onebot", "OneBot 12 标准接口：HTTP + WebSocket，完整动作集")]
public sealed class OneBotComponent : IComponent
{
    public string Name => "adapter-onebot";

    public Task StartAsync(IContext ctx)
    {
        var cfg = OneBotConfig.Load();
        var hub = new OneBotHub(ctx, cfg);

        // 事件桥：所有适配器的入站消息 -> OneBot message 事件（推 WS + webhook）
        var bridgeSubs = new Dictionary<IAdapter, Func<InboundMessage, Task>>();
        ctx.Require<IAdapter>(
            onAdd: adapter =>
            {
                Func<InboundMessage, Task> h = m => hub.OnInboundMessage(adapter.Platform, m);
                bridgeSubs[adapter] = h;
                adapter.OnMessage += h;
                ctx.Track(() => { }, () => { if (bridgeSubs.Remove(adapter, out var x)) adapter.OnMessage -= x; });
                Console.WriteLine($"[adapter-onebot] bridging '{adapter.Platform}' events");
            },
            onRemove: adapter => { if (bridgeSubs.Remove(adapter, out var h)) adapter.OnMessage -= h; });

        // 对外提供完整类型化 API
        ctx.Provide<IOneBotApi>(hub);

        // HTTP + WS 服务器都是可撤销效果
        var http = new OneBotHttpServer(hub, cfg.HttpPort);
        var ws = new OneBotWsServer(hub, cfg.WsPort);
        ctx.Track(() => _ = http.RunAsync(), http.Stop);
        ctx.Track(() => _ = ws.RunAsync(), ws.Stop);

        return Task.CompletedTask;
    }
}

/// <summary>配置（宿主目录 onebot.json，可选）。</summary>
public sealed record OneBotConfig(int HttpPort, int WsPort, string[] Webhooks)
{
    public static OneBotConfig Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "onebot.json");
            if (!File.Exists(path)) return new(5700, 5701, []);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            return new(
                r.TryGetProperty("httpPort", out var h) ? h.GetInt32() : 5700,
                r.TryGetProperty("wsPort", out var w) ? w.GetInt32() : 5701,
                r.TryGetProperty("webhooks", out var wh)
                    ? wh.EnumerateArray().Select(x => x.GetString()!).ToArray() : []);
        }
        catch { return new(5700, 5701, []); }
    }
}
