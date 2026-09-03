using LogFilterView.Models;

namespace LogFilterView.Services;

/// <summary>名前付きフィルタ設定。</summary>
public sealed class FilterPreset
{
    public string Name { get; set; } = string.Empty;
    public string Include { get; set; } = string.Empty;
    public string Exclude { get; set; } = string.Empty;
    public MatchMode Mode { get; set; } = MatchMode.Plain;
    public bool CaseSensitive { get; set; }
    public LogicMode IncludeLogic { get; set; } = LogicMode.Or;
    public LogicMode ExcludeLogic { get; set; } = LogicMode.Or;

    /// <summary>「含む」を絞り込みには使わず、強調表示だけに使うか。</summary>
    public bool IncludeHighlightOnly { get; set; }

    public override string ToString() => Name;
}

/// <summary>%APPDATA%\LogFilterView\settings.json に保存される内容。</summary>
public sealed class AppSettings
{
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 820;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public bool WindowMaximized { get; set; }

    public double FilterPaneWidth { get; set; } = 300;
    public bool FilterPaneVisible { get; set; } = true;

    public string IncludeText { get; set; } = string.Empty;
    public string ExcludeText { get; set; } = string.Empty;
    public MatchMode Mode { get; set; } = MatchMode.Plain;
    public bool CaseSensitive { get; set; }
    public LogicMode IncludeLogic { get; set; } = LogicMode.Or;
    public LogicMode ExcludeLogic { get; set; } = LogicMode.Or;
    public bool AutoApply { get; set; } = true;
    public int ContextLines { get; set; }

    /// <summary>「含む」を絞り込みには使わず、強調表示だけに使うか（除外は効いたまま）。</summary>
    public bool IncludeHighlightOnly { get; set; }

    /// <summary>「含む」を行ごとのリストではなく素のテキストとして編集するか。</summary>
    public bool IncludeAsText { get; set; }

    public bool WordWrap { get; set; }
    public bool ShowLineNumbers { get; set; } = true;
    public bool HighlightMatches { get; set; } = true;
    public double FontSize { get; set; } = 13;
    public string FontFamily { get; set; } = "Consolas, MS Gothic";

    public string EncodingKey { get; set; } = string.Empty;

    public List<FilterPreset> Presets { get; set; } = new();
    public List<string> RecentFiles { get; set; } = new();
}
