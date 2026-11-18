using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ShiroBot.TelegramAdapter;

internal sealed class BoundedCache<T>(int capacity = 2048)
{
    private readonly ConcurrentDictionary<string, T> _values = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _keys = new();
    public void Set(string key, T value)
    {
        if (_values.TryAdd(key, value)) _keys.Enqueue(key);
        else _values[key] = value;
        while (_values.Count > capacity && _keys.TryDequeue(out var oldest)) _values.TryRemove(oldest, out _);
    }
    public bool TryGet(string key, out T value) => _values.TryGetValue(key, out value!);
    public void Clear() { _values.Clear(); _keys.Clear(); }
}

internal static class ButtonCodec
{
    public static string Encode(string id, string data) => "sb:" + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new[] { id, data }))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static (string Id, string Data) Decode(string value)
    {
        if (value.StartsWith("sb:", StringComparison.Ordinal))
        {
            try
            {
                var text = value[3..].Replace('-', '+').Replace('_', '/');
                text = text.PadRight((text.Length + 3) / 4 * 4, '=');
                var pair = JsonSerializer.Deserialize<string[]>(Convert.FromBase64String(text));
                if (pair is { Length: 2 } && pair.All(x => x is not null)) return (pair[0], pair[1]);
            }
            catch (Exception ex) when (ex is FormatException or JsonException) { }
        }
        return (value, value);
    }
}

/// <summary>读取到的资源：小文件在内存中，大文件留在磁盘上并在上传时流式读取。</summary>
internal sealed class ResourceBytes
{
    private readonly byte[]? _bytes;
    private readonly string? _path;
    private readonly bool _temporary;

    public ResourceBytes(byte[] bytes, string fileName)
    {
        _bytes = bytes;
        FileName = fileName;
        Length = bytes.LongLength;
    }

    private ResourceBytes(string path, string fileName, long length, bool temporary)
    {
        _path = path;
        FileName = fileName;
        Length = length;
        _temporary = temporary;
    }

    public static ResourceBytes FromFile(string path, string fileName, long length, bool temporary = false) =>
        new(path, fileName, length, temporary);

    public string FileName { get; }
    public long Length { get; }

    /// <summary>打开内容；临时文件在流关闭时删除。</summary>
    public Stream OpenRead() => _bytes is not null
        ? new MemoryStream(_bytes, writable: false)
        : new FileStream(_path!, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan | (_temporary ? FileOptions.DeleteOnClose : FileOptions.None));

    public HttpContent CreateContent() => new StreamContent(OpenRead(), 81920);
}

internal static class ResourceReader
{
    /// <summary>Telegram 官方 Bot API 服务器的上传上限，也是内存中保留资源的上限。</summary>
    public const long OfficialMaxBytes = 50L * 1024 * 1024;

    /// <summary>自建 Bot API Server（--local 模式）的上传上限。</summary>
    public const long LocalServerMaxBytes = 2000L * 1024 * 1024;

    private static readonly HttpClient Downloads = new() { Timeout = TimeSpan.FromMinutes(2) };

    public static void Validate(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        if (Path.IsPathFullyQualified(uri) || uri.StartsWith("data:", StringComparison.Ordinal)
            || uri.StartsWith("base64://", StringComparison.Ordinal)) return;
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https" or "file") return;
        throw new NotSupportedException("资源必须是 HTTP(S)、本地绝对路径、file URI、data URI 或 base64:// 数据。");
    }

