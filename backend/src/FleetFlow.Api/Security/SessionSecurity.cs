using System.Security.Claims;
using System.Threading.RateLimiting;
using FleetFlow.Application.Insights;
using FleetFlow.Infrastructure.Identity;
using FleetFlow.Infrastructure.Persistence;
using FleetFlow.Infrastructure.Portfolio;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace FleetFlow.Api.Security;

public static class SessionSecurity
{
    public const string InsightRead = "InsightsRead", AiGenerate = "AiGenerate";
    public const string Read = "LogisticsRead", Write = "LogisticsWrite", Manage = "ManageUsers";
    public static IServiceCollection AddSessionSecurity(this IServiceCollection services, bool development)
    {
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequiredUniqueChars = 6;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<IdentityPersistence>().AddDefaultTokenProviders();
        services.AddDataProtection().SetApplicationName("FleetFlow").PersistKeysToDbContext<IdentityPersistence>();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "FleetFlow.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = false;
            options.EventsType = typeof(SessionCookieEvents);
        });
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<TimeProvider>((options, clock) => options.TimeProvider = clock);
        services.AddScoped<SessionCookieEvents>();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "FleetFlow.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        services.AddScoped<CsrfFilter>();
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            options.AddPolicy(InsightRead, policy => policy.RequireRole(FleetRoles.All));
            options.AddPolicy(AiGenerate, policy => policy.RequireRole(FleetRoles.All).RequireAssertion(context =>
                context.Resource is HttpContext http && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
                && http.RequestServices.GetRequiredService<AiSettings>().AllowedUserIds.Contains(id)));
            options.AddPolicy(Read, policy => policy.RequireRole(FleetRoles.All));
            options.AddPolicy(Write, policy => policy.RequireRole(FleetRoles.Admin, FleetRoles.Dispatcher));
            options.AddPolicy(Manage, policy => policy.RequireRole(FleetRoles.Admin));
        });
        services.AddRateLimiter(options =>
        {
            options.AddPolicy("demo-entry", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = context.RequestServices.GetRequiredService<PortfolioSettings>().EntryPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await Problem(context.HttpContext, 429, context.HttpContext.Request.Path.StartsWithSegments("/api/v1/demo") ? "demo_entry_rate_limited" : "login_rate_limited", "Too many attempts. Try again later.").ExecuteAsync(context.HttpContext);
            };
        });
        return services;
    }

    public static IResult Problem(HttpContext context, int status, string code, string detail) => Results.Problem(
        statusCode: status, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code, ["traceId"] = context.TraceIdentifier });
}

public sealed class SessionCookieEvents(SignInManager<ApplicationUser> signIn, IdentityPersistence db, PortfolioScope scope, PortfolioSettings settings, TimeProvider clock) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        // Identity validates its own stamp. Check on every request for immediate revocation.
        var user = context.Principal is null ? null : await signIn.ValidateSecurityStampAsync(context.Principal);
        PortfolioSlot? slot = null;
        if (user is { IsDemo: true } && Guid.TryParse(context.Principal?.FindFirstValue("portfolio_lease"), out var lease))
            slot = await db.PortfolioSlots.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == user.Id && s.LeaseId == lease && s.Ready && s.ExpiresAtUtc > clock.GetUtcNow(), context.HttpContext.RequestAborted);
        if (user is null || !user.IsActive || (settings.Enabled && (!user.IsDemo || slot is null || context.Principal?.IsInRole(slot.Role) != true)) || (!settings.Enabled && user.IsDemo))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
        else if (slot is not null) scope.Bind(slot);
    }
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context) =>
        SessionSecurity.Problem(context.HttpContext, 401, "authentication_required", "Sign in to continue.").ExecuteAsync(context.HttpContext);
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context) =>
        SessionSecurity.Problem(context.HttpContext, 403, "forbidden", "Your role does not permit this operation.").ExecuteAsync(context.HttpContext);
}

public sealed class CsrfFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter, IOrderedFilter
{
    public int Order => 1000;
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (HttpMethods.IsGet(context.HttpContext.Request.Method) || HttpMethods.IsHead(context.HttpContext.Request.Method)
            || HttpMethods.IsOptions(context.HttpContext.Request.Method)) return;
        try { await antiforgery.ValidateRequestAsync(context.HttpContext); }
        catch (AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = 400,
                Title = "Request verification failed.",
                Detail = "Refresh the session and try again.",
                Extensions = { ["code"] = "csrf_invalid", ["traceId"] = context.HttpContext.TraceIdentifier }
            })
            { StatusCode = 400 };
        }
    }
}
