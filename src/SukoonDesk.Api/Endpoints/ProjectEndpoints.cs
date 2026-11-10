using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SukoonDesk.Api.Domain;
using SukoonDesk.Api.Infrastructure;
using SukoonDesk.Api.Security;

namespace SukoonDesk.Api.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects").RequireAuthorization().WithTags("Projects");
        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.SettingsManage));
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.SettingsManage));
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.SettingsManage));
        return app;
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, AppDbContext db, bool includeInactive = false)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        var query = db.TenantProjects.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (!includeInactive) query = query.Where(x => x.IsActive);
        var rows = await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).Select(x => new
        {
            x.Id, x.Name, x.Code, x.Description, x.SortOrder, x.IsActive, x.CreatedAt,
            residentCount = db.PublicMembers.Count(member => member.ProjectId == x.Id)
        }).ToListAsync();
        return Results.Ok(rows);
    }

    private static async Task<IResult> CreateAsync(ProjectRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        var error = Validate(request);
        if (error is not null) return error;
        var name = request.Name.Trim();
        var code = NormalizedCode(request.Code, name);
        if (await db.TenantProjects.AnyAsync(x => x.TenantId == tenantId && (x.Name == name || x.Code == code)))
            return Results.Conflict(new { message = "A project with this name or code already exists." });
        var project = new TenantProject
        {
            TenantId = tenantId, Name = name, Code = code, Description = request.Description?.Trim() ?? string.Empty,
            SortOrder = request.SortOrder, IsActive = request.IsActive
        };
        db.TenantProjects.Add(project);
        await db.SaveChangesAsync();
        return Results.Created($"/api/projects/{project.Id}", new { project.Id });
    }

    private static async Task<IResult> UpdateAsync(Guid id, ProjectRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        var error = Validate(request);
        if (error is not null) return error;
        var project = await db.TenantProjects.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (project is null) return Results.NotFound();
        var name = request.Name.Trim();
        var code = NormalizedCode(request.Code, name);
        if (await db.TenantProjects.AnyAsync(x => x.TenantId == tenantId && x.Id != id && (x.Name == name || x.Code == code)))
            return Results.Conflict(new { message = "A project with this name or code already exists." });
        project.Name = name;
        project.Code = code;
        project.Description = request.Description?.Trim() ?? string.Empty;
        project.SortOrder = request.SortOrder;
        project.IsActive = request.IsActive;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        var project = await db.TenantProjects.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (project is null) return Results.NotFound();
        if (await db.PublicMembers.AnyAsync(x => x.ProjectId == id))
            return Results.BadRequest(new { message = "This project has residents. Deactivate it instead of deleting it." });
        db.TenantProjects.Remove(project);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static IResult? Validate(ProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Project name is required."] });
        if (request.Name.Trim().Length > 120)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Project name cannot exceed 120 characters."] });
        return null;
    }

    private static string NormalizedCode(string? code, string name)
    {
        var value = string.IsNullOrWhiteSpace(code) ? name : code;
        return new string(value.Where(char.IsLetterOrDigit).Take(30).ToArray()).ToUpperInvariant();
    }
}

public sealed record ProjectRequest(string Name, string? Code, string? Description, int SortOrder, bool IsActive);
