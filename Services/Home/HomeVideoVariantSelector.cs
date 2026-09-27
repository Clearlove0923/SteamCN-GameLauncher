using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>每次启动在同一游戏的已验证视频候选间轮换，本次运行保持选择稳定。</summary>
public sealed class HomeVideoVariantSelector
{
    private readonly string _cacheRoot;
    private readonly Dictionary<string, string> _selectedIds = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public HomeVideoVariantSelector(string cacheRoot) => _cacheRoot = cacheRoot;

    public HomeContent Select(HomeContentRequest request, HomeContent content)
    {
        var background = content.Background;
        if (background is null || background.Variants.Count == 0) return content;

        var variants = background.Variants;
        HomeVideoVariant selected;
        lock (_sync)
        {
            // 本次运行曾选择过的动画不再变化，避免首页刷新或切换页面时突然重播。
            if (_selectedIds.TryGetValue(request.GameId, out var currentId)
                && variants.FirstOrDefault(item => item.Id == currentId) is { } current)
            {
                selected = current;
            }
            else
            {
                var statePath = Path.Combine(_cacheRoot, HomeCacheKey.FolderSegment(request), "last-home-video.txt");
                string? previousId = null;
                try { if (File.Exists(statePath)) previousId = File.ReadAllText(statePath).Trim(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogService.Instance.AddLog($"[首页背景] 读取上次动画失败：{ex.Message}");
                }

                // 仅记录候选 ID；下次启动按上游候选顺序选择下一段动画。
                var previousIndex = Array.FindIndex(variants.ToArray(), item => item.Id == previousId);
                selected = variants[(previousIndex + 1) % variants.Count];
                _selectedIds[request.GameId] = selected.Id;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
                    File.WriteAllText(statePath, selected.Id);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogService.Instance.AddLog($"[首页背景] 保存动画轮换状态失败：{ex.Message}");
                }
            }
        }

        return content with
        {
            Background = background with
            {
                VideoUrl = selected.VideoUrl,
                ImageUrl = selected.ImageUrl,
                LocalPath = selected.LocalPath,
            },
        };
    }
}
