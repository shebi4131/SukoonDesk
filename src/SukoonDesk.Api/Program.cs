using System.Text.Json.Serialization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using SukoonDesk.Api.Domain;
using SukoonDesk.Api.Endpoints;
using SukoonDesk.Api.Infrastructure;
using SukoonDesk.Api.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));
    else
        options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite"));
});

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("SukoonDesk");

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "sukoondesk.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.IsEssential = true;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.Events.OnValidatePrincipal = async context =>
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated != true) return;

        var hasUser = Guid.TryParse(context.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out _);
        var hasTenant = Guid.TryParse(context.Principal.FindFirstValue("tenant_id"), out _);
        var hasMembership = Guid.TryParse(context.Principal.FindFirstValue("membership_id"), out _);
        if (hasUser && hasTenant && hasMembership)
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var tenantId = Guid.Parse(context.Principal.FindFirstValue("tenant_id")!);
            var membershipId = Guid.Parse(context.Principal.FindFirstValue("membership_id")!);
            if (await db.Tenants.AnyAsync(x => x.Id == tenantId && x.IsActive) && await db.TenantMemberships.AnyAsync(x => x.Id == membershipId && x.Status == MembershipStatus.Active)) return;
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    };
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization();
builder.Services.AddPermissionAuthorization();
builder.Services.AddScoped<SettingService>();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddHttpClient<IEmailSender, EmailSender>(client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
if ((builder.Configuration["Storage:Provider"] ?? "Local").Equals("S3", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IFileStorage, S3FileStorage>();
else
    builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddProblemDetails();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-SukoonDesk-CSRF";
    options.Cookie.Name = "sukoondesk.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddFixedWindowLimiter("authentication", limiter => { limiter.PermitLimit = 20; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; });
    options.AddFixedWindowLimiter("public", limiter => { limiter.PermitLimit = 100; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; });
});
builder.Services.AddOpenApi();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (app.Environment.IsDevelopment() && provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        await db.Database.EnsureCreatedAsync();
        await SchemaUpgrade.ApplyAsync(db);
    }
    else
    {
        await db.Database.MigrateAsync();
    }
    await SeedData.EnsureSeededAsync(db);
    await SeedData.EnsureTenantOperationalRolesAsync(db);
    await PlatformAdminSeeder.EnsureSeededAsync(scope.ServiceProvider, builder.Configuration, app.Environment);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseExceptionHandler();
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; font-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && HttpMethods.IsPost(context.Request.Method) ||
        context.Request.Path.StartsWithSegments("/api") && HttpMethods.IsPut(context.Request.Method) ||
        context.Request.Path.StartsWithSegments("/api") && HttpMethods.IsPatch(context.Request.Method) ||
        context.Request.Path.StartsWithSegments("/api") && HttpMethods.IsDelete(context.Request.Method))
    {
        var antiforgery = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
        try { await antiforgery.ValidateRequestAsync(context); }
        catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = "The security token is missing or expired. Refresh the page and try again." });
            return;
        }
    }
    await next();
});

app.MapGet("/api/security/csrf", (HttpContext context, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
    Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", service = "SukoonDesk.Api" }));
app.MapGet("/api/health/ready", async (AppDbContext db) => await db.Database.CanConnectAsync()
    ? Results.Ok(new { status = "ready" })
    : Results.Problem("Database is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable));
app.MapAuthEndpoints();
app.MapProfileEndpoints();
app.MapPlatformAdminEndpoints();
app.MapRbacEndpoints();
app.MapSettingEndpoints();
app.MapDashboardEndpoints();
app.MapComplaintEndpoints();
app.MapOperationsAdminEndpoints();
app.MapInsightEndpoints();
app.MapPublicEndpoints();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            context.Context.Response.Headers.CacheControl = "no-cache, no-store";
        else
            context.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
    }
});
if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html")))
    app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
