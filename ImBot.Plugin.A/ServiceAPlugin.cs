using System;
using System.Threading.Tasks;
using ImBot.Core;

namespace ImBot.Plugins;

/// <summary>
/// A 插件：提供底层能力 GreetApi.A()（发送"你也好"）。
/// 其它插件经 ctx.Require&lt;IGreetApi&gt; 调用；本插件卸载时 API 随 Provide 回滚消失，
/// 所有正在用它的插件会被宿主级联卸载。
/// </summary>
[Plugin("plugin-a", "提供 GreetApi：A 方法向会话发送\"你也好\"")]
public sealed class ServiceAPlugin : IComponent
{
    public string Name => "plugin-a";

    public Task StartAsync(IContext ctx)
    {
        ctx.Provide<IGreetApi>(new GreetApi(ctx));
        Console.WriteLine("[plugin-a] GreetApi 已发布");
        return Task.CompletedTask;
    }

    /// <summary>A 方法的实现：懒解析当前适配器发送，不绑定具体适配器实例（适配器可换）。</summary>
    private sealed class GreetApi(IContext ctx) : IGreetApi
    {
        public async Task GreetBack(string conversationId)
        {
            var adapter = ctx.Resolve<IAdapter>();
            if (adapter == null)
            {
                Console.WriteLine("[plugin-a] 无适配器在线，无法发送");
                return;
            }
            await adapter.SendTextAsync(conversationId, "你也好");
        }
    }
}
