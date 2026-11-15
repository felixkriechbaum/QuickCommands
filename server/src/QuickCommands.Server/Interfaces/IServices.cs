using QuickCommands.Server.Dtos.Requests;
using QuickCommands.Server.Dtos.Responses;

namespace QuickCommands.Server.Interfaces;

public interface IUserService
{
    Task<MeResponse> GetMeAsync(CancellationToken ct);
    Task<MeResponse> UpdateMeAsync(UpdateMeRequest request, CancellationToken ct);
    Task<IReadOnlyList<PackageSummaryResponse>> GetMyPackagesAsync(CancellationToken ct);

    /// <summary>Deletes all data of the current user (see DESIGN.md, "Account löschen").</summary>
    Task DeleteMeAsync(CancellationToken ct);
}

public interface ICloudSaveService
{
    Task<CloudSaveDocument> GetCurrentAsync(CancellationToken ct);

    /// <param name="expectedRevision">Revision from If-Match, or null for If-None-Match: * (first save).</param>
    Task<CloudSaveRevisionResponse> SaveAsync(string json, long? expectedRevision, CancellationToken ct);

    Task<IReadOnlyList<CloudSaveRevisionResponse>> GetRevisionsAsync(CancellationToken ct);
    Task<CloudSaveDocument> GetRevisionAsync(long revision, CancellationToken ct);
    Task DeleteAsync(CancellationToken ct);
}

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken ct);
}

public interface IStoreService
{
    Task<PagedResponse<PackageSummaryResponse>> SearchAsync(SearchPackagesQuery query, CancellationToken ct);
    Task<PackageDetailResponse> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<PackageVersionResponse>> GetVersionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<PackageDetailResponse> PublishAsync(PackageRequest request, CancellationToken ct);
    Task<PackageDetailResponse> UpdateAsync(Guid id, PackageRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task SetStarAsync(Guid id, bool starred, CancellationToken ct);
    Task RecordInstallAsync(Guid id, CancellationToken ct);
}

public interface IModerationService
{
    Task ReportAsync(Guid packageId, ReportPackageRequest request, CancellationToken ct);
    Task<PagedResponse<ReportResponse>> GetReportsAsync(ReportListQuery query, CancellationToken ct);
    Task RemovePackageAsync(Guid packageId, RemovePackageRequest request, CancellationToken ct);
    Task RestorePackageAsync(Guid packageId, CancellationToken ct);
    Task BanUserAsync(Guid userId, BanUserRequest request, CancellationToken ct);
    Task UnbanUserAsync(Guid userId, CancellationToken ct);
}
