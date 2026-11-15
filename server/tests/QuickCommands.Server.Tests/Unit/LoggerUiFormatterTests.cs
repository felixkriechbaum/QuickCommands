using System.Text.Json;
using QuickCommands.Server.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace QuickCommands.Server.Tests.Unit;

public sealed class LoggerUiFormatterTests
{
    private static readonly HashSet<string> AllowedTopLevel = ["type", "timestamp", "description", "stacktrace", "origin", "metadata"];

    private static JsonElement Format(LogEventLevel level, string template, Exception? exception, params (string Name, object? Value)[] properties)
    {
        var parsed = new MessageTemplateParser().Parse(template);
        var e = new LogEvent(
            new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero),
            level,
            exception,
            parsed,
            properties.Select(p => new LogEventProperty(p.Name, p.Value as LogEventPropertyValue ?? new ScalarValue(p.Value))));
        return JsonDocument.Parse(LoggerUiFormatter.Format(e, "host-1")).RootElement;
    }

    [Theory]
    [InlineData(LogEventLevel.Debug, "debug")]
    [InlineData(LogEventLevel.Information, "info")]
    [InlineData(LogEventLevel.Warning, "warn")]
    [InlineData(LogEventLevel.Error, "error")]
    [InlineData(LogEventLevel.Fatal, "error")]
    public void Levels_map_to_loggerui_types(LogEventLevel level, string type) =>
        Assert.Equal(type, Format(level, "x", null).GetProperty("type").GetString());

    [Fact]
    public void Writes_only_the_allowed_fields()
    {
        var line = Format(LogEventLevel.Error, "Package {PackageId} failed", new InvalidOperationException("boom"), ("PackageId", "p1"));
        Assert.All(line.EnumerateObject(), p => Assert.Contains(p.Name, AllowedTopLevel));
        Assert.Equal(1_794_823_200_000, line.GetProperty("timestamp").GetInt64());
        Assert.Equal("Package p1 failed", line.GetProperty("description").GetString());
        Assert.Contains("InvalidOperationException: boom", line.GetProperty("stacktrace").GetString());
        Assert.Equal("host-1", line.GetProperty("origin").GetString());
    }

    [Fact]
    public void Numbers_keep_their_format_and_text_stays_readable()
    {
        var e = Format(LogEventLevel.Information, "HTTP {RequestMethod} responded {StatusCode} in {Elapsed:0} ms: {Note}", null,
            ("RequestMethod", "GET"), ("StatusCode", 200), ("Elapsed", 12.7), ("Note", "Ümlaut \"quoted\""));
        Assert.Equal("HTTP GET responded 200 in 13 ms: Ümlaut \"quoted\"", e.GetProperty("description").GetString());
        Assert.Contains("Ümlaut", e.GetRawText());
    }

    [Fact]
    public void Metadata_is_flat_renamed_and_typed()
    {
        var metadata = Format(LogEventLevel.Information, "x", null,
            ("SourceContext", "QuickCommands.Server.Services.StoreService"),
            ("RequestPath", "/api/health"),
            ("StatusCode", 200),
            ("Elapsed", 12.34567),
            ("IsBanned", true),
            ("ConnectionId", "noise"),
            ("Tags", new SequenceValue([new ScalarValue("a"), new ScalarValue("b")])),
            ("Nothing", null)).GetProperty("metadata");

        Assert.Equal("QuickCommands.Server.Services.StoreService", metadata.GetProperty("source").GetString());
        Assert.Equal("/api/health", metadata.GetProperty("path").GetString());
        Assert.Equal(200, metadata.GetProperty("statusCode").GetInt32());
        Assert.Equal(12.346, metadata.GetProperty("elapsedMs").GetDouble());
        Assert.True(metadata.GetProperty("isBanned").GetBoolean());
        Assert.False(metadata.TryGetProperty("connectionId", out _));
        Assert.Equal(JsonValueKind.String, metadata.GetProperty("tags").ValueKind);
        Assert.Equal(JsonValueKind.Null, metadata.GetProperty("nothing").ValueKind);
    }

    [Fact]
    public void Metadata_respects_the_loggerui_limits()
    {
        var properties = Enumerable.Range(0, 80).Select(i => ($"P{i}", (object?)new string('x', 3000))).ToArray();
        var metadata = Format(LogEventLevel.Information, "x", null, properties).GetProperty("metadata");
        Assert.Equal(LoggerUiFormatter.MaxMetadataKeys, metadata.EnumerateObject().Count());
        Assert.All(metadata.EnumerateObject(), p => Assert.True(p.Value.GetString()!.Length <= LoggerUiFormatter.MaxMetadataValueLength));
    }
}
