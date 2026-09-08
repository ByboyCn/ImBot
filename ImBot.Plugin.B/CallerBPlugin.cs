using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ImBot.Core;

namespace ImBot.Plugins;

/// <summary>
/// B 插件：监听消息，按文本分发调用 A 插件 GreetApi 的不同方法：
/// 发 "1" → A 方法；发 "B" → B 方法；发 "3" → D 方法；发 "aaa" → C 方法。
/// B 不认识 A 的实现，只依赖 Core 里的契约 IGreetApi。
/// </summary>
[Plugin("plugin-b", "消息分发：1→A方法，B→B方法，3→D方法，aaa→C方法")]
public sealed class CallerBPlugin : IComponent
{
    private readonly Dictionary<IAdapter, Func<InboundMessage, Task>> _subs = new();
    private IGreetApi? _api;

    public string Name => "plugin-b";

    public Task StartAsync(IContext ctx)
    {
        // 依赖 1：A 插件的 API（没上线就静默等待）
        ctx.Require<IGreetApi>(
            onAdd: api => { _api = api; Console.WriteLine("[plugin-b] GreetApi 可用"); },
            onRemove: _ => { _api = null; Console.WriteLine("[plugin-b] GreetApi 消失（A 已卸载？）"); });

        // 依赖 2：适配器消息流
        ctx.Require<IAdapter>(
            onAdd: adapter =>
            {
                Console.WriteLine($"[plugin-b] listening on '{adapter.Platform}'");
                Func<InboundMessage, Task> handler = async m =>
                {
                    if (_api == null) return;
                    var text = m.Text?.Trim();
                    switch (text)
                    {
                        case "1":
                            Console.WriteLine("[plugin-b] 收到 1 -> 调用 A 方法 GreetBack()");
                            await _api.GreetBack(m.ConversationId);
                            break;
                        case "B":
                            Console.WriteLine("[plugin-b] 收到 B -> 调用 B 方法 MethodB()");
                            await _api.MethodB(m.ConversationId);
                            break;
                        case "3":
                            Console.WriteLine("[plugin-b] 收到 3 -> 调用 D 方法 MethodD()");
                            await _api.MethodD(m.ConversationId);
                            break;
                        case "aaa":
                            Console.WriteLine("[plugin-b] 收到 aaa -> 调用 C 方法 MethodC()");
                            await _api.MethodC(m.ConversationId);
                            break;
                    }
                };
                _subs[adapter] = handler;
                adapter.OnMessage += handler;
                ctx.Track(() => { }, () => { if (_subs.Remove(adapter, out var h)) adapter.OnMessage -= h; });
            },
            onRemove: adapter =>
            {
                if (_subs.Remove(adapter, out var h)) adapter.OnMessage -= h;
            });
        return Task.CompletedTask;
    }
}
