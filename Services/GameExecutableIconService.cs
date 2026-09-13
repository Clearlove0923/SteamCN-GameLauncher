using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace SteamCNGameLauncher.Services;

/// <summary>从真实游戏可执行文件读取 Windows Shell 图标，供游戏切换列表显示。</summary>
public sealed class GameExecutableIconService
{
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ImageSource?> LoadAsync(string? executablePath, int size = 64)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) return null;
        var fullPath = Path.GetFullPath(executablePath);
        if (_cache.TryGetValue(fullPath, out var cached)) return cached;

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(fullPath);
            using var thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.SingleItem,
                (uint)Math.Clamp(size, 16, 256),
                ThumbnailOptions.UseCurrentScale);
            if (thumbnail == null || thumbnail.Size == 0) return null;

            var image = new BitmapImage();
            await image.SetSourceAsync(thumbnail);
            _cache[fullPath] = image;
            return image;
        }
        catch (Exception ex)
        {
            LogService.Instance.AddLog($"读取游戏图标失败：{ex.Message}");
            return null;
        }
    }
}
