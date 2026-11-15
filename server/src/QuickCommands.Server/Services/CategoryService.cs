using Microsoft.EntityFrameworkCore;
using QuickCommands.Server.Data;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Interfaces;
using QuickCommands.Server.Mapping;

namespace QuickCommands.Server.Services;

public sealed class CategoryService(AppDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(ct);
        return categories.Select(c => c.ToResponse()).ToList();
    }
}
