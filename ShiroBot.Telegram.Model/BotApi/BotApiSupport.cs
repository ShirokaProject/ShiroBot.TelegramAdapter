using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShiroBot.Telegram.Model.BotApi;

/// <summary>Base of every generated Bot API object. Fields this version does not model are kept as-is.</summary>
public abstract record BotApiObject
{
    /// <summary>Fields the generated model does not know yet; written back unchanged when serialized.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

/// <summary>Serializer settings shared by every Bot API request and response.</summary>
public static class BotApiJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>Bot API <c>Integer or String</c> chat identifier: a numeric ID or an <c>@username</c>.</summary>
[JsonConverter(typeof(ChatIdConverter))]
public readonly record struct ChatId
{
    private ChatId(long? id, string? username)
    {
        Id = id;
        Username = username;
    }

    /// <summary>Numeric chat ID, or null when the chat is addressed by username.</summary>
    public long? Id { get; }

    /// <summary>Public username in the form <c>@username</c>, or null for numeric IDs.</summary>
    public string? Username { get; }

    public static implicit operator ChatId(long id) => new(id, null);

    public static implicit operator ChatId(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        return long.TryParse(username, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id)
            ? new ChatId(id, null)
            : new ChatId(null, username);
    }

    public override string ToString() => Id?.ToString(CultureInfo.InvariantCulture) ?? Username ?? string.Empty;
}

internal sealed class ChatIdConverter : JsonConverter<ChatId>
{
    public override ChatId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Number ? reader.GetInt64() : (ChatId)reader.GetString()!;

    public override void Write(Utf8JsonWriter writer, ChatId value, JsonSerializerOptions options)
    {
        if (value.Id is { } id) writer.WriteNumberValue(id);
        else if (value.Username is { } username) writer.WriteStringValue(username);
        else throw new InvalidOperationException("ChatId has neither a numeric ID nor a username.");
    }
}

/// <summary>
///     Bot API <c>InputFile or String</c>: an existing <c>file_id</c>, an HTTP URL Telegram downloads itself,
///     or content uploaded with the request. Uploads work both as method parameters and inside
///     <c>InputMedia*</c>, <c>InputSticker</c>, <c>InputProfilePhoto*</c> and <c>InputStoryContent*</c> objects.
/// </summary>
[JsonConverter(typeof(InputFileConverter))]
public sealed class InputFile
{
    private readonly ReadOnlyMemory<byte>? _bytes;
    private readonly Stream? _stream;

    private InputFile(string? value, string? fileName, ReadOnlyMemory<byte>? bytes, Stream? stream)
    {
        Value = value;
        FileName = fileName;
        _bytes = bytes;
        _stream = stream;
    }

    /// <summary>The file_id or URL sent as text; null for uploads.</summary>
    public string? Value { get; }

    /// <summary>File name sent with an upload.</summary>
    public string? FileName { get; }

    public bool IsUpload => _bytes is not null || _stream is not null;

    public static InputFile FromFileId(string fileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        return new InputFile(fileId, null, null, null);
    }

    public static InputFile FromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
            throw new ArgumentException("Telegram only downloads files from HTTP(S) URLs.", nameof(url));
        return new InputFile(url, null, null, null);
    }

    public static InputFile FromBytes(ReadOnlyMemory<byte> content, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return new InputFile(null, fileName, content, null);
    }

    /// <summary>Uploads a stream. The stream is read once, when the request is sent, and is not disposed.</summary>
    public static InputFile FromStream(Stream content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return new InputFile(null, fileName, null, content);
    }

    /// <summary>Treats the text as a file_id or URL.</summary>
    public static implicit operator InputFile(string fileIdOrUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileIdOrUrl);
        return new InputFile(fileIdOrUrl, null, null, null);
    }

    /// <summary>Creates the multipart body part of an upload.</summary>
    public HttpContent CreateContent()
    {
        HttpContent content = _stream is not null
            ? new StreamContent(_stream)
            : new ReadOnlyMemoryContent(_bytes ?? throw new InvalidOperationException("This InputFile is not an upload."));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    public override string ToString() => Value ?? "upload:" + FileName;
}

internal sealed class InputFileConverter : JsonConverter<InputFile>
{
    public override InputFile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString()!;

    public override void Write(Utf8JsonWriter writer, InputFile value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.IsUpload ? BotApiUploads.Attach(value) : value.Value);
}

/// <summary>Collects files uploaded inside JSON-serialized parameters, which Telegram references as <c>attach://name</c>.</summary>
public static class BotApiUploads
{
    [ThreadStatic] private static List<KeyValuePair<string, InputFile>>? _current;

    /// <summary>Routes uploads serialized on this thread into <paramref name="target"/> until disposed.</summary>
    public static IDisposable Collect(List<KeyValuePair<string, InputFile>> target)
    {
        var previous = _current;
        _current = target;
        return new Restore(previous);
    }

    internal static string Attach(InputFile file)
    {
        var uploads = _current ?? throw new InvalidOperationException(
            "Uploaded InputFile values can only be serialized as part of a Telegram Bot API request.");
        var name = "shirobot_file" + uploads.Count.ToString(CultureInfo.InvariantCulture);
        uploads.Add(new KeyValuePair<string, InputFile>(name, file));
        return "attach://" + name;
    }

    private sealed class Restore(List<KeyValuePair<string, InputFile>>? previous) : IDisposable
    {
        public void Dispose() => _current = previous;
    }
}

/// <summary>
///     Reads a Bot API union by its discriminator field and writes the runtime type.
///     Values this version does not know become the union's <c>*Unknown</c> record with all fields kept.
/// </summary>
public sealed class BotApiUnionConverter<T> : JsonConverter<T> where T : class
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        var type = BotApiUnionRegistry.Resolve(typeof(T), element);
        return (T?)element.Deserialize(type, options);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
}

internal static partial class BotApiUnionRegistry
{
    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsZero(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.GetInt64() == 0;

    /// <summary>Picks the candidate whose required fields are all present, preferring the most specific one.</summary>
    private static Type? Pick(JsonElement element, params (Type Type, string[] Required)[] candidates)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        Type? best = null;
        var bestCount = -1;
        foreach (var (type, required) in candidates)
        {
            if (required.Length <= bestCount || !required.All(name => element.TryGetProperty(name, out _))) continue;
            best = type;
            bestCount = required.Length;
        }

        return best;
    }
}

/// <summary>Plain-text <see cref="RichText"/>, serialized as a JSON string.</summary>
[JsonConverter(typeof(RichTextPlainConverter))]
public sealed record RichTextPlain(string Text) : BotApiObject, RichText;

/// <summary>Concatenated <see cref="RichText"/> parts, serialized as a JSON array.</summary>
[JsonConverter(typeof(RichTextListConverter))]
public sealed record RichTextList(IReadOnlyList<RichText> Items) : BotApiObject, RichText;

internal sealed class RichTextPlainConverter : JsonConverter<RichTextPlain>
{
    public override RichTextPlain Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, RichTextPlain value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Text);
}

internal sealed class RichTextListConverter : JsonConverter<RichTextList>
{
    public override RichTextList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(JsonSerializer.Deserialize<IReadOnlyList<RichText>>(ref reader, options) ?? []);

    public override void Write(Utf8JsonWriter writer, RichTextList value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Items, options);
}
