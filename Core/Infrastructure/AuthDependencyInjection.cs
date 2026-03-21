using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Translarr.Core.Application.Constants;
using Translarr.Core.Infrastructure.Persistence;

namespace Translarr.Core.Infrastructure;

public static class AuthDependencyInjection
{
    public static IServiceCollection AddTranslarrAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("translarr-auth")
                               ?? "Data Source=translarr-auth.db";

        services.AddDbContext<AuthDbContext>(options =>
        {
            options.UseSqlite(connectionString);
        });

        services.AddIdentity<IdentityUser, IdentityRole>(options =>
            {
                options.Password.RequiredLength = AuthConstants.MinPasswordLength;
                options.Password.RequireDigit = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders();

        // JWT Bearer authentication
        var jwtSecret = configuration["Jwt:Secret"] ?? AuthConstants.DefaultJwtSecret;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        // Data Protection - API only (no sharing with WebApp needed anymore)
        var dpKeysPath = configuration["DataProtection:KeysPath"]
                         ?? (Directory.Exists("/app") ? AuthConstants.DefaultDpKeysPath : Path.Combine(Path.GetTempPath(), "translarr-dp-keys"));
        Directory.CreateDirectory(dpKeysPath);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath))
            .SetApplicationName(AuthConstants.DataProtectionAppName);

        return services;
    }

    public static async Task InitializeAuthDatabaseAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await context.Database.EnsureCreatedAsync();
    }
}
