using System;
using System.Collections.Generic;
using System.Linq;
using TCYM.UI.Core;
using TCYM.UI.Elements;
using TCYM.UI.Elements.Message;

namespace ImBot.UI;

/// <summary>插件管理页：状态来自宿主推送（IUiBackend），操作发回宿主执行。</summary>
internal sealed class PluginManagerPage : UIView
{
    private readonly IUiBackend _backend;
    internal bool IsActive = true;
    private readonly UILabel _statusLabel;
    private readonly UIView _listBox;

    internal PluginManagerPage(IUiBackend backend)
    {
        _backend = backend;
        Style = new DefaultUIStyle { Width = "100%", Height = "100%" };
        ClassName = new List<string> { "page" };

        _statusLabel = new UILabel { ClassName = new List<string> { "page-status" } };
        _listBox = new UIView { ClassName = new List<string> { "plugin-list" } };

        Children = new()
        {
            new UILabel { Text = "插件管理", ClassName = new List<string> { "page-title" } },
            new UILabel
            {
                Text = "加载与卸载均为运行时操作：卸载会按 LIFO 回滚该插件的全部效果（时间可组合性），依赖它的插件自动停用（空间可组合性）。卸载 ui-console 会关闭本窗口。",
                ClassName = new List<string> { "page-subtitle" },
            },
            _statusLabel,
            new UIScrollView
            {
                Style = new DefaultUIStyle { Width = "100%", Height = "calc(100% - 140px)" },
                ClassName = new List<string> { "plugin-scroll" },
                Children = new() { _listBox },
            },
        };
        Rebuild();
    }

    internal void Rebuild()
    {
        var state = _backend.State;
        var loaded = state.Loaded.ToHashSet();
        var rows = new List<UIElement>();

        foreach (var name in state.Catalog)
        {
            var isLoaded = loaded.Contains(name);
            state.Descriptions.TryGetValue(name, out var desc);
            var captured = name;
            var capturedLoaded = isLoaded;

            rows.Add(new UIView
            {
                ClassName = new List<string> { "plugin-row" },
                Style = new DefaultUIStyle { Width = "100%", Height = "64px", Display = "flex" },
                Children = new()
                {
                    new UIView
                    {
                        ClassName = new List<string> { "plugin-info" },
                        Style = new DefaultUIStyle { Width = "calc(100% - 270px)", Height = "100%" },
                        Children = new()
                        {
                            new UILabel { Text = name + (name == "ui-console" ? "（本窗口）" : ""), ClassName = new List<string> { "plugin-name" }, Style = new DefaultUIStyle { Width = "100%", Height = "24px" } },
                            new UILabel { Text = desc ?? "", ClassName = new List<string> { "plugin-desc" }, Style = new DefaultUIStyle { Width = "100%", Height = "20px" } },
                        },
                    },
                    new UIView
                    {
                        Style = new DefaultUIStyle { Width = "80px", Height = "100%" },
                        Children = new()
                        {
                            new UITag
                            {
                                Text = isLoaded ? "已加载" : "未加载",
                                ClassColor = isLoaded ? TagClassColor.Green : TagClassColor.Red,
                            },
                        },
                    },
                    CreateToggleBtn(captured, capturedLoaded),
                    new UIButton
                    {
                        Text = "删除",
                        Style = new DefaultUIStyle { Width = "66px", Height = "32px" },
                        Events = new() { Click = _ => _backend.Delete(captured) },
                    },
                },
            });
        }

        _listBox.Children = rows;
        _statusLabel.Text = $"共 {state.Catalog.Count} 个插件，已加载 {state.Loaded.Count} 个";
    }

    /// <summary>创建操作按钮：点击即置为“处理中…”并禁用，宿主回推状态后 Rebuild 出正确按钮。</summary>
    private UIButton CreateToggleBtn(string name, bool loaded)
    {
        var btn = new UIButton
        {
            Text = loaded ? "卸载" : "加载",
            ClassName = new List<string> { loaded ? "btn-danger" : "btn-primary" },
            Style = new DefaultUIStyle { Width = "90px", Height = "32px" },
        };
        btn.Events = new()
        {
            Click = _ =>
            {
                if (btn.Disabled) return;
                btn.Disabled = true;
                btn.Text = "处理中…";
                Toggle(name, loaded);
            },
        };
        return btn;
    }

    private void Toggle(string name, bool loaded)
    {
        if (name == "ui-console" && loaded)
        {
            UIMessage.Info("即将卸载本窗口（宿主进程继续运行）");
        }
        if (loaded) _backend.Unload(name);
        else _backend.Load(name);
        // 状态回推（≤300ms）触发 Rebuild：按钮按宿主最新状态重建为 卸载/加载
    }
}
