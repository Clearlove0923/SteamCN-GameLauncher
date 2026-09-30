using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services.Update;

/// <summary>
/// 为固定的 Kachina 0.5.1 无索引在线更新器制作一次性配置副本。
/// 只改官方 TLV 的 CONFIG，原生程序和主题保持原样；拒绝处理含 INDEX/META 的完整安装包。
/// </summary>
internal static class KachinaSessionPackage
{
    public const string UpdaterFileName = "SteamCN-GameLauncher.update.exe";

    public static async Task<string> CreateAsync(string template, LauncherUpdateInfo? update, string? installDirectory = null)
    {
        var bytes = await File.ReadAllBytesAsync(template).ConfigureAwait(false);
        var marker = Encoding.ASCII.GetBytes("!IN\0\0\a\0CONFIG");
        var offset = bytes.AsSpan().LastIndexOf(marker);
        if (offset < 0 || offset + marker.Length + 4 > bytes.Length)
            throw new InvalidDataException("更新器配置缺失，请重新安装应用。");
        var sizeOffset = offset + marker.Length;
        var oldSize = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(sizeOffset, 4)));
        var contentOffset = sizeOffset + 4;
        if (oldSize <= 0 || oldSize > bytes.Length - contentOffset)
            throw new InvalidDataException("更新器配置长度无效。");
        var config = JsonNode.Parse(bytes.AsSpan(contentOffset, oldSize))?.AsObject()
            ?? throw new InvalidDataException("更新器配置无效。");
        if (config["exeName"]?.GetValue<string>() != "SteamCN-GameLauncher.exe")
            throw new InvalidDataException("更新器与当前应用不匹配。");

        // 解析所有后续 TLV，确认没有需要重算偏移量的索引/元数据。
        for (var cursor = contentOffset + oldSize; cursor < bytes.Length;)
        {
            if (bytes.Length - cursor < 10 || !bytes.AsSpan(cursor, 4).SequenceEqual("!IN\0"u8))
                throw new InvalidDataException("更新器主题格式无效。");
            var nameSize = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(cursor + 4, 2));
            if (bytes.Length - cursor < 10 + nameSize)
                throw new InvalidDataException("更新器主题长度无效。");
            var name = Encoding.UTF8.GetString(bytes, cursor + 6, nameSize);
            if (name != "\0IMAGE") throw new InvalidDataException("需要无索引在线更新器，不能使用完整安装包作为模板。");
            var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(cursor + 6 + nameSize, 4)));
            if (size < 0 || size > bytes.Length - cursor - 10 - nameSize)
                throw new InvalidDataException("更新器主题数据不完整。");
            cursor += 10 + nameSize + size;
        }

        // 临时 EXE 不在安装目录；绝对默认路径让 Kachina 正确识别为现有安装，
        // 不修改 Inno Setup 的卸载注册表，也不把临时目录误当成安装目录。
        config["programFilesPath"] = Path.GetFullPath(installDirectory ?? AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        config["title"] = AppInfo.AppName;
        config["description"] = update is null
            ? "暂时无法读取版本说明。请选择来源后点击更新重试；关闭此窗口不会影响应用。"
            : $"当前版本 {AppInfo.Version}  →  发布版本 {update.Version}\n\n{update.ReleaseNotes}";
        if (update is not null)
        {
            // 将两个来源都固定到窗口展示的版本，避免检测和下载之间 latest 变化。
            foreach (var source in config["source"]!.AsArray())
            {
                var id = source!["id"]!.GetValue<string>();
                source["uri"] = UpdateSourcePolicy.GetPackageUrl(id, update.Version);
            }
        }
        var replacement = Encoding.UTF8.GetBytes(config.ToJsonString());
        var sessionDirectory = Path.Combine(Path.GetTempPath(), "SteamCN-GameLauncher-Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDirectory);
        var path = Path.Combine(sessionDirectory, UpdaterFileName);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await output.WriteAsync(bytes.AsMemory(0, sizeOffset)).ConfigureAwait(false);
        var sizeBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(sizeBytes, checked((uint)replacement.Length));
        await output.WriteAsync(sizeBytes).ConfigureAwait(false);
        await output.WriteAsync(replacement).ConfigureAwait(false);
        await output.WriteAsync(bytes.AsMemory(contentOffset + oldSize)).ConfigureAwait(false);
        return path;
    }
}
