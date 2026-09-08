namespace ImBot.PluginA.Api;

/// <summary>
/// 插件 A 对外公开的 API 契约。
/// 放在独立的 Api 类库（部署到宿主主目录、默认 ALC 加载）：
/// 提供方（plugin-a）与消费方（plugin-b）各自独立 ALC，经此共享类型身份，
/// ImBot.Core 框架层无需感知任何插件契约。
/// </summary>
public interface IGreetApi
{
    /// <summary>A 方法：向指定会话发送"你也好"。</summary>
    Task GreetBack(string conversationId);

    /// <summary>B 方法：向指定会话发送"这是B方法"。</summary>
    Task MethodB(string conversationId);

    /// <summary>C 方法：向指定会话发送"这是C方法"。</summary>
    Task MethodC(string conversationId);

    /// <summary>D 方法：向指定会话发送"这是D方法"。</summary>
    Task MethodD(string conversationId);
}