    /// <param name="client">提供下载代理和上传上限（官方服务器 50 MB，自建服务器 2000 MB）。</param>
    public static async Task<ResourceBytes> ReadAsync(string uri, string? fileName, CancellationToken token, TelegramClient? client = null)
    {
        token.ThrowIfCancellationRequested();
        Validate(uri);
        var maxBytes = client?.MaxUploadBytes ?? OfficialMaxBytes;
        if (uri.StartsWith("data:", StringComparison.Ordinal) || uri.StartsWith("base64://", StringComparison.Ordinal))
        {
            var encoded = uri.StartsWith("base64://", StringComparison.Ordinal) ? uri[9..] : DataBody(uri);
            var size = encoded.Length / 4L * 3;
            if (size > Math.Min(maxBytes, OfficialMaxBytes)) throw TooLarge(size, Math.Min(maxBytes, OfficialMaxBytes));
            return new(Convert.FromBase64String(encoded), SafeName(fileName ?? "upload.bin"));
        }
        if (Uri.TryCreate(uri, UriKind.Absolute, out var remote) && remote.Scheme is "http" or "https")
        {
            using var response = await (client?.Downloads ?? Downloads).GetAsync(remote, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } announced && announced > maxBytes) throw TooLarge(announced, maxBytes);
            await using var input = await response.Content.ReadAsStreamAsync(token);
            return await ReadLimitedAsync(input, SafeName(fileName ?? Path.GetFileName(remote.LocalPath)), maxBytes, token);
        }
        var path = uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(uri).LocalPath : uri;
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("资源文件不存在。", path);
        if (info.Length > maxBytes) throw TooLarge(info.Length, maxBytes);
        var name = SafeName(fileName ?? info.Name);
        // 大文件直接从磁盘上传，不整个读入内存。
        if (info.Length > OfficialMaxBytes) return ResourceBytes.FromFile(info.FullName, name, info.Length);
        return new(await File.ReadAllBytesAsync(info.FullName, token), name);
    }

    private static NotSupportedException TooLarge(long size, long maxBytes) => new(maxBytes <= OfficialMaxBytes
        ? $"文件 {Megabytes(size)} 超过 Telegram 官方 Bot API 的 50 MB 上传上限。发送更大的文件需要自建 Telegram Bot API Server" +
          "（--local 模式，上限 2000 MB），并在适配器配置中填写 API Base URL。"
        : $"文件 {Megabytes(size)} 超过 Telegram Bot API Server 的 {Megabytes(maxBytes)} 上传上限。");

    private static string Megabytes(long bytes) => (bytes / 1024d / 1024d).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " MB";

    private static string DataBody(string uri)
    {
        var comma = uri.IndexOf(',');
        if (comma < 0 || !uri[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("data URI 必须使用 base64 编码。");
        return uri[(comma + 1)..];
    }
    private static string SafeName(string name)
    {
        name = Path.GetFileName(name.Replace('\\', '/')).Replace("\r", "").Replace("\n", "");
        return string.IsNullOrWhiteSpace(name) ? "upload.bin" : name;
    }
    /// <summary>读取远程内容：上限内放在内存，超过 50 MB 时写入临时文件（上传后删除）。</summary>
    private static async Task<ResourceBytes> ReadLimitedAsync(Stream input, string fileName, long maxBytes, CancellationToken token)
    {
        var memory = new MemoryStream();
        FileStream? spill = null;
        string? spillPath = null;
        try
        {
            var buffer = new byte[81920];
            long total = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, token)) != 0)
            {
                total += count;
                if (total > maxBytes) throw TooLarge(total, maxBytes);
                if (spill is null && total > OfficialMaxBytes)
                {
                    spillPath = Path.Combine(Path.GetTempPath(), "shirobot-telegram-" + Guid.NewGuid().ToString("N"));
                    spill = new FileStream(spillPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                    memory.Position = 0;
                    await memory.CopyToAsync(spill, token);
                    memory.SetLength(0);
                }
                if (spill is not null) await spill.WriteAsync(buffer.AsMemory(0, count), token);
                else await memory.WriteAsync(buffer.AsMemory(0, count), token);
            }

            if (spill is null) return new ResourceBytes(memory.ToArray(), fileName);
            await spill.DisposeAsync();
            spill = null;
            var result = ResourceBytes.FromFile(spillPath!, fileName, total, temporary: true);
            spillPath = null;
            return result;
        }
        finally
        {
            if (spill is not null) await spill.DisposeAsync();
            if (spillPath is not null) File.Delete(spillPath);
        }
    }
}
