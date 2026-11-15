using System.Linq.Expressions;
using System.Text.Json;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Entities;

namespace QuickCommands.Server.Mapping;

/// <summary>What a list query loads per package – no scripts, so search stays cheap.</summary>
internal sealed class PackageSummaryRow
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Category { get; init; }
    public List<string> Tags { get; init; } = [];
    public List<ShellLanguage> Languages { get; init; } = [];
    public string? Author { get; init; }
    public int Version { get; init; }
    public PackageStatus Status { get; init; }
    public int StarCount { get; init; }
    public int InstallCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

internal static class PackageMapping
{
    public static readonly Expression<Func<StorePackage, PackageSummaryRow>> ToSummaryRow = p => new PackageSummaryRow
    {
        Id = p.Id,
        Title = p.Title,
        Description = p.Description,
        Category = p.Category != null ? p.Category.Slug : null,
        Tags = p.Tags.OrderBy(t => t.Tag).Select(t => t.Tag).ToList(),
        Languages = p.Commands.Select(c => c.Language).ToList(),
        Author = p.Owner != null ? p.Owner.Handle : null,
        Version = p.Version,
        Status = p.Status,
        StarCount = p.StarCount,
        InstallCount = p.InstallCount,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
    };

    public static PackageSummaryResponse ToResponse(this PackageSummaryRow row) => new(
        row.Id,
        row.Title,
        row.Description,
        row.Category,
        row.Tags,
        row.Languages.Distinct().Order().ToList(),
        row.Languages.Count,
        row.Author,
        row.Version,
        row.Status,
        row.StarCount,
        row.InstallCount,
        row.CreatedAt,
        row.UpdatedAt);

    /// <summary>Needs Category, Owner, Tags and Commands loaded.</summary>
    public static PackageDetailResponse ToDetailResponse(this StorePackage p, bool? starredByMe, bool? installedByMe, bool isOwner) => new(
        p.Id,
        p.Title,
        p.Description,
        p.Category?.Slug,
        p.Tags.Select(t => t.Tag).Order(StringComparer.Ordinal).ToList(),
        p.Owner?.Handle,
        p.Version,
        p.Status,
        p.RemovalReason,
        p.StarCount,
        p.InstallCount,
        p.CreatedAt,
        p.UpdatedAt,
        p.Commands.OrderBy(c => c.SortOrder).Select(ToResponse).ToList(),
        starredByMe,
        installedByMe,
        isOwner);

    public static PackageCommandResponse ToResponse(this StoreCommand c) => new(
        c.Name,
        c.Language,
        c.Description,
        c.Script,
        c.ParametersJson is null ? null : JsonDocument.Parse(c.ParametersJson).RootElement.Clone());

    public static PackageVersionResponse ToVersionResponse(this StorePackage p) => new(
        p.Id,
        p.Status,
        p.Status == PackageStatus.Active ? p.Version : null,
        p.Status == PackageStatus.Removed ? p.RemovalReason : null);
}
