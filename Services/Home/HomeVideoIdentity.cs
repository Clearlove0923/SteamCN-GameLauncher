using SteamCNGameLauncher.Models.Home;

namespace SteamCNGameLauncher.Services.Home;

/// <summary>为远程缓存视频和 Provider 直接返回的本地视频生成一致的播放身份。</summary>
public static class HomeVideoIdentity
{
    public static string Create(HomeBackground? background, Uri? resolvedUri = null)
    {
        if (!string.IsNullOrWhiteSpace(background?.VideoUrl))
            return background.VideoUrl;

        var localPath = background?.LocalPath;
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            try
            {
                if (Path.IsPathFullyQualified(localPath))
                    return new Uri(Path.GetFullPath(localPath)).AbsoluteUri;
                if (Uri.TryCreate(localPath, UriKind.Absolute, out var localUri) && localUri.IsFile)
                    return localUri.AbsoluteUri;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or UriFormatException)
            {
                // 无效本地路径交给调用方的媒体校验处理；这里不制造身份。
            }
        }

        return resolvedUri?.AbsoluteUri ?? string.Empty;
    }

    public static bool Matches(HomeBackground? background, string key) =>
        !string.IsNullOrWhiteSpace(key)
        && string.Equals(Create(background), key, StringComparison.OrdinalIgnoreCase);
}
