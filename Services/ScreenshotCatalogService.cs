namespace SteamCNGameLauncher.Services;

public sealed record ScreenshotFile(
    string Path,
    string FileName,
    DateTimeOffset ModifiedAt,
    long Size);

/// <summary>读取用户选择目录中的截图文件，不承担 UI 或路径持久化。</summary>
public sealed class ScreenshotCatalogService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp",
    };

    public Task<IReadOnlyList<ScreenshotFile>> GetAsync(
        string directoryPath,
        bool newestFirst,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Path.IsPathFullyQualified(directoryPath))
            return Task.FromResult<IReadOnlyList<ScreenshotFile>>([]);

        return Task.Run<IReadOnlyList<ScreenshotFile>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(directoryPath)) return [];

            var files = new List<ScreenshotFile>();
            foreach (var path in Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SupportedExtensions.Contains(Path.GetExtension(path))) continue;
                try
                {
                    var info = new FileInfo(path);
                    files.Add(new ScreenshotFile(
                        info.FullName,
                        info.Name,
                        new DateTimeOffset(info.LastWriteTime),
                        info.Length));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    // 单个文件不可读时保留其余可用截图。
                }
            }

            return newestFirst
                ? files.OrderByDescending(item => item.ModifiedAt).ThenBy(item => item.FileName).ToArray()
                : files.OrderBy(item => item.ModifiedAt).ThenBy(item => item.FileName).ToArray();
        }, cancellationToken);
    }
}
