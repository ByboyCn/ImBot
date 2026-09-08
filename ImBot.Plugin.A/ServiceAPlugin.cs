using System;
using System.Threading.Tasks;
using ImBot.Core;

namespace ImBot.Plugins;

/// <summary>
/// A 插件：提供底层能力 GreetApi 的 4 个方法（A/B/C/D），各向会话发送不同回复。
/// 其它插件经 ctx.Require&lt;IGreetApi&gt; 调用；本插件卸载时 API 随 Provide 回滚消失，
/// 所有正在用它的插件会被宿主级联卸载。
/// </summary>
[Plugin("plugin-a", "提供 GreetApi 的 A/B/C/D 四个方法")]
public sealed class ServiceAPlugin : IComponent
{
    public string Name => "plugin-a";

    public Task StartAsync(IContext ctx)
    {
        ctx.Provide<IGreetApi>(new GreetApi(ctx));
        Console.WriteLine("[plugin-a] GreetApi 已发布（A/B/C/D 四个方法）");
        return Task.CompletedTask;
    }

    /// <summary>实现：懒解析当前适配器发送，不绑定具体适配器实例（适配器可换）。</summary>
    private sealed class GreetApi(IContext ctx) : IGreetApi
    {
        public Task GreetBack(string conversationId) => Send(conversationId, "你也好");
        public Task MethodB(string conversationId) => Send(conversationId, "这是B方法");
        public Task MethodC(string conversationId) => Send(conversationId, "这是C方法");
        public Task MethodD(string conversationId) => Send(conversationId, "这是D方法");

        private async Task Send(string conversationId, string text)
        {
            var adapter = ctx.Resolve<IAdapter>();
            if (adapter == null)
            {
                Console.WriteLine("[plugin-a] 无适配器在线，无法发送");
                return;
            }
            await adapter.SendTextAsync(conversationId, text);
        }
    }
}
