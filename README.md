# ImBot — 时空可组合的 C# IM 机器人框架

基于论文 [《A Programming Paradigm for Spatiotemporal Composability》](https://arxiv.org/abs/2608.25512)（Cordis / 时空可组合性范式）实现的插件式 IM 机器人框架：**宿主永远不变，一切皆插件**。

## 特性

- **时空可组合性**（论文核心落地）
  - **时间可组合**：插件的一切修改都是"可撤销效果"（revertible effect），卸载时按 LIFO 完整回滚——资源、事件订阅、命令注册全部自动清理，插件作者**不写任何清理代码**
  - **空间可组合**：插件通过 `Provide<T>` / `Require<T>` 声明服务与依赖；提供者上线依赖者自动激活，下线自动停用（静默等待，不报错）
  - **级联卸载**：卸载 A 时，所有正在消费 A 服务的插件被递归卸载（卸适配器 → 路由 → 命令全下）
- **插件热插拔**
  - 宿主 `ImBot.Host.exe` 永不改动；插件是 `plugins\` 目录里的独立 dll
  - `MetadataLoadContext` 无锁发现（不锁文件，卸载后 dll 可立即删除/替换）
  - 真正加载时才装入独立 collectible `AssemblyLoadContext`，卸载后 GC 回收、**文件锁释放**
  - 运行中装卸：`:load / :unload / :delete`（宿主控制台）、`/plugins`（聊天命令）、桌面 UI 按钮
- **桌面控制台也是插件**：`ui-console` 基于 [TCYM.UI](https://tcym.top:8035/tcym/UI/Doc/index.html)（SkiaSharp/SDL），因 UI 框架生命周期为进程级，采用独立子进程 + stdin/stdout JSON 协议——UI 崩溃/卡死不影响宿主
- **首个目标平台**：控制台适配器（演示）；企业微信适配器开发中（对接私有 wxwork 协议库）

## 结构

```
ImBot.slnx
├── ImBot.Core/          框架核心：Context / Composition / Provide-Require / 级联卸载
├── ImBot.Host/          唯一 exe（宿主）：扫描装配 + 带外管理通道 + --ui 子进程分支
├── ImBot.Router/        命令路由插件（/ 前缀命令分发）
├── ImBot.ConsoleAdapter/控制台适配器插件（模拟 IM 平台）
├── ImBot.Cmd.Help/      /help 命令插件
├── ImBot.Cmd.Echo/      /echo 命令插件
├── ImBot.Cmd.Admin/     /plugins 聊天内插件管理
├── ImBot.Cmd.Greet/     收到"你好"回复"你也好"（消息订阅型插件示例）
├── ImBot.UiConsole/     TCYM.UI 桌面控制台插件（插件管理 + 消息测试）
└── Directory.Build.props  插件自动部署：编译后 dll → Host\plugins\
```

## 快速开始

```bash
dotnet build ImBot.slnx
dotnet run --project ImBot.Host

# 宿主控制台命令（: 前缀）
:list                    # 查看目录（= plugins\ 实时内容）与已加载
:load *                  # 全量加载
:load router             # 加载指定插件
:unload adapter-console  # 卸载（级联卸载依赖它的插件）
:delete cmd-echo         # 卸载 + 删除 dll 文件 + 移除条目

# 聊天（直接输入，console 适配器）
/help
/echo 你好
你好                     # → 你也好（cmd-greet 插件）
```

桌面 UI 窗口随 `ui-console` 插件自动拉起：插件管理页装卸/删除，消息测试页可与机器人对话。

## 写一个新插件

1. 新建类库项目 `ImBot.Cmd.Xxx`（引用 `ImBot.Core`），加入 slnx
2. 实现插件类：

```csharp
[Plugin("cmd-xxx", "插件描述（宿主扫描时自动读取）")]
public sealed class XxxPlugin : IComponent
{
    public string Name => "cmd-xxx";

    public Task StartAsync(IContext ctx)
    {
        ctx.Require<ICommandRegistry>(reg =>
            ctx.Track(
                () => reg.Register("xxx", "说明", Handle),
                () => reg.Unregister("xxx")));   // 卸载时自动回滚，无需写清理
        return Task.CompletedTask;
    }
}
```

3. `dotnet build` —— dll 自动进 `plugins\`，宿主立即发现，**零改动**。

提供底层能力的插件用 `ctx.Provide<T>(service)`；依赖它的插件用 `ctx.Require<T>(onAdd, onRemove)` 挂载/摘除。

## 设计要点

- **管理平面与被管理平面分离**：管理 API（`IAdminApi`）是宿主级服务，不随任何插件卸载——卸掉 admin/UI 后仍可用 `:` 命令装回
- **发现与加载分离**：目录扫描用 MetadataLoadContext（只读元数据、不锁文件），`:load` 才真正加载
- **UI 插件 = 子进程**：卸载即杀进程（进程级时间可组合性），重载即重新拉起

## 依赖

- .NET 10
- [TCYM.UI](https://www.nuget.org/packages/TCYM.UI) 0.1.1.18（仅 ui-console）
