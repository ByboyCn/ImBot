using System;
using System.Collections.Generic;
using TCYM.UI.Core;
using TCYM.UI.Elements;

namespace ImBot.UI;

/// <summary>TCYM.UI 窗口应用（子进程主线程运行；生命周期 = 进程，卸载即退出）。</summary>
internal sealed class UiApp
{
    private readonly IUiBackend _backend;

    internal UiApp(IUiBackend backend) => _backend = backend;

    /// <summary>初始化并运行主循环（阻塞，直到窗口关闭或宿主断开管道）。</summary>
    internal void Run()
    {
        UISystem.EnableFrameTimingLog = false;
        UISystem.EnableRenderTypeProfile = false;
        UISystem.EnableGpuInitLog = false;
        UISystem.Initialize("ImBot 控制台", 1024, 680, true, 60, resizable: true);

        UISystem.RegisterGlobalDefaultsCss("*{font-size:14px;color:rgba(0,0,0,0.88);}");
        UISystem.RegisterClassStylesFromCss(UiCss.Dark);

        var root = UISystem.Manager?.Root;
        if (root == null) return;
        root.Id = "root";
        root.AddChild(new UICaptionBar { CaptionTitle = "ImBot 控制台" });
        root.AddChild(new AppShell(_backend));

        try { UISystem.Run(); }
        finally { UISystem.Shutdown(); }
    }

    // ==================== 布局壳：侧边栏 + 内容区 ====================

    internal sealed class AppShell : UIView
    {
        private readonly PluginManagerPage _plugins;
        private readonly MessageTesterPage _tester;
        private readonly UILabel _navPlugins;
        private readonly UILabel _navTester;
        private readonly UIView _main;      // 内容区：当前页从树上挂/摘实现切页

        internal AppShell(IUiBackend backend)
        {
            Style = new DefaultUIStyle { Width = "100%", Height = "100%" };
            ClassName = new List<string> { "app-root" };

            _plugins = new PluginManagerPage(backend);
            _tester = new MessageTesterPage(backend);
            _navPlugins = NavItem("插件管理", active: true);
            _navTester = NavItem("消息测试", active: false);

            _main = new UIView
            {
                ClassName = new List<string> { "main" },
                Children = new() { _plugins },   // 初始只挂插件管理页
            };

            Children = new()
            {
                new UIView
                {
                    ClassName = new List<string> { "sidebar" },
                    Children = new()
                    {
                        new UILabel { Text = "ImBot", ClassName = new List<string> { "sidebar-title" } },
                        new UILabel { Text = "时空可组合机器人控制台", ClassName = new List<string> { "sidebar-sub" } },
                        _navPlugins,
                        _navTester,
                    },
                },
                _main,
            };

            _navPlugins.Events = new() { Click = _ => Switch(toPlugins: true) };
            _navTester.Events = new() { Click = _ => Switch(toPlugins: false) };

            // 宿主状态推送 -> 两个页面刷新（RemoteBackend 保证在 UI 线程回调）
            backend.StateChanged += () =>
            {
                if (_plugins.IsActive) _plugins.Rebuild();
                if (_tester.IsActive) _tester.RefreshLog();
            };
        }

        private static UILabel NavItem(string text, bool active) => new()
        {
            Text = text,
            ClassName = new List<string> { "nav-item", active ? "nav-active" : "" },
        };

        private void Switch(bool toPlugins)
        {
            // 真正的切页：把当前页从树上摘下，挂上目标页（尺寸置 0 在 TCYM.UI 中不生效会叠页）
            UIView current = toPlugins ? _tester : _plugins;
            UIView next = toPlugins ? _plugins : _tester;
            if (ReferenceEquals(current, next)) return;
            if (_main.Children.Contains(current)) _main.RemoveChild(current);
            if (!_main.Children.Contains(next)) _main.AddChild(next);
            _plugins.IsActive = toPlugins;
            _tester.IsActive = !toPlugins;

            _navPlugins.ClassName = new List<string> { "nav-item", toPlugins ? "nav-active" : "" };
            _navTester.ClassName = new List<string> { "nav-item", toPlugins ? "" : "nav-active" };
            if (toPlugins) _plugins.Rebuild();
            else _tester.RefreshLog();
        }
    }
}
