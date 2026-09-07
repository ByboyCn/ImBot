namespace ImBot.UI;

/// <summary>TCYM.UI 不支持 flex:1，一律显式宽高 / calc()。</summary>
internal static class UiCss
{
    internal const string Dark = """
        .app-root { width: 100%; height: 100%; display: flex; }
        .sidebar { width: 220px; height: 100%; background: #001529; display: flex; flex-direction: column; padding: 8px; gap: 4px; }
        .sidebar-title { width: 100%; height: 40px; color: #ffffff; font-size: 20px; }
        .sidebar-sub { width: 100%; height: 18px; color: rgba(255,255,255,0.45); font-size: 12px; }
        .nav-item { width: 100%; height: 40px; color: rgba(255,255,255,0.65); padding-left: 12px; border-radius: 6px; cursor: pointer; }
        .nav-item:hover { background: rgba(255,255,255,0.08); }
        .nav-active { color: #ffffff; background: #1677ff; }
        .main { width: calc(100% - 220px); height: 100%; background: #f5f5f5; display: flex; flex-direction: column; padding: 16px; gap: 8px; }
        .page { width: 100%; height: calc(100% - 70px); display: flex; flex-direction: column; gap: 8px; }
        .page-title { width: 100%; height: 34px; font-size: 22px; color: rgba(0,0,0,0.88); }
        .page-subtitle { width: 100%; color: rgba(0,0,0,0.45); font-size: 13px; }
        .page-status { width: 100%; height: 20px; color: #1677ff; font-size: 13px; }
        .plugin-scroll { width: 100%; height: calc(100% - 140px); }
        .plugin-list { display: flex; flex-direction: column; gap: 8px; width: 100%; }
        .plugin-row { display: flex; align-items: center; gap: 12px; background: #ffffff; border-radius: 8px; padding: 8px 16px; width: 100%; }
        .plugin-info { display: flex; flex-direction: column; justify-content: center; }
        .plugin-name { font-size: 16px; color: rgba(0,0,0,0.88); }
        .plugin-desc { font-size: 12px; color: rgba(0,0,0,0.45); }
        .btn-primary { color: #ffffff; background: #1677ff; border-radius: 6px; padding: 6px 12px; cursor: pointer; }
        .btn-danger { color: #ffffff; background: #ff4d4f; border-radius: 6px; padding: 6px 12px; cursor: pointer; }
        .msg-row { display: flex; align-items: center; gap: 8px; }
        .msg-input { width: calc(100% - 104px); height: 34px; }
        .msg-send { width: 90px; height: 34px; }
        .msg-scroll { width: 100%; background: #ffffff; border-radius: 8px; }
        .msg-log { display: flex; flex-direction: column; gap: 4px; width: 100%; padding: 12px; }
        .msg-line { width: 100%; color: rgba(0,0,0,0.88); font-size: 13px; }
        """;
}
