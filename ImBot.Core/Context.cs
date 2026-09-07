using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ImBot.Core;

/// <summary>根上下文：全局服务表 + 观察者表。宿主持有，进程生命周期内存在。</summary>
public sealed class RootContext
{
    /// <summary>一个依赖订阅：属主组件 + 激活/停用回调 + 当前绑定的服务实例。</summary>
    internal sealed class Watcher
    {
        public required string Owner;                 // 订阅者组件名（"$host" 表示宿主）
        public required Action<object> Add;
        public required Action<object> Remove;
        public object? LastInstance;                  // 最近一次激活它的服务实例（null=待命中）
    }

    // 服务按类型聚合，支持同类型多实例（多个适配器）
    private readonly Dictionary<Type, List<object>> _services = new();
    private readonly Dictionary<Type, List<Watcher>> _watchers = new();

    internal object Lock { get; } = new();

    /// <summary>服务实例因属主卸载而消失时，对每个因此失去依赖的已激活组件触发一次。</summary>
    public event Action<string /*dependentName*/, Type /*serviceType*/>? DependentOrphaned;

    internal void AddService(Type t, object instance)
    {
        lock (Lock)
        {
            if (!_services.TryGetValue(t, out var list)) _services[t] = list = new();
            list.Add(instance);
            foreach (var w in SnapshotWatchers(t))
            {
                w.LastInstance = instance;
                w.Add(instance);
            }
        }
    }

    internal void RemoveService(Type t, object instance)
    {
        List<string> orphans;
        lock (Lock)
        {
            if (_services.TryGetValue(t, out var list)) list.Remove(instance);
            orphans = new();
            foreach (var w in SnapshotWatchers(t))
            {
                if (ReferenceEquals(w.LastInstance, instance))
                {
                    w.LastInstance = null;
                    w.Remove(instance);
                    if (w.Owner != "$host") orphans.Add(w.Owner);
                }
                else
                {
                    w.Remove(instance);   // 未被该实例激活的订阅者仅通知
                }
            }
        }
        // 锁外触发（处理方可能再进锁）
        foreach (var o in orphans.Distinct())
            DependentOrphaned?.Invoke(o, t);
    }

    internal List<object> GetServices(Type t) =>
        _services.TryGetValue(t, out var l) ? l.ToList() : new();

    private List<Watcher> SnapshotWatchers(Type t) =>
        _watchers.TryGetValue(t, out var l) ? l.ToList() : new();

    internal Watcher AddWatcher(Type t, string owner, Action<object> add, Action<object> remove)
    {
        var w = new Watcher { Owner = owner, Add = add, Remove = remove };
        if (!_watchers.TryGetValue(t, out var l)) _watchers[t] = l = new();
        l.Add(w);
        return w;
    }

    internal void RemoveWatcher(Type t, Watcher w)
    {
        if (_watchers.TryGetValue(t, out var l))
            l.RemoveAll(x => ReferenceEquals(x, w));
    }

    /// <summary>为宿主级服务（IAdminApi 等）直接注册，不进任何 journal。</summary>
    public void ProvideHost<T>(T instance) where T : class
    {
        if (!_services.TryGetValue(typeof(T), out var list)) _services[typeof(T)] = list = new();
        list.Add(instance);
    }
}

/// <summary>组件作用域：dispose 时 LIFO 回滚该组件的全部效果、退订全部依赖。</summary>
public sealed class ScopeContext : IContext
{
    private readonly RootContext _root;
    private readonly Stack<Action> _journal = new();
    private readonly List<(Type t, RootContext.Watcher w)> _subs = new();

    internal ScopeContext(RootContext root, string ownerName) { _root = root; OwnerName = ownerName; }

    /// <summary>本作用域的组件名（级联卸载时用于定位依赖者）。</summary>
    internal string OwnerName { get; }

    internal bool Disposed { get; private set; }

    public void Track(Action apply, Action revert)
    {
        lock (_root.Lock)
        {
            if (Disposed) throw new ObjectDisposedException("component scope");
            apply();
            _journal.Push(revert);
        }
    }

    public void Track(Action apply, Func<Action> revertFactory)
    {
        lock (_root.Lock)
        {
            if (Disposed) throw new ObjectDisposedException("component scope");
            apply();
            _journal.Push(revertFactory());
        }
    }

    public T? Resolve<T>() where T : class => ResolveAll<T>().FirstOrDefault();

    public IReadOnlyList<T> ResolveAll<T>() where T : class =>
        _root.GetServices(typeof(T)).Cast<T>().ToList();

