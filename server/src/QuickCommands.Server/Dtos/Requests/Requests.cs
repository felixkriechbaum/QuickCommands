using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QuickCommands.Server.Entities;

namespace QuickCommands.Server.Dtos.Requests;

public sealed record UpdateMeRequest(string Handle);

/// <summary>Publishing and editing use the same shape: an edit replaces everything.</summary>
public sealed record PackageRequest(
    string Title,
    string? Description,
    string Category,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<PackageCommandRequest> Commands);

/// <param name="Parameters">The desktop parameter list (JSON array). Stored as-is.</param>
public sealed record PackageCommandRequest(
    string Name,
    ShellLanguage Language,
    string? Description,
    string Script,
    JsonElement? Parameters);

public enum PackageSort
{
    Popular,
    Stars,
    Installs,
    New,
    Updated,
}

public sealed class SearchPackagesQuery
{
    /// <summary>Substring of title, description, tag or command name.</summary>
    [FromQuery(Name = "q")]
    public string? Q { get; init; }

    /// <summary>Category slug.</summary>
    [FromQuery(Name = "category")]
    public string? Category { get; init; }

    [FromQuery(Name = "tag")]
    public string? Tag { get; init; }
    [FromQuery(Name = "language")]
    public ShellLanguage? Language { get; init; }
    [FromQuery(Name = "sort")]
    public PackageSort Sort { get; init; } = PackageSort.Popular;
    [FromQuery(Name = "page")]
    public int Page { get; init; } = 1;
    [FromQuery(Name = "pageSize")]
    public int PageSize { get; init; } = 20;
}

public sealed record ReportPackageRequest(ReportReason Reason, string? Comment);

public sealed class ReportListQuery
{
    /// <summary>true = only unresolved reports (default), false = only resolved ones.</summary>
    [FromQuery(Name = "open")]
    public bool Open { get; init; } = true;

    [FromQuery(Name = "page")]
    public int Page { get; init; } = 1;
    [FromQuery(Name = "pageSize")]
    public int PageSize { get; init; } = 20;
}

public sealed record RemovePackageRequest(ReportReason Reason);

/// <param name="RemovePackagesWithReason">If set, all active or hidden packages of the user are removed with this reason.</param>
public sealed record BanUserRequest(string? Reason, ReportReason? RemovePackagesWithReason);
