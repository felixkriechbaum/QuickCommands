using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Entities;

namespace QuickCommands.Server.Mapping;

internal static class OtherMapping
{
    public static MeResponse ToMeResponse(this User user, bool isAdmin) =>
        new(user.Id, user.Handle, isAdmin, user.IsBanned, user.CreatedAt);

    public static CategoryResponse ToResponse(this Category c) => new(c.Slug, c.NameDe, c.NameEn);

    public static CloudSaveRevisionResponse ToResponse(this CloudSaveRevision r) =>
        new(r.Revision, r.SchemaVersion, r.SizeBytes, r.CreatedAt);

    /// <summary>Needs Package (with Owner) and Reporter loaded.</summary>
    public static ReportResponse ToResponse(this StoreReport r) => new(
        r.Id,
        r.PackageId,
        r.Package!.Title,
        r.Package.Status,
        r.Package.OwnerId,
        r.Package.Owner?.Handle,
        r.ReporterId,
        r.Reporter?.Handle,
        r.Reason,
        r.Comment,
        r.CreatedAt,
        r.ResolvedAt,
        r.Resolution);
}
