using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ImBot.Core;

namespace ImBot.Plugins;

/// <summary>
/// 打招呼插件：直接监听适配器消息（非命令），收到"你好"回复"你也好"。
/// 订阅生命周期双保险：本插件卸载时 journal 回滚摘订阅；适配器卸载时 onRemove 摘订阅。
/// </summary>
[Plugin("cmd-greet", "收到\"你好\"回复\"你也好\"")]
public sealed class GreetPlugin : IComponent
{
    private readonly Dictionary<IAdapter, Func<InboundMessage, Task>> _subs = new();

    public string Name => "cmd-greet";

    public Task StartAsync(IContext ctx)
    {
        ctx.Require<IAdapter>(
            onAdd: adapter =>
            {
                Console.WriteLine($"[cmd-greet] listening on '{adapter.Platform}'");
                Func<InboundMessage, Task> handler = async m =>
                {
                    if (m.Text?.Trim() == "你好")
                        await adapter.SendTextAsync(m.ConversationId, "你也好");
                };
                _subs[adapter] = handler;
                adapter.OnMessage += handler;
                // 本插件卸载时回滚：摘掉所有订阅
                ctx.Track(
                    () => { },
                    () => { if (_subs.Remove(adapter, out var h)) adapter.OnMessage -= h; });
            },
            onRemove: adapter =>
            {
                // 适配器先卸载：摘掉订阅，防止事件残留在死适配器上
                if (_subs.Remove(adapter, out var h)) adapter.OnMessage -= h;
                Console.WriteLine($"[cmd-greet] adapter '{adapter.Platform}' gone");
            });
        return Task.CompletedTask;
    }
}
