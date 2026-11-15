using System.Text.Json;
using QuickCommands.Server.Entities;

namespace QuickCommands.Server.Dtos.Responses;

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record HealthResponse(string Status, string Version, string Database);

public sealed record MeResponse(Guid Id, string? Handle, bool IsAdmin, bool IsBanned, DateTime CreatedAt);

public sealed record CategoryResponse(string Slug, string NameDe, string NameEn);

public sealed record CloudSaveRevisionResponse(long Revision, int SchemaVersion, int SizeBytes, DateTime CreatedAt);

/// <summary>Internal: a stored document plus its metadata. The controller writes <see cref="Json"/> unchanged.</summary>
public sealed record CloudSaveDocument(long Revision, string Json, DateTime CreatedAt);

public sealed record PackageSummaryResponse(
    Guid Id,
    string Title,
    string Description,
    string? Category,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ShellLanguage> Languages,
    int CommandCount,
    string? Author,
    int Version,
    PackageStatus Status,
    int StarCount,
    int InstallCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <param name="StarredByMe">Only set for authenticated requests (like <paramref name="InstalledByMe"/>).</param>
public sealed record PackageDetailResponse(
    Guid Id,
    string Title,
    string Description,
    string? Category,
    IReadOnlyList<string> Tags,
    string? Author,
    int Version,
    PackageStatus Status,
    ReportReason? RemovalReason,
    int StarCount,
    int InstallCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<PackageCommandResponse> Commands,
    bool? StarredByMe,
    bool? InstalledByMe,
    bool IsOwner);

public sealed record PackageCommandResponse(
    string Name,
    ShellLanguage Language,
    string Description,
    string Script,
    JsonElement? Parameters);

public sealed record PackageVersionResponse(Guid Id, PackageStatus Status, int? Version, ReportReason? RemovalReason);

public sealed record ReportResponse(
    Guid Id,
    Guid PackageId,
    string PackageTitle,
    PackageStatus PackageStatus,
    Guid? AuthorId,
    string? Author,
    Guid ReporterId,
    string? Reporter,
    ReportReason Reason,
    string? Comment,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    ReportResolution? Resolution);
