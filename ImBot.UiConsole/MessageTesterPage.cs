using System;
using System.Collections.Generic;
using System.Linq;
using TCYM.UI.Core;
using TCYM.UI.Elements;
using TCYM.UI.Elements.Input;

namespace ImBot.UI;

/// <summary>消息测试页：输入/回车发送到宿主，日志来自宿主推送（含机器人回复）。</summary>
internal sealed class MessageTesterPage : UIView
{
    private readonly IUiBackend _backend;
    internal bool IsActive;
    private readonly UIInput _input;
    private readonly UIView _logBox;
    private readonly UIScrollView _scroll;

    internal MessageTesterPage(IUiBackend backend)
    {
        _backend = backend;
        Style = new DefaultUIStyle { Width = "100%", Height = "100%" };
        ClassName = new List<string> { "page" };

        _input = new UIInput
        {
            Placeholder = "输入消息后回车发送，如 /help 或 /echo 你好",
            ClassName = new List<string> { "msg-input" },
            Events = new()
            {
                KeyDown = e =>
                {
                    if (e.Type == TCYM.UI.Events.UIKeyboardEventType.KeyDown
                        && (e.KeyCode == 40 || e.Text is "\r" or "\n"))
                    {
                        e.Handled = true;
                        Send();
                    }
                },
            },
        };

        _logBox = new UIView
        {
            ClassName = new List<string> { "msg-log" },
            Style = new DefaultUIStyle { Width = "100%" },
        };
        _scroll = new UIScrollView
        {
            Style = new DefaultUIStyle { Width = "100%", Height = "calc(100% - 130px)" },
            ClassName = new List<string> { "msg-scroll" },
            Children = new() { _logBox },
        };

        Children = new()
        {
            new UILabel { Text = "消息测试", ClassName = new List<string> { "page-title" } },
            new UIView
            {
                Style = new DefaultUIStyle { Width = "100%", Height = "36px", Display = "flex" },
                ClassName = new List<string> { "msg-row" },
                Children = new()
                {
                    _input,
                    new UIButton
                    {
                        Text = "发送",
                        ClassName = new List<string> { "btn-primary", "msg-send" },
                        Style = new DefaultUIStyle { Width = "90px", Height = "34px" },
                        Events = new() { Click = _ => Send() },
                    },
                },
            },
            new UILabel { Text = "会话日志（宿主推送）", ClassName = new List<string> { "page-subtitle" } },
            _scroll,
        };
    }

    /// <summary>宿主状态推送时同步刷新日志（StateChanged 在 UI 线程回调）。</summary>
    internal void RefreshLog()
    {
        var lines = _backend.State.Log;
        if (lines.Count == _lastCount) return;
        _lastCount = lines.Count;

        _logBox.RemoveAllChildren();
        foreach (var line in lines.TakeLast(100))
        {
            _logBox.AddChild(new UILabel
            {
                Text = line,
                ClassName = new List<string> { "msg-line" },
                Wrap = true,
            });
        }
        _scroll.ScrollByY(100000);
    }
    private int _lastCount = -1;

    private void Send()
    {
        var text = _input.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _input.Text = "";
        _backend.Send(text);   // 日志经宿主回推显示
    }
}
