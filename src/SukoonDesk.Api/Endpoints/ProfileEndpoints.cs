using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SukoonDesk.Api.Domain;
using SukoonDesk.Api.Infrastructure;

namespace SukoonDesk.Api.Endpoints;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profile").RequireAuthorization().WithTags("Profile");
        group.MapGet("/", GetAsync);
        group.MapPut("/", UpdateAsync);
        return app;
    }

    private static async Task<IResult> GetAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Results.Unauthorized();
        var profile = await db.Users.Where(x => x.Id == userId).Select(x => new { x.DisplayName, x.Email, x.PhoneNumber }).SingleOrDefaultAsync();
        return profile is null ? Results.NotFound() : Results.Ok(profile);
    }

    private static async Task<IResult> UpdateAsync(ProfileUpdateRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Results.Unauthorized();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return Results.NotFound();
        var name = request.DisplayName?.Trim() ?? string.Empty;
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (name.Length < 2) return Results.ValidationProblem(new Dictionary<string, string[]> { ["displayName"] = ["Enter your full name."] });
        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Enter a valid email address."] });
        var emailChanged = !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase);
        var passwordChanged = !string.IsNullOrWhiteSpace(request.NewPassword);
        if ((emailChanged || passwordChanged) && (string.IsNullOrWhiteSpace(request.CurrentPassword) || !await userManager.CheckPasswordAsync(user, request.CurrentPassword)))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["currentPassword"] = ["Enter your current password to change email or password."] });
        if (emailChanged)
        {
            var owner = await userManager.FindByEmailAsync(email);
            if (owner is not null && owner.Id != user.Id) return Results.Conflict(new { message = "That email address is already in use." });
        }
        if (passwordChanged)
        {
            var changed = await userManager.ChangePasswordAsync(user, request.CurrentPassword!, request.NewPassword!);
            if (!changed.Succeeded) return Results.ValidationProblem(changed.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray()));
            user.MustChangePassword = false;
        }
        user.DisplayName = name;
        user.Email = email;
        user.UserName = email;
        user.PhoneNumber = request.PhoneNumber?.Trim();
        user.EmailConfirmed = true;
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded) return Results.ValidationProblem(updated.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray()));
        return Results.NoContent();
    }
}

public sealed record ProfileUpdateRequest(string DisplayName, string Email, string? PhoneNumber, string? CurrentPassword, string? NewPassword);