    public void Provide<T>(T instance) where T : class
    {
        var t = typeof(T);
        lock (_root.Lock)
        {
            if (Disposed) throw new ObjectDisposedException("component scope");
            _root.AddService(t, instance);          // apply：注册 + 通知所有依赖者
            _journal.Push(() => _root.RemoveService(t, instance));  // revert（触发孤儿检测）
        }
    }

    public void Require<T>(Action<T> onAdd, Action<T>? onRemove = null) where T : class
    {
        var t = typeof(T);
        lock (_root.Lock)
        {
            if (Disposed) throw new ObjectDisposedException("component scope");
            var w = _root.AddWatcher(t, OwnerName, o => onAdd((T)o), o => onRemove?.Invoke((T)o));
            _subs.Add((t, w));
            // 订阅时立即对已有实例逐个回调（activating）
            foreach (var existing in _root.GetServices(t).ToList())
            {
                w.LastInstance = existing;
                w.Add(existing);
            }
        }
    }

    /// <summary>卸载组件：先摘自己的订阅，再 LIFO 回滚所有效果（回滚时触发孤儿检测）。</summary>
    public void Dispose()
    {
        lock (_root.Lock)
        {
            if (Disposed) return;
            Disposed = true;
            foreach (var (t, w) in _subs)
            {
                if (w.LastInstance is { } inst) w.Remove(inst);   // 通知停用
                _root.RemoveWatcher(t, w);
            }
            while (_journal.Count > 0)
                _journal.Pop()();
        }
    }
}

/// <summary>
/// 组合根（论文第 4 章 orchestration）：加载/卸载组件。
/// 每个组件一个独立 ScopeContext，保证 temporal composability 精确到单组件。
/// </summary>
public sealed class Composition
{
    private readonly RootContext _root = new();
    private readonly Dictionary<string, (IComponent comp, ScopeContext scope)> _loaded = new();

    public IContext HostContext { get; }

    public Composition()
    {
        HostContext = new HostScope(_root);
    }

    internal RootContext Root => _root;
    internal object Lock => _root.Lock;
    internal IReadOnlyDictionary<string, ScopeContext> LoadedScopes => _loaded.ToDictionary(kv => kv.Key, kv => kv.Value.scope);

    public async Task LoadAsync(IComponent c)
    {
        lock (_root.Lock)
        {
            if (_loaded.ContainsKey(c.Name))
                throw new InvalidOperationException($"component '{c.Name}' already loaded");
        }
        var scope = new ScopeContext(_root, c.Name);
        await c.StartAsync(scope);
        lock (_root.Lock) _loaded[c.Name] = (c, scope);
    }

    public Task<bool> UnloadAsync(string name)
    {
        (IComponent, ScopeContext)? entry;
        lock (_root.Lock)
        {
            if (!_loaded.Remove(name, out var e)) return Task.FromResult(false);
            entry = e;
        }
        var (_, scope) = entry!.Value;
        scope.Dispose();   // LIFO 回滚该组件的一切效果；Provide 回滚触发孤儿事件
        return Task.FromResult(true);
    }

    public IReadOnlyList<string> Loaded
    {
        get { lock (_root.Lock) return _loaded.Keys.ToList(); }
    }

    public bool IsLoaded(string name) { lock (_root.Lock) return _loaded.ContainsKey(name); }

    /// <summary>宿主级服务注册（IAdminApi 等），随进程存活。</summary>
    public void ProvideHost<T>(T service) where T : class => _root.ProvideHost(service);

    private sealed class HostScope : IContext
    {
        private readonly RootContext _root;
        public HostScope(RootContext root) => _root = root;
        public void Track(Action apply, Action revert) => apply();  // 宿主效果不回滚
        public void Track(Action apply, Func<Action> revertFactory) => apply();
        public T? Resolve<T>() where T : class => ResolveAll<T>().FirstOrDefault();
        public IReadOnlyList<T> ResolveAll<T>() where T : class => _root.GetServices(typeof(T)).Cast<T>().ToList();
        public void Provide<T>(T instance) where T : class => _root.ProvideHost(instance);
        public void Require<T>(Action<T> onAdd, Action<T>? onRemove = null) where T : class
        {
            foreach (var s in _root.GetServices(typeof(T)).Cast<T>()) onAdd(s);
            _root.AddWatcher(typeof(T), "$host", o => onAdd((T)o), o => onRemove?.Invoke((T)o));
        }
    }
}
