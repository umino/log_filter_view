using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sudare.Services;

/// <summary>設定の読み書き。失敗しても致命的ではないので握りつぶす。</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // ウィンドウ位置は「未設定」を NaN で表している。既定の設定では NaN を書こうとすると
        // 例外になり、Save が丸ごと失敗して設定が一切保存されなくなるため、明示的に許可する。
        // 読み込み側も同じ設定なので、数値のまま書かれた既存のファイルもそのまま読める。
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>改名前の保存先。読み込みだけ見に行く。</summary>
    private const string LegacyFolderName = "LogFilterView";

    public string FilePath { get; }

    /// <summary>改名前の設定ファイル。新しい保存先がまだ無いときだけ読む。</summary>
    public string LegacyFilePath { get; }

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        FilePath = Path.Combine(appData, "Sudare", "settings.json");
        LegacyFilePath = Path.Combine(appData, LegacyFolderName, "settings.json");
    }

    /// <summary>
    /// 設定を読む。
    /// </summary>
    /// <remarks>
    /// 新しい保存先がまだ無ければ、改名前の <c>%APPDATA%\LogFilterView</c> から引き継ぐ。
    /// プリセットや最近使ったファイルが改名で消えてしまわないようにするためで、
    /// 書き戻しは常に新しい保存先へ行う（古い側はそのまま残す）。
    /// </remarks>
    public AppSettings Load()
    {
        return Read(FilePath) ?? Read(LegacyFilePath) ?? new AppSettings();

        static AppSettings? Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
            }
            catch
            {
                return null;
            }
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // 保存できなくても動作は続行する
        }
    }
}
