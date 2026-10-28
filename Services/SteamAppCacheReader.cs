using System.Text;
using System.Text.Json;
using SteamCNGameLauncher.Models;

namespace SteamCNGameLauncher.Services;

/// <summary>Read-only reader for appinfo versions 39–41. Unknown formats fail closed.</summary>
public static class SteamAppCacheReader
{
    public static Task<SteamAppInfo> ReadAsync(string path, string appId, CancellationToken token = default)
        => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Read(stream, appId, token);
        }, token);

    public static SteamAppInfo Read(Stream stream, string appId, CancellationToken token = default)
    {
        if (!uint.TryParse(appId, out var wanted) || wanted == 0) throw new InvalidDataException("Invalid AppID");
        using var reader = new BinaryReader(stream, new UTF8Encoding(false, true), leaveOpen: true);
        var magic = reader.ReadUInt32();
        if (magic is not (0x07564427 or 0x07564428 or 0x07564429))
            throw new InvalidDataException("不支持的 Steam appinfo 缓存版本");
        if (reader.ReadUInt32() != 1) throw new InvalidDataException("非公开 Steam universe");
        string[]? strings = null;
        long recordsEnd = stream.Length;
        if (magic == 0x07564429)
        {
            recordsEnd = reader.ReadInt64();
            if (recordsEnd < 16 || recordsEnd > stream.Length - 4) throw new InvalidDataException("Invalid string table");
            stream.Position = recordsEnd;
            var count = reader.ReadUInt32();
            if (count > 1000000) throw new InvalidDataException("String table too large");
            strings = new string[count];
            for (var i = 0; i < count; i++) { token.ThrowIfCancellationRequested(); strings[i] = CString(reader); }
            stream.Position = 16;
        }
        while (stream.Position + 4 <= recordsEnd)
        {
            token.ThrowIfCancellationRequested();
            var id = reader.ReadUInt32();
            if (id == 0) break;
            var size = reader.ReadUInt32();
            var end = stream.Position + size;
            var headerSize = magic == 0x07564427 ? 40 : 60;
            if (size < headerSize || end > recordsEnd) throw new InvalidDataException("Invalid app record size");
            if (id != wanted) { stream.Position = end; continue; }
            stream.Position += headerSize;
            if (end - stream.Position > 16 * 1024 * 1024) throw new InvalidDataException("App record too large");
            using var payload = new MemoryStream(reader.ReadBytes(checked((int)(end - stream.Position))));
            using var data = new BinaryReader(payload, new UTF8Encoding(false, true));
            var root = ReadObject(data, strings, token, 0);
            if (payload.Position != payload.Length || !root.TryGetValue("appinfo", out var app)
                || app is not Dictionary<string, object> fields
                || !fields.TryGetValue("appid", out var embedded) || embedded.ToString() != appId)
                throw new InvalidDataException("Invalid appinfo payload");
            // Reuse the existing normalized parser; no mixing build/depot/launch metadata across sources.
            return SteamAppInfoParser.Parse(JsonSerializer.Serialize(new
            {
                status = "success", data = new Dictionary<string, object> { [appId] = app }
            }), appId);
        }
        throw new InvalidDataException("本机 Steam 缓存没有该游戏信息");
    }

    private static Dictionary<string, object> ReadObject(BinaryReader reader, string[]? strings,
        CancellationToken token, int depth)
    {
        if (depth > 64) throw new InvalidDataException("VDF nesting too deep");
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var type = reader.ReadByte();
            if (type == 8) return result;
            string key;
            if (strings == null) key = CString(reader);
            else
            {
                var index = reader.ReadUInt32();
                if (index >= strings.Length) throw new InvalidDataException("Invalid string index");
                key = strings[index];
            }
            object value = type switch
            {
                0 => ReadObject(reader, strings, token, depth + 1),
                1 => CString(reader),
                2 => reader.ReadInt32(),
                3 => reader.ReadSingle(),
                7 => reader.ReadUInt64(),
                10 => reader.ReadInt64(),
                _ => throw new InvalidDataException($"Unsupported VDF type {type}")
            };
            if (!result.TryAdd(key, value)) throw new InvalidDataException("Duplicate VDF key");
        }
    }

    private static string CString(BinaryReader reader)
    {
        using var bytes = new MemoryStream();
        for (var i = 0; i < 1024 * 1024; i++)
        {
            var b = reader.ReadByte();
            if (b == 0) return new UTF8Encoding(false, true).GetString(bytes.ToArray());
            bytes.WriteByte(b);
        }
        throw new InvalidDataException("VDF string too large");
    }
}
