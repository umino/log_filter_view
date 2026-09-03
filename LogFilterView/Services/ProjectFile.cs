using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogFilterView.Models;

namespace LogFilterView.Services;

/// <summary>
/// 作業状態をまとめた「プロジェクト」。ログ本体は含めず、対象ファイルへの参照だけを持つ。
/// </summary>
public sealed class ProjectFile
{
    public const string Extension = ".lfvproj";
    public const string FilterText = "LogFilterView プロジェクト (*.lfvproj)|*.lfvproj|すべてのファイル (*.*)|*.*";

    public int Version { get; set; } = 1;

    /// <summary>保存時点での対象ログの絶対パス。</summary>
    public string LogFilePath { get; set; } = string.Empty;

    /// <summary>プロジェクトファイルから見た相対パス。フォルダごと移動された場合の手がかりにする。</summary>
    public string LogFileRelativePath { get; set; } = string.Empty;

    public string EncodingKey { get; set; } = string.Empty;

    public string IncludeText { get; set; } = string.Empty;
    public string ExcludeText { get; set; } = string.Empty;
    public MatchMode Mode { get; set; } = MatchMode.Plain;
    public bool CaseSensitive { get; set; }
    public LogicMode IncludeLogic { get; set; } = LogicMode.Or;
    public LogicMode ExcludeLogic { get; set; } = LogicMode.Or;

    /// <summary>「含む」を絞り込みには使わず、強調表示だけに使うか（除外は効いたまま）。</summary>
    public bool IncludeHighlightOnly { get; set; }

    public int ContextLines { get; set; }

    /// <summary>マーカーを付けた行番号（1 基点）。</summary>
    public List<int> Markers { get; set; } = new();

    /// <summary>
    /// <see cref="Markers"/> と同じ並びのマーカー色番号。
    /// 色を持たない旧形式や、数が合わない場合は既定色として読む。
    /// </summary>
    public List<int> MarkerColors { get; set; } = new();

    public bool WordWrap { get; set; }
    public bool ShowLineNumbers { get; set; } = true;
    public bool HighlightMatches { get; set; } = true;
    public double FontSize { get; set; } = 13;
    public string FontFamily { get; set; } = string.Empty;

    public string SearchText { get; set; } = string.Empty;

    /// <summary>保存時にカーソルがあった行番号（1 基点）。0 なら復元しない。</summary>
    public int CursorLineNumber { get; set; }

    public string SavedAt { get; set; } = string.Empty;
}

/// <summary>プロジェクトファイルが読めなかったことを表す。</summary>
public sealed class ProjectLoadException : Exception
{
    public ProjectLoadException(string message, Exception? inner = null) : base(message, inner) { }
}

public static class ProjectService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Save(string path, ProjectFile project)
    {
        project.SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // ログを別マシン・別フォルダへ持っていっても開けるよう、相対パスも残しておく
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory) && !string.IsNullOrEmpty(project.LogFilePath))
        {
            try
            {
                project.LogFileRelativePath = Path.GetRelativePath(directory, project.LogFilePath);
            }
            catch (ArgumentException)
            {
                project.LogFileRelativePath = string.Empty;   // ドライブが違う場合など
            }
        }

        var json = JsonSerializer.Serialize(project, Options);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    public static ProjectFile Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            throw new ProjectLoadException($"プロジェクトファイルを読めませんでした。\n{ex.Message}", ex);
        }

        ProjectFile? project;
        try
        {
            project = JsonSerializer.Deserialize<ProjectFile>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new ProjectLoadException($"プロジェクトファイルの形式が正しくありません。\n{ex.Message}", ex);
        }

        if (project is null) throw new ProjectLoadException("プロジェクトファイルが空です。");
        return project;
    }

    /// <summary>
    /// 記録された絶対パス → プロジェクトからの相対パス、の順に対象ログを探す。
    /// どちらでも見つからなければ空文字を返す（呼び出し側でエラーにする）。
    /// </summary>
    public static string ResolveLogPath(string projectPath, ProjectFile project)
    {
        if (!string.IsNullOrEmpty(project.LogFilePath) && File.Exists(project.LogFilePath))
        {
            return project.LogFilePath;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        if (!string.IsNullOrEmpty(directory) && !string.IsNullOrEmpty(project.LogFileRelativePath))
        {
            try
            {
                var candidate = Path.GetFullPath(Path.Combine(directory, project.LogFileRelativePath));
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // 不正なパスは無視する
            }
        }

        return string.Empty;
    }

    public static bool IsProjectPath(string path) =>
        path.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase);
}
