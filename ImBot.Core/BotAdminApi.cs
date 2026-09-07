using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ImBot.Core;

/// <summary>
/// 宿主级管理 API（进程生命周期，不可卸载）——目录 + 装卸 + 级联卸载。
/// 卸载 A 时，所有正在消费 A 所提供服务的已加载组件会被一并卸载（递归）。
/// </summary>
public sealed class BotAdminApi(Composition app, Dictionary<string, Func<IComponent>> catalog) : IAdminApi
{
    /// <summary>卸载完成后的回调（宿主用它回收插件的 AssemblyLoadContext）。</summary>
    public Action<string>? AfterUnload;

    /// <summary>目录 miss 时的重扫描钩子（宿主实现：用 MetadataLoadContext 无锁发现）。</summary>
    public Func<int>? Rescan;

    /// <summary>删除钩子（宿主实现：删 dll 文件 + 清目录条目）。返回是否成功。</summary>
    public Func<string, Task<bool>>? DeleteImpl;

    /// <summary>上一次 LoadAsync 返回 false 是否因为"已加载"（供调用方区分提示）。</summary>
    public bool AlreadyLoaded { get; private set; }

    public IReadOnlyList<string> Loaded => app.Loaded;

    /// <summary>目录 = plugins\ 文件夹的实时内容：每次查询都重扫描，新丢进来的 dll 立即可见。</summary>
    public IReadOnlyList<string> Catalog()
    {
        Rescan?.Invoke();
        return catalog.Keys.ToList();
    }

    public async Task<bool> LoadAsync(string name)
    {
        // 已加载：幂等返回失败（由调用方提示），不能让 Composition 抛异常炸掉宿主
        if (app.IsLoaded(name)) { AlreadyLoaded = true; return false; }
        AlreadyLoaded = false;
        if (!catalog.TryGetValue(name, out var f))
        {
            Rescan?.Invoke();
            if (!catalog.TryGetValue(name, out f)) return false;
        }
        try { await app.LoadAsync(f()); }
        catch (Exception ex)
        {
            Console.WriteLine($"[host] 加载 {name} 失败: {ex}");
            return false;
        }
        return true;
    }

    public async Task<bool> UnloadAsync(string name) => await UnloadCascadeAsync(name, root: true);

    /// <summary>删除插件：先卸载（含级联），再删 dll 文件，最后从目录移除。</summary>
    public async Task<bool> DeleteAsync(string name)
    {
        if (app.IsLoaded(name) && !await UnloadCascadeAsync(name, root: true))
            return false;
        if (DeleteImpl == null) return false;
        return await DeleteImpl(name);
    }

    /// <summary>卸载并级联：期间产生的孤儿（正在消费本组件服务的已加载组件）递归卸载。</summary>
    private async Task<bool> UnloadCascadeAsync(string name, bool root)
    {
        if (!app.IsLoaded(name)) return false;

        var orphans = new List<string>();
        void OnOrphaned(string dependent, Type t)
        {
            // 只级联仍处于加载状态的组件（卸载链上的会被 IsLoaded 过滤）
            if (app.IsLoaded(dependent) && dependent != name) orphans.Add(dependent);
        }

        app.Root.DependentOrphaned += OnOrphaned;
        try { if (!await app.UnloadAsync(name)) return false; }
        finally { app.Root.DependentOrphaned -= OnOrphaned; }

        try { AfterUnload?.Invoke(name); }
        catch (Exception ex) { Console.WriteLine($"[host] AfterUnload({name}) 失败: {ex.Message}"); }

        // 级联卸载：正在消费 name 服务的依赖者全部卸掉（递归，直到没有新孤儿）
        foreach (var dep in orphans.Distinct().ToList())
        {
            if (!app.IsLoaded(dep)) continue;
            Console.WriteLine($"[host] 级联卸载 {dep}（依赖了 {name} 的服务）");
            await UnloadCascadeAsync(dep, root: false);
        }
        if (root && orphans.Count > 0)
            Console.WriteLine($"[host] {name} 卸载完成，级联卸载了 {orphans.Distinct().Count()} 个依赖插件");
        return true;
    }
}
