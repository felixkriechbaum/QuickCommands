using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog.Events;
using Serilog.Parsing;

namespace QuickCommands.Server.Logging;

/// <summary>
/// Turns a Serilog event into one JSONL line of the loggerui format:
/// { type, timestamp, description, stacktrace?, origin, metadata }. The loggerui rejects any other top-level field,
/// nested metadata, more than 50 metadata keys and strings over 2000 characters – this class enforces all of that.
/// </summary>
internal static partial class LoggerUiFormatter
{
    public const int MaxMetadataKeys = 50;
    public const int MaxMetadataValueLength = 2000;

    /// <summary>Serilog / ASP.NET property names → the names used in the loggerui (see DESIGN.md).</summary>
    private static readonly Dictionary<string, string> Renames = new(StringComparer.Ordinal)
    {
        ["SourceContext"] = "source",
        ["RequestMethod"] = "method",
        ["RequestPath"] = "path",
        ["Elapsed"] = "elapsedMs",
    };

    /// <summary>Noise from ASP.NET logging scopes.</summary>
    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal) { "ConnectionId", "SpanId", "ParentId", "RequestId" };

    // Not embedded in HTML – keeps umlauts and quotes readable in the loggerui.
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [GeneratedRegex(@"^[\w.-]{1,64}$")]
    private static partial Regex ValidKey();

    public static string Format(LogEvent e, string origin)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("type", Level(e.Level));
            writer.WriteNumber("timestamp", e.Timestamp.ToUnixTimeMilliseconds());
            writer.WriteString("description", RenderMessage(e));
            if (e.Exception is not null)
                writer.WriteString("stacktrace", e.Exception.ToString());
            writer.WriteString("origin", origin);
            WriteMetadata(writer, e);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Like LogEvent.RenderMessage, but strings without quotes ("GET /api/health", not "\"GET\" \"/api/health\"").</summary>
    private static string RenderMessage(LogEvent e)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        foreach (var token in e.MessageTemplate.Tokens)
        {
            if (token is PropertyToken property && e.Properties.TryGetValue(property.PropertyName, out var value) &&
                value is ScalarValue { Value: string text })
                writer.Write(text);
            else
                token.Render(e.Properties, writer, CultureInfo.InvariantCulture);
        }
        return writer.ToString();
    }

    public static string Level(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose or LogEventLevel.Debug => "debug",
        LogEventLevel.Information => "info",
        LogEventLevel.Warning => "warn",
        _ => "error",
    };

    private static void WriteMetadata(Utf8JsonWriter writer, LogEvent e)
    {
        var written = new HashSet<string>(StringComparer.Ordinal);
        writer.WriteStartObject("metadata");

        if (e.TraceId is { } traceId && !e.Properties.ContainsKey("traceId"))
            Write(writer, written, "traceId", new ScalarValue(traceId.ToHexString()));

        foreach (var (name, value) in e.Properties)
        {
            if (written.Count >= MaxMetadataKeys)
                break;
            if (Skipped.Contains(name))
                continue;
            var key = Renames.TryGetValue(name, out var renamed) ? renamed : CamelCase(name);
            if (ValidKey().IsMatch(key))
                Write(writer, written, key, value);
        }
        writer.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter writer, HashSet<string> written, string key, LogEventPropertyValue value)
    {
        if (!written.Add(key))
            return;
        writer.WritePropertyName(key);
        switch (value)
        {
            case ScalarValue { Value: null }:
                writer.WriteNullValue();
                break;
            case ScalarValue { Value: bool b }:
                writer.WriteBooleanValue(b);
                break;
            case ScalarValue { Value: int or long or short or byte or uint or ushort or sbyte } s:
                writer.WriteNumberValue(Convert.ToInt64(s.Value, CultureInfo.InvariantCulture));
                break;
            case ScalarValue { Value: ulong u }:
                writer.WriteNumberValue(u);
                break;
            case ScalarValue { Value: double d } when double.IsFinite(d):
                writer.WriteNumberValue(Math.Round(d, 3));
                break;
            case ScalarValue { Value: float f } when float.IsFinite(f):
                writer.WriteNumberValue(Math.Round(f, 3));
                break;
            case ScalarValue { Value: decimal m }:
                writer.WriteNumberValue(m);
                break;
            case ScalarValue { Value: DateTime dt }:
                writer.WriteStringValue(dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                break;
            case ScalarValue { Value: DateTimeOffset dto }:
                writer.WriteStringValue(dto.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                break;
            case ScalarValue { Value: string s }:
                writer.WriteStringValue(Truncate(s));
                break;
            case ScalarValue s:
                writer.WriteStringValue(Truncate(Convert.ToString(s.Value, CultureInfo.InvariantCulture) ?? ""));
                break;
            default:
                // Sequences, structures and dictionaries – the loggerui only accepts flat values.
                writer.WriteStringValue(Truncate(value.ToString()));
                break;
        }
    }

    private static string CamelCase(string name) => name.Length == 0 || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static string Truncate(string s) => s.Length <= MaxMetadataValueLength ? s : s[..(MaxMetadataValueLength - 1)] + "…";
}
