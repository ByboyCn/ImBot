using System;
using System.Collections.Generic;

namespace ImBot.UI;

/// <summary>UI 与宿主之间的共享状态快照。</summary>
public sealed record UiState(
    IReadOnlyList<string> Loaded,
    IReadOnlyList<string> Catalog,
    IReadOnlyDictionary<string, string> Descriptions,
    IReadOnlyList<string> Log)
{
    public static UiState Empty { get; } = new([], [], new Dictionary<string, string>(), []);
}

/// <summary>UI 侧后端接口：宿主可能在本进程（未用）或另一个进程（当前实现）。</summary>
public interface IUiBackend
{
    UiState State { get; }
    /// <summary>状态变化通知（保证在 UI 线程回调）。</summary>
    event Action? StateChanged;
    void Load(string name);
    void Unload(string name);
    void Delete(string name);
    void Send(string text);
}
