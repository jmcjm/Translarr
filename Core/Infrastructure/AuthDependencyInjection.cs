using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Translarr.Core.Application.Constants;
using Translarr.Core.Infrastructure.Persistence;

namespace Translarr.Core.Infrastructure;

public static class AuthDependencyInjection
{
    public static IServiceCollection AddTranslarrAuth(this IServiceCollection services, IConfiguration configuration)
    {
        // Auth database - separate from main translarr-db
        var connectionString = configuration.GetConnectionString("translarr-auth")
                               ?? "Data Source=translarr-auth.db";

        services.AddDbContext<AuthDbContext>(options =>
        {
            options.UseSqlite(connectionString);
        });

        // ASP.NET Identity
        services.AddIdentity<IdentityUser, IdentityRole>(options =>
            {
                // Password policy - minimum 8 chars, no complexity requirements
                options.Password.RequiredLength = AuthConstants.MinPasswordLength;
                options.Password.RequireDigit = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;

                // Lockout - 5 failed attempts, 15 min lockout
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders();

        // Cookie authentication
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = AuthConstants.CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;

            // API returns 401/403 instead of redirecting to a login page
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

        // Data Protection - persist keys so cookies survive container restarts
        var dpKeysPath = configuration["DataProtection:KeysPath"] ?? AuthConstants.DefaultDpKeysPath;
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath))
            .SetApplicationName(AuthConstants.DataProtectionAppName);

        return services;
    }

    /// <summary>
    /// Ensure auth database is created and ready (uses EnsureCreatedAsync, not migrations)
    /// </summary>
    public static async Task InitializeAuthDatabaseAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await context.Database.EnsureCreatedAsync();
    }
}
