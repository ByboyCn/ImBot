using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ImBot.Core;

namespace ImBot.Plugins;

/// <summary>
/// B 插件：监听消息，收到"你好"时调用 A 插件发布的 IGreetApi.A 方法（GreetBack）。
/// 演示插件间调用：B 不认识 A 的实现，只依赖 Core 里的契约 IGreetApi；
/// A 卸载后 IGreetApi 消失，B 的 onRemove 触发（实际会被宿主级联卸载）。
/// </summary>
[Plugin("plugin-b", "收到\"你好\"调用 A 插件的 GreetApi.A()")]
public sealed class CallerBPlugin : IComponent
{
    private readonly Dictionary<IAdapter, Func<InboundMessage, Task>> _subs = new();
    private IGreetApi? _greetApi;

    public string Name => "plugin-b";

    public Task StartAsync(IContext ctx)
    {
        // 依赖 1：A 插件的 API（没上线就静默等待）
        ctx.Require<IGreetApi>(
            onAdd: api => { _greetApi = api; Console.WriteLine("[plugin-b] GreetApi 可用"); },
            onRemove: _ => { _greetApi = null; Console.WriteLine("[plugin-b] GreetApi 消失（A 已卸载？）"); });

        // 依赖 2：适配器消息流
        ctx.Require<IAdapter>(
            onAdd: adapter =>
            {
                Console.WriteLine($"[plugin-b] listening on '{adapter.Platform}'");
                Func<InboundMessage, Task> handler = async m =>
                {
                    if (m.Text?.Trim() != "你好") return;
                    if (_greetApi == null)
                    {
                        Console.WriteLine("[plugin-b] 收到你好，但 GreetApi 不可用（plugin-a 未加载）");
                        return;
                    }
                    Console.WriteLine("[plugin-b] 收到你好 -> 调用 plugin-a 的 GreetBack()");
                    await _greetApi.GreetBack(m.ConversationId);   // 跨插件调用 A 方法
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
