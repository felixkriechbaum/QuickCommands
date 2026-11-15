using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickCommands.Server.Dtos.Responses;
using QuickCommands.Server.Interfaces;

namespace QuickCommands.Server.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/categories")]
public sealed class CategoriesController(ICategoryService categories) : ControllerBase
{
    /// <summary>The fixed list of store categories.</summary>
    [HttpGet]
    public Task<IReadOnlyList<CategoryResponse>> GetAll(CancellationToken ct) => categories.GetAllAsync(ct);
}
