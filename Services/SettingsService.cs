using System.Text.Json;
using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services;

public class SettingsService
{
    private static readonly object SyncRoot = new();
    // 独立仓库使用自己的配置目录，不再与旧项目共享设置文件。
    private static readonly string DefaultSettingsPath = Path.Combine(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "SteamCN-GameLauncher"),
        "settings.json");
    private readonly string _settingsPath;

    public SettingsService() : this(GetDefaultSettingsPath()) { }

    private static string GetDefaultSettingsPath()
    {
#if DEBUG
        // UI 回归只读写独立测试配置；Release 不接受此环境覆盖。
        var testPath = Environment.GetEnvironmentVariable("STEAMCN_TEST_SETTINGS_PATH");
        if (!string.IsNullOrWhiteSpace(testPath) && Path.IsPathFullyQualified(testPath)) return testPath;
#endif
        return DefaultSettingsPath;
    }

    internal SettingsService(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = settingsPath;
    }

    public AppSettings Load()
    {
        lock (SyncRoot)
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }
    }

    public bool Save(AppSettings settings)
    {
        lock (SyncRoot)
        {
            return SaveCore(settings);
        }
    }

    /// <summary>在同一把锁内重新读取并更新设置，避免不同页面用旧快照相互覆盖。</summary>
    public bool Update(Action<AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (SyncRoot)
        {
            var settings = LoadCore();
            update(settings);
            return SaveCore(settings);
        }
    }

    private AppSettings LoadCore()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    private bool SaveCore(AppSettings settings)
    {
        try
        {
            var settingsDir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(settingsDir))
                Directory.CreateDirectory(settingsDir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
