# ImBot —— 时空可组合 IM 机器人架构

> 范式来源：arXiv:2608.25512《A Programming Paradigm for Spatiotemporal Composibility》
> 运行：`dotnet run`（交互，stdin 即 IM 消息）／`dotnet run -- demo`（脚本演示）

## 1. 架构总览

```
宿主 (Program)
 ├─ Composition（组合根：LoadAsync/UnloadAsync，每组件独立 ScopeContext）
 ├─ 宿主级服务（不参与装卸）：IAdminApi + 插件目录（name -> factory）
 └─ 宿主控制通道 HostCliLoop：stdin ':' 前缀 = 管理操作（:list/:load/:unload）
      ↓ 其余行
 ┌─ 插件平面（全部可装卸）────────────────────────┐
 │  adapter-console  Provide<IAdapter>             │ ← 平台适配器（QQ/企微/TG 同构）
 │  router           Provide<ICommandRegistry>     │ ← Require<IAdapter>，订阅绑定适配器生命周期
 │  cmd-help/echo    Require<ICommandRegistry>     │ ← 业务命令
 │  admin            Require<IAdminApi+ICommandRegistry> ← 聊天内装卸（依赖宿主服务）
 └─────────────────────────────────────────────────┘
```

## 2. 核心机制（论文两维度的实现）

| 机制 | 实现 | 保证 |
|---|---|---|
| 可撤销效果 | `IContext.Track(apply, revert)`，每组件 journal 栈，卸载 LIFO 回滚 | 时间可组合性 |
| 响应式依赖 | `Provide<T>`(本身是效果) + `Require<T>(onAdd, onRemove)`，同类型多实例 | 空间可组合性 |
| 统一上下文 | `ScopeContext` 同时承载 Track/Provide/Require，全程持锁串行 | 交错安全 |
| 订阅生命周期 | router 对每个适配器 onAdd 挂订阅、onRemove 摘订阅（绑定提供者，不是订阅者） | 适配器下线即静默 |

## 3. 关键设计决策（含踩过的坑）

1. **管理通道与被管理平面分离**（论文 6.3）：聊天内 `/plugins` 是 admin *插件*，把它卸了就没人能装回——
   所以宿主必须有带外通道（HostCliLoop 的 `:` 命令；生产可换成 Unix socket / Web 端口）。
2. **订阅挂在提供者生命周期上**：若 router 把事件订阅 Track 进自己的 journal，适配器卸载时不会摘订阅
   （曾实际踩坑）。正确做法：onAdd 挂、onRemove 摘。
3. **Provide 是可撤销效果**：适配器卸载 = Provide 回滚 = 依赖者 onRemove 自动触发，无需任何显式通知。
4. **卸载后的旧实例是死的**：重载适配器产生新实例，旧实例上的消息应静默丢弃——这是正确行为，不是 bug。
5. **依赖失败 ≠ 异常**：Require 未满足时组件静默待命，先装命令后装路由也能收敛。

## 4. 接新平台（如企业微信）

写一个 `AdapterComponent` 派生类：
- `CreateAdapter()` 返回实现 `IAdapter` 的包装（连到平台 SDK）；
- `ConnectAsync()` 里启动收消息轮询/长连接（`CancellationToken` 由基类在卸载时触发）；
- 发消息走 `SendTextAsync`。

参考已有对接草稿：D:\source\c#Plugin\wxwork（WxWork 协议库；需补 NewWXWorkPb/xbpb 才能编译）。

## 5. 文件清单

- `Core/Domain.cs` —— 领域接口（InboundMessage/IAdapter/ICommandRegistry/IAdminApi/IComponent/IContext）
- `Core/Context.cs` —— RootContext/ScopeContext/Composition（范式核心，约 200 行）
- `Plugins/RouterComponent.cs` —— 命令路由
- `Plugins/Adapters.cs` —— AdapterComponent 基类 + ConsoleAdapter
- `Plugins/Commands.cs` —— help/echo/admin 命令插件
- `Program.cs` —— 宿主：目录、管理 API、控制通道、demo
