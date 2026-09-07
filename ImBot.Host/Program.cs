using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using ImBot.Core;

namespace ImBot.Host;

internal static class Program
{
    /// <summary>每个插件 dll 一个可回收 ALC：卸载后程序集可被 GC 回收，dll 文件锁释放、可删除。</summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        public PluginLoadContext() : base("plugin", isCollectible: true) { }
        protected override Assembly? Load(AssemblyName name)
        {
            // 依赖（ImBot.Core / TCYM.UI / SkiaSharp...）回落到默认上下文 —— 类型身份与宿主一致
            return null;
        }
    }

    public static async Task Main(string[] args)
    {
        // UI 子进程模式：反射加载 plugins\ImBot.UiConsole.dll（避免宿主进程静态引用，保证该 dll 在宿主内可回收）
        if (args.Contains("--ui"))
        {
            // LoadFrom 上下文只在 plugins\ 里找依赖；TCYM.UI 等在主目录，需钩子兜底解析
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                var p = Path.Combine(AppContext.BaseDirectory,
                    new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(p) ? Assembly.LoadFrom(p) : null;
            };
            var path = Path.Combine(AppContext.BaseDirectory, "plugins", "ImBot.UiConsole.dll");
            try
            {
                // 预载主目录依赖进默认上下文：LoadFrom(plugins/UiConsole) 的依赖解析
                // 只探查 plugins/，按标识能命中已加载程序集，但不会触发 AssemblyResolve 兜底
                foreach (var dep in new[] { "TCYM.UI.dll", "SkiaSharp.dll", "ImBot.ConsoleAdapter.dll" })
                {
                    var dp = Path.Combine(AppContext.BaseDirectory, dep);
                    if (File.Exists(dp)) Assembly.LoadFrom(dp);
                }
                var asm = Assembly.LoadFrom(path);
                asm.GetType("ImBot.UI.ChildUi")!
                   .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!
                   .Invoke(null, null);
            }
            catch (Exception ex)
            {
                // 全量异常 + 已加载程序集写入文件（stderr 在管道场景下会被截断）
                var sb = new System.Text.StringBuilder(ex.ToString());
                sb.AppendLine();
                sb.AppendLine("=== loaded assemblies ===");
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    sb.AppendLine($"{a.FullName}  <-  {(a.IsDynamic ? "(dynamic)" : a.Location)}");
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-crash.log"), sb.ToString());
                throw;
            }
            return;
        }

        // 控制台统一 UTF-8：stdin/stdout 与管道、UI 子进程（UTF-8 JSON）编码一致，中文不再乱码
        try { Console.InputEncoding = System.Text.Encoding.UTF8; } catch { }
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

        // ---- 插件目录：发现与加载分离 ----
        // 发现：MetadataLoadContext 只读元数据，不加载程序集、不锁文件 —— 卸载后 dll 可立即删除；
        //       （此前 Rescan 用真加载补条目，会把刚释放的文件锁又锁上）
        // 加载：:load 时才把 dll 装进独立 collectible ALC。
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        var catalog = new Dictionary<string, Func<IComponent>>(StringComparer.Ordinal);
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        // name -> (dll 路径, 类型全名, 描述) —— 仅元数据，无程序集引用
        var meta = new Dictionary<string, (string dll, string typeName, string desc)>(StringComparer.Ordinal);
        var typeMap = new Dictionary<string, (Type type, PluginLoadContext alc)>(StringComparer.Ordinal);

        var app = new Composition();
        var api = new BotAdminApi(app, catalog);

        // 发现(MetadataLoadContext 持文件句柄)与删除互斥，避免撞句柄
        var scanLock = new object();

        // 注意：签名只能用 Core 类型（不能出现 ConsoleAdapter），否则创建委托时会强制加载
        // 插件程序集（在 plugins\ 下，默认上下文解析不到）
        IAdapter? LiveAdapter() =>
            app.HostContext.ResolveAll<IAdapter>().LastOrDefault(a => a.Platform == "console");

        // 发现（无锁）：读 [Plugin] 特性建目录条目，工厂延迟到 Load 才真正加载 dll
        void Discover(bool onlyNew)
        {
            lock (scanLock)
            DiscoverCore(onlyNew);
        }
        void DiscoverCore(bool onlyNew)
        {
            {
            var dlls = Directory.Exists(pluginsDir)
                ? Directory.GetFiles(pluginsDir, "*.dll") : Array.Empty<string>();
            if (dlls.Length == 0) return;

            var resolverPaths = new List<string>(dlls);
            resolverPaths.AddRange(Directory.GetFiles(AppContext.BaseDirectory, "*.dll"));
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            resolverPaths.AddRange(Directory.GetFiles(runtimeDir, "*.dll"));
            using var mlc = new MetadataLoadContext(new PathAssemblyResolver(resolverPaths.Distinct().ToList()));

            foreach (var dll in dlls)
            {
                System.Reflection.Assembly asm;
                try { asm = mlc.LoadFromAssemblyPath(dll); }
                catch (Exception ex) { Console.WriteLine($"[host] 跳过 {dll}: {ex.Message}"); continue; }

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }

                foreach (var t in types)
                {
                    // MLC 类型与运行时类型身份不同，按名字判定 IComponent
                    if (t.IsAbstract || t.GetInterfaces().All(i => i.Name != "IComponent")) continue;
                    var attr = t.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "PluginAttribute");
                    var name = attr?.ConstructorArguments.Count > 0 ? attr.ConstructorArguments[0].Value as string : null;
                    var desc = attr?.ConstructorArguments.Count > 1 ? attr.ConstructorArguments[1].Value as string : null;
                    name ??= t.Name;
                    desc = string.IsNullOrWhiteSpace(desc) ? "(无描述)" : desc!;

                    if (onlyNew && meta.TryGetValue(name, out var prev) && prev.dll == dll) continue;   // 已知且未换文件

                    if (meta.TryGetValue(name, out var old) && old.dll != dll && typeMap.ContainsKey(name))
                        continue;   // 已加载旧文件，跳过避免干扰；卸载后才会捡起新文件

                    meta[name] = (dll, t.FullName!, desc);
                    descriptions[name] = desc;
                    var capturedName = name;
                    catalog[name] = () => LoadComponent(capturedName);
                    Console.WriteLine($"[host] 发现插件: {name} ({Path.GetFileName(dll)}) - {desc}");
                }
            }

            // dll 已被删除的条目移除
            foreach (var gone in meta.Where(kv => !File.Exists(kv.Value.dll)).Select(kv => kv.Key).ToList())
            {
                meta.Remove(gone);
                catalog.Remove(gone);
                Console.WriteLine($"[host] 移除条目 {gone}（dll 已删除）");
            }
            }
        }

        // 真正加载：独立 collectible ALC，从此持有 dll 文件锁直到卸载回收
        IComponent LoadComponent(string name)
        {
            var (dll, typeName, _) = meta[name];
            var alc = new PluginLoadContext();
            var asm = alc.LoadFromAssemblyPath(dll);
            var t = asm.GetType(typeName) ?? throw new InvalidOperationException($"{dll} 中找不到 {typeName}");
            typeMap[name] = (t, alc);
            return Create(t, api, descriptions, LiveAdapter);
        }

        // 卸载完成 -> 回收 ALC -> GC -> 文件锁释放（目录条目保留，:load 随时装回）
        api.AfterUnload = name =>
        {
            if (!typeMap.Remove(name, out var e)) return;
            var weak = new WeakReference(e.alc);
            e.alc.Unload();
            for (int i = 0; i < 15 && weak.IsAlive; i++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                if (weak.IsAlive) Thread.Sleep(150);
            }
            Console.WriteLine($"[host] {name} 程序集已{(weak.IsAlive ? "回收未完成（仍被引用）" : "回收")}，dll 文件锁已释放");
        };

        // 删除实现：卸载已由 DeleteAsync 完成，这里删文件（重试应对杀毒/句柄延迟）+ 清条目
        api.DeleteImpl = async name =>
        {
            if (!meta.TryGetValue(name, out var m)) return false;
            for (int i = 0; i < 15; i++)
            {
                try { lock (scanLock) File.Delete(m.dll); break; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && i < 14)
                {
                    // 文件可能仍被内存映射（ALC 回收中）或被并发扫描短暂持有
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    await Task.Delay(300);
                }
                catch (Exception ex) { Console.WriteLine($"[host] 删除 {m.dll} 失败: {ex.Message}"); return false; }
            }
            meta.Remove(name);
            catalog.Remove(name);
            Console.WriteLine($"[host] 已删除 {Path.GetFileName(m.dll)}");
            return true;
        };

        Discover(onlyNew: false);
        api.Rescan = () => { Discover(onlyNew: true); return 0; };

        if (catalog.Count == 0) Console.WriteLine($"[host] 警告：{pluginsDir} 下没有发现插件");

        // 宿主级服务：管理平面与被管理插件平面分离
        app.ProvideHost<IAdminApi>(api);

        // 初始加载扫描到的全部插件
        foreach (var name in catalog.Keys.ToList())
        {
            try { await app.LoadAsync(catalog[name]()); }
            catch (Exception ex) { Console.WriteLine($"[host] 加载 {name} 失败: {ex}"); }
        }

        Console.WriteLine($"宿主已启动：{catalog.Count} 个插件（扫描自 plugins\\）");
        Console.WriteLine("宿主命令: :list | :load <name> | :unload <name> | :delete <name>");

        // 宿主控制通道：stdin ':' 命令；UI 窗口关闭后宿主仍存活
        while (true)
        {
            var line = Console.ReadLine();
            if (line == null) { await Task.Delay(200); continue; }
            if (line.Length == 0) continue;

            // ':' = 宿主管理命令；其余行 = 聊天消息，转发给当前存活的 console 适配器
            if (!line.StartsWith(':'))
            {
                var adapter = LiveAdapter();
                if (adapter == null)
                    Console.WriteLine("[host] 无 console 适配器在线（:load adapter-console）");
                else
                    await adapter.RaiseUserMessageAsync("me", line);
                continue;
            }

            var parts = line[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts)
            {
                case ["list"]:
                    Console.WriteLine($"已加载: {string.Join(", ", api.Loaded)}\n目录: {string.Join(", ", api.Catalog())}");
                    break;
                case ["load", "*"]:
                    // 全量加载：目录里所有尚未加载的插件
                    foreach (var n in api.Catalog().Where(n => !api.Loaded.Contains(n)).ToList())
                        if (await api.LoadAsync(n)) Console.WriteLine($"[host] loaded {n}");
                    Console.WriteLine($"[host] 全量加载完成：{api.Loaded.Count} 个已加载，目录共 {api.Catalog().Count} 个");
                    break;
                case ["load", var name]:
                    // 直接走 api：目录 miss 时内部会 Rescan 重扫 plugins\（支持卸载后重装/换新 dll）
                    if (await api.LoadAsync(name)) Console.WriteLine($"[host] loaded {name}");
                    else Console.WriteLine(api.AlreadyLoaded
                        ? $"[host] {name} 已处于加载状态" : $"[host] 加载失败：{name} 不在目录中");
                    break;
                case ["unload", var name]:
                    Console.WriteLine(await api.UnloadAsync(name)
                        ? $"[host] unloaded {name}" : $"[host] 卸载失败：{name} 未加载");
                    break;
                case ["delete", var name]:
                    Console.WriteLine(await api.DeleteAsync(name)
                        ? $"[host] deleted {name}（已卸载并删除 dll）" : $"[host] 删除失败：{name} 不在目录中");
                    break;
                default:
                    Console.WriteLine("[host] 用法: :list | :load <name> | :unload <name> | :delete <name>");
                    break;
            }
        }
    }

    /// <summary>实例化插件；实现 IHostInjectedPlugin 的注入宿主服务。</summary>
    private static IComponent Create(Type t, BotAdminApi api,
        IReadOnlyDictionary<string, string> descriptions, Func<IAdapter?> liveAdapter)
    {
        var comp = (IComponent)Activator.CreateInstance(t)!;
        if (comp is IHostInjectedPlugin inj)
        {
            inj.AdminApi = api;
            inj.Descriptions = descriptions;
            inj.AdapterLookup = liveAdapter;
        }
        return comp;
    }
}
