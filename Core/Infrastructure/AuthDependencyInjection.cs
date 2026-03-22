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
        // Bind options
        var authOptions = new AuthOptions();
        configuration.GetSection(AuthOptions.SectionName).Bind(authOptions);
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));

        var jwtOptions = new JwtOptions();
        configuration.GetSection(JwtOptions.SectionName).Bind(jwtOptions);
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        if (string.IsNullOrEmpty(jwtOptions.Secret))
            throw new InvalidOperationException("Jwt:Secret is not configured. Set Jwt__Secret environment variable.");

        // Auth database
        var connectionString = configuration.GetConnectionString("translarr-auth")
                               ?? "Data Source=translarr-auth.db";

        services.AddDbContext<AuthDbContext>(options =>
        {
            options.UseSqlite(connectionString);
        });

        // Identity - user management
        services.AddIdentity<IdentityUser, IdentityRole>(options =>
            {
                options.Password.RequiredLength = authOptions.MinPasswordLength;
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
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret));

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
                // SignalR sends JWT as query string during WebSocket upgrade
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                            context.Token = token;
                        return Task.CompletedTask;
                    }
                };
            });

        // Data Protection - API only
        var dpKeysPath = configuration["DataProtection:KeysPath"]
                         ?? (Directory.Exists("/app") ? "/app/data/dp-keys" : Path.Combine(Path.GetTempPath(), "translarr-dp-keys"));
        Directory.CreateDirectory(dpKeysPath);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath))
            .SetApplicationName("Translarr");

        return services;
    }

    public static async Task InitializeAuthDatabaseAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await context.Database.EnsureCreatedAsync();
    }
}
