# Authentication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add cookie-based authentication with ASP.NET Identity to Translarr (single admin account, setup wizard on first launch).

**Architecture:** Separate SQLite database for Identity (`translarr-auth.db`), cookie auth with circuit-scoped forwarding in Blazor Server, WebApp-side login/logout POST endpoints, rate-limited API login endpoint with lockout.

**Tech Stack:** ASP.NET Identity, EF Core SQLite, ASP.NET Data Protection, ASP.NET Rate Limiting, Blazor Server AuthenticationStateProvider

**Spec:** `docs/superpowers/specs/2026-03-20-authentication-design.md`

---

### Task 1: Package References and AuthDbContext

**Files:**
- Modify: `Directory.Packages.props:12` (remove JwtBearer)
- Modify: `Core/Infrastructure/Infrastructure.csproj` (add Identity package + FrameworkReference)
- Create: `Core/Infrastructure/Persistence/AuthDbContext.cs`

- [ ] **Step 1: Remove JwtBearer from Directory.Packages.props**

Remove this line from `Directory.Packages.props` (line 12):
```xml
<PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.0.10" />
```

Note: `Microsoft.AspNetCore.Identity.EntityFrameworkCore` is already in `Directory.Packages.props` at line 13 (version 10.0.0). Do NOT add a duplicate.

- [ ] **Step 2: Update Infrastructure.csproj**

Add to `Core/Infrastructure/Infrastructure.csproj`:
```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>

<ItemGroup>
  <!-- existing PackageReferences... -->
  <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" />
</ItemGroup>
```

The `FrameworkReference` is **required** because `AuthDependencyInjection.cs` uses `StatusCodes`, `CookieSecurePolicy`, `SameSiteMode`, and other ASP.NET Core types not available from NuGet packages alone.

- [ ] **Step 3: Create AuthDbContext**

Create `Core/Infrastructure/Persistence/AuthDbContext.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Translarr.Core.Infrastructure.Persistence;

public class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<IdentityUser>(options);
```

- [ ] **Step 4: Verify build**

Run: `dotnet build Core/Infrastructure/Infrastructure.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props Core/Infrastructure/Infrastructure.csproj Core/Infrastructure/Persistence/AuthDbContext.cs
git commit -m "Add AuthDbContext with Identity and remove unused JwtBearer package"
```

---

### Task 2: Auth DI Registration

**Files:**
- Create: `Core/Infrastructure/AuthDependencyInjection.cs`

- [ ] **Step 1: Create AuthDependencyInjection.cs**

Create `Core/Infrastructure/AuthDependencyInjection.cs`:
```csharp
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
                options.Password.RequiredLength = 8;
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
            options.Cookie.Name = ".Translarr.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
            // Note: ExpireTimeSpan is the max lifetime. Whether the cookie is
            // persistent (survives browser close) or session-only is controlled by
            // isPersistent in SignInManager calls (the "Remember me" checkbox).

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
        var dpKeysPath = configuration["DataProtection:KeysPath"] ?? "/app/data/dp-keys";
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath))
            .SetApplicationName("Translarr");

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
```

- [ ] **Step 2: Verify build**

Run: `dotnet build Core/Infrastructure/Infrastructure.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add Core/Infrastructure/AuthDependencyInjection.cs
git commit -m "Add auth DI registration with Identity, cookie config, and Data Protection"
```

---

### Task 3: API Security Middleware + Auth Endpoints

These are combined into one task to avoid committing code that references endpoints that don't exist yet.

**Files:**
- Modify: `Core/Api/Program.cs` (full rewrite)
- Create: `Core/Api/Endpoints/AuthEndpoints.cs`

- [ ] **Step 1: Create AuthEndpoints.cs**

Create `Core/Api/Endpoints/AuthEndpoints.cs`:
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Translarr.Core.Api.Endpoints;

public static class AuthEndpoints
{
    private static readonly SemaphoreSlim SetupLock = new(1, 1);

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/setup/status", GetSetupStatus)
            .WithName("GetSetupStatus")
            .AllowAnonymous();

        group.MapPost("/setup", Setup)
            .WithName("Setup")
            .AllowAnonymous();

        group.MapPost("/login", Login)
            .WithName("Login")
            .AllowAnonymous()
            .RequireRateLimiting("login");

        group.MapPost("/logout", Logout)
            .WithName("Logout")
            .RequireAuthorization();

        group.MapGet("/me", GetCurrentUser)
            .WithName("GetCurrentUser")
            .RequireAuthorization();

        group.MapPost("/change-password", ChangePassword)
            .WithName("ChangePassword")
            .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> GetSetupStatus(UserManager<IdentityUser> userManager)
    {
        var hasUsers = await userManager.Users.AnyAsync();
        return Results.Ok(new { needsSetup = !hasUsers });
    }

    private static async Task<IResult> Setup(
        [FromBody] SetupRequest request,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager)
    {
        if (!await SetupLock.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            return Results.Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Setup already in progress"
            });
        }

        try
        {
            if (await userManager.Users.AnyAsync())
            {
                return Results.NotFound();
            }

            var user = new IdentityUser { UserName = request.Username };
            var result = await userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
            {
                return Results.ValidationProblem(
                    result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
            }

            // Verify no race condition
            if (await userManager.Users.CountAsync() > 1)
            {
                await userManager.DeleteAsync(user);
                return Results.Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Race condition detected"
                });
            }

            await signInManager.SignInAsync(user, isPersistent: true);
            return Results.Ok(new { message = "Setup complete" });
        }
        finally
        {
            SetupLock.Release();
        }
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        SignInManager<IdentityUser> signInManager)
    {
        var result = await signInManager.PasswordSignInAsync(
            request.Username,
            request.Password,
            isPersistent: request.RememberMe,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            if (result.IsLockedOut)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status423Locked,
                    title: "Account locked",
                    detail: "Too many failed attempts. Please try again later.");
            }

            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials");
        }

        return Results.Ok(new { message = "Login successful" });
    }

    private static async Task<IResult> Logout(SignInManager<IdentityUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.Ok(new { message = "Logged out" });
    }

    private static IResult GetCurrentUser(ClaimsPrincipal user)
    {
        return Results.Ok(new
        {
            username = user.Identity?.Name,
            isAuthenticated = user.Identity?.IsAuthenticated ?? false
        });
    }

    private static async Task<IResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        UserManager<IdentityUser> userManager,
        ClaimsPrincipal user)
    {
        var identityUser = await userManager.GetUserAsync(user);
        if (identityUser == null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "User not found");
        }

        var result = await userManager.ChangePasswordAsync(identityUser, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            return Results.ValidationProblem(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
        }

        return Results.Ok(new { message = "Password changed successfully" });
    }
}

public record SetupRequest(string Username, string Password);
public record LoginRequest(string Username, string Password, bool RememberMe = false);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
```

- [ ] **Step 2: Rewrite API Program.cs**

Replace `Core/Api/Program.cs` entirely:
```csharp
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using SwaggerThemes;
using Translarr.Core.Api.Endpoints;
using Translarr.Core.Api.Middleware;
using Translarr.Core.Infrastructure;
using Translarr.ServiceDefaults;

namespace Translarr.Core.Api;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();

        // Add Infrastructure services (DbContext, Repositories, Services)
        builder.Services.AddInfrastructure(builder.Configuration);

        // Add auth (Identity, cookie, Data Protection)
        builder.Services.AddTranslarrAuth(builder.Configuration);

        builder.Services.AddAuthorization();

        // Forwarded headers for reverse proxy / Cloudflare Tunnel
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = null;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        // Rate limiting on login endpoint
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("login", limiter =>
            {
                limiter.PermitLimit = 5;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });

        // Global exception handler
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();

        // Swagger - registered always, but only exposed in Development
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Translarr API",
                Version = "v1",
                Description = "API for automatic subtitle translation using Gemini AI"
            });
        });

        var app = builder.Build();

        // Initialize databases
        await DependencyInjection.InitializeDatabaseAsync(app.Services);
        await AuthDependencyInjection.InitializeAuthDatabaseAsync(app.Services);

        app.MapDefaultEndpoints();

        // Forwarded headers MUST be before auth and rate limiter
        app.UseForwardedHeaders();

        // Swagger - development only
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(
                Theme.UniversalDark,
                setupAction: c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Translarr API v1");
                    c.RoutePrefix = "swagger";
                });
        }

        app.UseExceptionHandler();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        // Map API endpoints
        var apiGroup = app.MapGroup("/api");

        apiGroup.MapGroup("/auth")
            .MapAuthEndpoints()
            .WithTags("Authentication");

        apiGroup.MapGroup("/library")
            .MapLibraryEndpoints()
            .RequireAuthorization()
            .WithTags("Library");

        apiGroup.MapGroup("/translation")
            .MapTranslationEndpoints()
            .RequireAuthorization()
            .WithTags("Translation");

        apiGroup.MapGroup("/settings")
            .MapSettingsEndpoints()
            .RequireAuthorization()
            .WithTags("Settings");

        apiGroup.MapGroup("/stats")
            .MapStatsEndpoints()
            .RequireAuthorization()
            .WithTags("Statistics");

        apiGroup.MapGroup("/series")
            .MapSeriesWatchEndpoints()
            .RequireAuthorization()
            .WithTags("Series Watch");

        await app.RunAsync();
    }
}
```

Note: `app.UseHttpsRedirection()` and `app.UseCors()` are intentionally removed. `builder.AddSqliteConnection("sqlite")` was a vestigial Aspire call and is removed.

- [ ] **Step 3: Verify build**

Run: `dotnet build Core/Api/Api.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add Core/Api/Endpoints/AuthEndpoints.cs Core/Api/Program.cs
git commit -m "Add auth endpoints and security middleware (forwarded headers, rate limiting, Swagger guard)"
```

---

### Task 4: Frontend Auth Plumbing

**Files:**
- Modify: `Frontend/HavitWebApp/HavitWebApp.csproj` (add Identity package)
- Create: `Frontend/HavitWebApp/Auth/AuthCookieHolder.cs`
- Create: `Frontend/HavitWebApp/Auth/CookieForwardingHandler.cs`
- Create: `Frontend/HavitWebApp/Auth/TranslarrAuthStateProvider.cs`
- Create: `Frontend/HavitWebApp/Components/Account/RedirectToLogin.razor`

- [ ] **Step 1: Add package to WebApp csproj**

In `Frontend/HavitWebApp/HavitWebApp.csproj`, add to the existing PackageReferences `<ItemGroup>` (lines 18-20):
```xml
<PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" />
```

- [ ] **Step 2: Create AuthCookieHolder**

Create `Frontend/HavitWebApp/Auth/AuthCookieHolder.cs`:
```csharp
namespace Translarr.Frontend.HavitWebApp.Auth;

/// <summary>
/// Circuit-scoped service that holds the auth cookie captured during the
/// initial HTTP request. HttpContext is null during SignalR interactions,
/// so we capture the cookie once and reuse it for the circuit lifetime.
/// </summary>
public class AuthCookieHolder
{
    public string? CookieValue { get; set; }
}
```

- [ ] **Step 3: Create CookieForwardingHandler**

Create `Frontend/HavitWebApp/Auth/CookieForwardingHandler.cs`:
```csharp
namespace Translarr.Frontend.HavitWebApp.Auth;

/// <summary>
/// DelegatingHandler that attaches the auth cookie to outgoing API requests.
/// Reads from circuit-scoped AuthCookieHolder, NOT from IHttpContextAccessor.
/// Must be registered as Transient (required by IHttpClientFactory).
/// </summary>
public class CookieForwardingHandler(AuthCookieHolder cookieHolder) : DelegatingHandler
{
    private const string CookieName = ".Translarr.Auth";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(cookieHolder.CookieValue))
        {
            request.Headers.Add("Cookie", $"{CookieName}={cookieHolder.CookieValue}");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
```

- [ ] **Step 4: Create TranslarrAuthStateProvider**

Create `Frontend/HavitWebApp/Auth/TranslarrAuthStateProvider.cs`:
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Translarr.Frontend.HavitWebApp.Auth;

public class TranslarrAuthStateProvider(
    IHttpClientFactory httpClientFactory,
    AuthCookieHolder cookieHolder) : AuthenticationStateProvider
{
    private static readonly AuthenticationState AnonymousState =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (string.IsNullOrEmpty(cookieHolder.CookieValue))
        {
            return AnonymousState;
        }

        try
        {
            var client = httpClientFactory.CreateClient("TranslarrApi");
            var response = await client.GetAsync("/api/auth/me");

            if (!response.IsSuccessStatusCode)
            {
                return AnonymousState;
            }

            var json = await response.Content.ReadFromJsonAsync<MeResponse>();
            if (json is not { IsAuthenticated: true })
            {
                return AnonymousState;
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, json.Username ?? "admin")],
                authenticationType: "Translarr");

            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch
        {
            return AnonymousState;
        }
    }

    public void NotifyAuthStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private record MeResponse(string? Username, bool IsAuthenticated);
}
```

- [ ] **Step 5: Create RedirectToLogin component**

Create `Frontend/HavitWebApp/Components/Account/RedirectToLogin.razor`:
```razor
@inject NavigationManager NavigationManager

@code {
    protected override void OnInitialized()
    {
        NavigationManager.NavigateTo("/login", forceLoad: true);
    }
}
```

- [ ] **Step 6: Verify build**

Run: `dotnet build Frontend/HavitWebApp/HavitWebApp.csproj`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add Frontend/HavitWebApp/Auth/ Frontend/HavitWebApp/Components/Account/ Frontend/HavitWebApp/HavitWebApp.csproj
git commit -m "Add frontend auth plumbing: cookie holder, forwarding handler, auth state provider"
```

---

### Task 5: Frontend DI, Middleware, and Routing

**Files:**
- Modify: `Frontend/HavitWebApp/Program.cs` (full rewrite)
- Modify: `Frontend/HavitWebApp/Components/Routes.razor` (AuthorizeRouteView)
- Modify: `Frontend/HavitWebApp/_Imports.razor` (add auth usings)

- [ ] **Step 1: Rewrite WebApp Program.cs**

Replace `Frontend/HavitWebApp/Program.cs` entirely:
```csharp
using Havit.Blazor.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Translarr.Frontend.HavitWebApp.Auth;
using Translarr.Frontend.HavitWebApp.Components;
using Translarr.Frontend.HavitWebApp.Services;
using Translarr.ServiceDefaults;

namespace Translarr.Frontend.HavitWebApp;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddHxServices();
        builder.Services.AddHxMessenger();

        builder.Services.AddScoped<ThemeService>();

        // Auth services
        builder.Services.AddScoped<AuthCookieHolder>();
        builder.Services.AddTransient<CookieForwardingHandler>();
        builder.Services.AddScoped<AuthenticationStateProvider, TranslarrAuthStateProvider>();
        builder.Services.AddAuthorization();
        builder.Services.AddHttpContextAccessor();

        // Data Protection - shared keys with API
        var dpKeysPath = builder.Configuration["DataProtection:KeysPath"] ?? "/app/data/dp-keys";
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath))
            .SetApplicationName("Translarr");

        // HttpClient for API with cookie forwarding
        var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https+http://Translarr-Api";
        builder.Services.AddHttpClient("TranslarrApi", client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
        }).AddHttpMessageHandler<CookieForwardingHandler>();

        // HttpClient without cookie forwarding - for login/setup/logout endpoints
        builder.Services.AddHttpClient("TranslarrApiDirect", client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
        });

        // API services
        builder.Services.AddScoped<LibraryApiService>();
        builder.Services.AddScoped<TranslationApiService>();
        builder.Services.AddScoped<SettingsApiService>();
        builder.Services.AddScoped<StatsApiService>();
        builder.Services.AddScoped<SeriesWatchApiService>();

        var app = builder.Build();

        app.MapDefaultEndpoints();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseStaticFiles();

        // Capture auth cookie from browser request into circuit-scoped holder
        app.Use(async (context, next) =>
        {
            var cookieHolder = context.RequestServices.GetService<AuthCookieHolder>();
            if (cookieHolder != null &&
                context.Request.Cookies.TryGetValue(".Translarr.Auth", out var cookieValue))
            {
                cookieHolder.CookieValue = cookieValue;
            }
            await next();
        });

        app.UseAntiforgery();

        // WebApp-side login endpoint: proxies to API, forwards Set-Cookie to browser
        app.MapPost("/account/login", async (HttpContext context, IHttpClientFactory factory) =>
        {
            var form = await context.Request.ReadFormAsync();
            var client = factory.CreateClient("TranslarrApiDirect");
            var response = await client.PostAsJsonAsync("/api/auth/login", new
            {
                username = form["username"].ToString(),
                password = form["password"].ToString(),
                rememberMe = form.ContainsKey("rememberMe")
            });

            if (response.IsSuccessStatusCode &&
                response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies)
                {
                    context.Response.Headers.Append("Set-Cookie", cookie);
                }
                var returnUrl = form["returnUrl"].FirstOrDefault();
                context.Response.Redirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
            }
            else
            {
                var statusCode = (int)response.StatusCode;
                var errorParam = statusCode switch
                {
                    423 => "locked",
                    429 => "ratelimit",
                    _ => "invalid"
                };
                context.Response.Redirect($"/login?error={errorParam}");
            }
        }).AllowAnonymous();

        // WebApp-side setup endpoint: proxies to API, forwards Set-Cookie
        app.MapPost("/account/setup", async (HttpContext context, IHttpClientFactory factory) =>
        {
            var form = await context.Request.ReadFormAsync();
            var password = form["password"].ToString();
            var confirmPassword = form["confirmPassword"].ToString();

            if (password != confirmPassword)
            {
                context.Response.Redirect("/setup?error=mismatch");
                return;
            }

            var client = factory.CreateClient("TranslarrApiDirect");
            var response = await client.PostAsJsonAsync("/api/auth/setup", new
            {
                username = form["username"].ToString(),
                password
            });

            if (response.IsSuccessStatusCode &&
                response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies)
                {
                    context.Response.Headers.Append("Set-Cookie", cookie);
                }
                context.Response.Redirect("/");
            }
            else
            {
                context.Response.Redirect("/setup?error=failed");
            }
        }).AllowAnonymous();

        // WebApp-side logout endpoint
        app.MapPost("/account/logout", async (HttpContext context, IHttpClientFactory factory) =>
        {
            var client = factory.CreateClient("TranslarrApiDirect");
            if (context.Request.Cookies.TryGetValue(".Translarr.Auth", out var cookie))
            {
                client.DefaultRequestHeaders.Add("Cookie", $".Translarr.Auth={cookie}");
            }
            await client.PostAsync("/api/auth/logout", null);
            context.Response.Cookies.Delete(".Translarr.Auth");
            context.Response.Redirect("/login");
        }).AllowAnonymous();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
```

Key points:
- `CookieForwardingHandler` is **Transient** (required by `IHttpClientFactory`)
- `AuthCookieHolder` is **Scoped** (lives for circuit lifetime)
- Two HttpClients: `TranslarrApi` (with cookie forwarding) and `TranslarrApiDirect` (without, for login/setup/logout proxy endpoints)
- Login, setup, logout are WebApp-side minimal API endpoints that proxy to API and forward `Set-Cookie` headers
- `AddCascadingAuthenticationState()` is NOT called here (we use the `<CascadingAuthenticationState>` component wrapper in Routes.razor instead - pick one, not both)

- [ ] **Step 2: Update Routes.razor**

Replace `Frontend/HavitWebApp/Components/Routes.razor`:
```razor
@using Microsoft.AspNetCore.Components.Authorization
@using Translarr.Frontend.HavitWebApp.Components.Account

<CascadingAuthenticationState>
    <Router AppAssembly="typeof(Program).Assembly">
        <Found Context="routeData">
            <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
                <NotAuthorized>
                    <RedirectToLogin />
                </NotAuthorized>
                <Authorizing>
                    <div class="d-flex justify-content-center my-4">
                        <div class="spinner-border text-primary" role="status">
                            <span class="visually-hidden">Checking authentication...</span>
                        </div>
                    </div>
                </Authorizing>
            </AuthorizeRouteView>
            <FocusOnNavigate RouteData="routeData" Selector="h1" />
        </Found>
    </Router>
</CascadingAuthenticationState>
```

- [ ] **Step 3: Add auth usings to _Imports.razor**

Add to `Frontend/HavitWebApp/_Imports.razor`:
```razor
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
```

- [ ] **Step 4: Verify build**

Run: `dotnet build Frontend/HavitWebApp/HavitWebApp.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add Frontend/HavitWebApp/Program.cs Frontend/HavitWebApp/Components/Routes.razor Frontend/HavitWebApp/_Imports.razor
git commit -m "Wire up frontend auth: DI, cookie capture, login/logout proxy endpoints, AuthorizeRouteView"
```

---

### Task 6: Login and Setup Pages

These are **static SSR pages** that render plain HTML forms posting to the WebApp-side endpoints from Task 5. They must NOT use interactive Blazor.

**Files:**
- Create: `Frontend/HavitWebApp/Components/Pages/Login.razor`
- Create: `Frontend/HavitWebApp/Components/Pages/Setup.razor`

- [ ] **Step 1: Create Login page**

Create `Frontend/HavitWebApp/Components/Pages/Login.razor`:
```razor
@page "/login"
@attribute [AllowAnonymous]
@attribute [RenderModeInteractiveServer(false)]

<PageTitle>Login - Translarr</PageTitle>

<div class="container">
    <div class="row justify-content-center mt-5">
        <div class="col-md-4">
            <div class="card">
                <div class="card-body">
                    <div class="text-center mb-4">
                        <img src="/favicon-32x32.png" alt="Translarr" style="height: 48px;" />
                        <h3 class="mt-2">Translarr</h3>
                    </div>

                    @if (!string.IsNullOrEmpty(Error))
                    {
                        <div class="alert alert-danger" role="alert">
                            @(Error switch
                            {
                                "locked" => "Account locked. Too many failed attempts. Try again later.",
                                "ratelimit" => "Too many login attempts. Please wait a minute.",
                                _ => "Invalid username or password."
                            })
                        </div>
                    }

                    <form method="post" action="/account/login">
                        <AntiforgeryToken />
                        <input type="hidden" name="returnUrl" value="@ReturnUrl" />
                        <div class="mb-3">
                            <label class="form-label">Username</label>
                            <input type="text" class="form-control" name="username" required autofocus />
                        </div>
                        <div class="mb-3">
                            <label class="form-label">Password</label>
                            <input type="password" class="form-control" name="password" required />
                        </div>
                        <div class="mb-3 form-check">
                            <input type="checkbox" class="form-check-input" name="rememberMe" value="true" id="rememberMe" />
                            <label class="form-check-label" for="rememberMe">Remember me (30 days)</label>
                        </div>
                        <button type="submit" class="btn btn-primary w-100">Login</button>
                    </form>
                </div>
            </div>
        </div>
    </div>
</div>

@code {
    [SupplyParameterFromQuery]
    private string? Error { get; set; }

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }
}
```

Note: This is a **plain HTML form** that POSTs to `/account/login` (the WebApp-side endpoint from Task 5). No Blazor interactivity, no `@bind`, no `@onsubmit`. The form fields use `name` attributes for standard HTML form submission. Error messages come from query parameters set by the login endpoint redirect.

If `@attribute [RenderModeInteractiveServer(false)]` doesn't compile (this syntax may vary by .NET version), use `@rendermode @(null)` instead, or handle it at the router level by removing `InteractiveServer` from `App.razor`'s `<Routes>` and applying `@rendermode InteractiveServer` individually to pages that need it.

- [ ] **Step 2: Create Setup page**

Create `Frontend/HavitWebApp/Components/Pages/Setup.razor`:
```razor
@page "/setup"
@attribute [AllowAnonymous]
@attribute [RenderModeInteractiveServer(false)]

<PageTitle>Setup - Translarr</PageTitle>

<div class="container">
    <div class="row justify-content-center mt-5">
        <div class="col-md-4">
            <div class="card">
                <div class="card-body">
                    <div class="text-center mb-4">
                        <img src="/favicon-32x32.png" alt="Translarr" style="height: 48px;" />
                        <h3 class="mt-2">Translarr Setup</h3>
                        <p class="text-muted">Create your admin account</p>
                    </div>

                    @if (!string.IsNullOrEmpty(Error))
                    {
                        <div class="alert alert-danger" role="alert">
                            @(Error switch
                            {
                                "mismatch" => "Passwords do not match.",
                                _ => "Setup failed. Please try again."
                            })
                        </div>
                    }

                    <form method="post" action="/account/setup">
                        <AntiforgeryToken />
                        <div class="mb-3">
                            <label class="form-label">Username</label>
                            <input type="text" class="form-control" name="username" required autofocus />
                        </div>
                        <div class="mb-3">
                            <label class="form-label">Password</label>
                            <input type="password" class="form-control" name="password" required minlength="8" />
                            <div class="form-text">Minimum 8 characters</div>
                        </div>
                        <div class="mb-3">
                            <label class="form-label">Confirm Password</label>
                            <input type="password" class="form-control" name="confirmPassword" required />
                        </div>
                        <button type="submit" class="btn btn-primary w-100">Create Account</button>
                    </form>
                </div>
            </div>
        </div>
    </div>
</div>

@code {
    [SupplyParameterFromQuery]
    private string? Error { get; set; }
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build Frontend/HavitWebApp/HavitWebApp.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add Frontend/HavitWebApp/Components/Pages/Login.razor Frontend/HavitWebApp/Components/Pages/Setup.razor
git commit -m "Add login and setup pages (static SSR, plain HTML forms)"
```

---

### Task 7: Logout Button and Change Password UI

**Files:**
- Modify: `Frontend/HavitWebApp/Components/Layout/MainLayout.razor:15-17` (logout button)
- Modify: `Frontend/HavitWebApp/Components/Pages/Settings.razor` (change password section)
- Modify: `Frontend/HavitWebApp/Services/SettingsApiService.cs` (add ChangePasswordAsync)

- [ ] **Step 1: Add logout button to navbar**

In `Frontend/HavitWebApp/Components/Layout/MainLayout.razor`, replace the `ms-auto` div (lines 15-17):
```razor
            <div class="ms-auto d-flex align-items-center gap-2">
                <ThemeSwitcher />
                <AuthorizeView>
                    <Authorized>
                        <form method="post" action="/account/logout">
                            <button type="submit" class="btn btn-outline-light btn-sm">
                                <i class="bi bi-box-arrow-right me-1"></i> Logout
                            </button>
                        </form>
                    </Authorized>
                </AuthorizeView>
            </div>
```

- [ ] **Step 2: Add ChangePasswordAsync to SettingsApiService**

Add to `Frontend/HavitWebApp/Services/SettingsApiService.cs`:
```csharp
public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword)
{
    var client = httpClientFactory.CreateClient("TranslarrApi");
    var response = await client.PostAsJsonAsync("/api/auth/change-password", new
    {
        currentPassword,
        newPassword
    });
    return response.IsSuccessStatusCode;
}
```

- [ ] **Step 3: Add change password section to Settings page**

In `Frontend/HavitWebApp/Components/Pages/Settings.razor`, add before the closing `</div>` of the right column (before line 118 `</div>`):

```razor
            <!-- Change Password -->
            <HxCard CssClass="mb-4">
                <HeaderTemplate>
                    <h5 class="mb-0">Change Password</h5>
                </HeaderTemplate>
                <BodyTemplate>
                    <div class="mb-3">
                        <label class="form-label">Current Password</label>
                        <input type="password" class="form-control" @bind="_currentPassword" />
                    </div>
                    <div class="mb-3">
                        <label class="form-label">New Password</label>
                        <input type="password" class="form-control" @bind="_newPassword" />
                        <div class="form-text">Minimum 8 characters</div>
                    </div>
                    <div class="mb-3">
                        <label class="form-label">Confirm New Password</label>
                        <input type="password" class="form-control" @bind="_confirmNewPassword" />
                    </div>
                    <HxButton Color="ThemeColor.Warning"
                              OnClick="ChangePassword"
                              Enabled="!_isChangingPassword">
                        Change Password
                    </HxButton>
                </BodyTemplate>
            </HxCard>
```

In the `@code` block, add fields and method:
```csharp
private string _currentPassword = "";
private string _newPassword = "";
private string _confirmNewPassword = "";
private bool _isChangingPassword;

private async Task ChangePassword()
{
    if (_newPassword != _confirmNewPassword)
    {
        Messenger.AddError("New passwords do not match");
        return;
    }

    if (_newPassword.Length < 8)
    {
        Messenger.AddError("Password must be at least 8 characters");
        return;
    }

    try
    {
        _isChangingPassword = true;
        var success = await SettingsService.ChangePasswordAsync(_currentPassword, _newPassword);

        if (success)
        {
            Messenger.AddInformation("Password changed successfully");
            _currentPassword = "";
            _newPassword = "";
            _confirmNewPassword = "";
        }
        else
        {
            Messenger.AddError("Failed to change password. Check your current password.");
        }
    }
    catch (Exception ex)
    {
        Messenger.AddError($"Error: {ex.Message}");
    }
    finally
    {
        _isChangingPassword = false;
    }
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build Frontend/HavitWebApp/HavitWebApp.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add Frontend/HavitWebApp/Components/Layout/MainLayout.razor Frontend/HavitWebApp/Components/Pages/Settings.razor Frontend/HavitWebApp/Services/SettingsApiService.cs
git commit -m "Add logout button to navbar and change password to Settings page"
```

---

### Task 8: Aspire and Docker Configuration

**Files:**
- Modify: `AppHost/AppHost.cs`
- Modify: `compose.yaml`
- Modify: `env.example`

- [ ] **Step 1: Update AppHost.cs**

Replace `AppHost/AppHost.cs`:
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var mediaRootPathOnHostParam = builder.AddParameter("MediaRootOnHost");
var mediaRootPathOnHost = mediaRootPathOnHostParam.Resource.GetValueAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult()
                          ?? throw new ArgumentException("MediaRootOnHost parameter not found");

var sqlite = builder.AddSqlite(name: "translarr-db", databaseFileName: "translarr.db")
    .WithSqliteWeb();

// Separate auth database (no web viewer needed - it's just Identity tables)
var sqliteAuth = builder.AddSqlite(name: "translarr-auth", databaseFileName: "translarr-auth.db");

var api = builder.AddProject<Projects.Api>("Translarr-Api")
    .WaitFor(sqlite)
    .WaitFor(sqliteAuth)
    .WithReference(sqlite)
    .WithReference(sqliteAuth)
    .WithEnvironment("MediaRootPath", mediaRootPathOnHost);

builder.AddProject<Projects.HavitWebApp>("Translarr-Havit-Web")
    .WaitFor(api)
    .WithReference(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

- [ ] **Step 2: Update compose.yaml**

Replace `compose.yaml`:
```yaml
services:
  translarr-db-init:
    image: alpine:latest
    container_name: translarr-db-init
    volumes:
      - translarr-db:/app/data
    command: >
      sh -c "for f in translarr.db translarr-auth.db; do
        [ ! -f /app/data/$$f ] && touch /app/data/$$f && echo \"$$f created\" || echo \"$$f exists\";
      done &&
      mkdir -p /app/data/dp-keys &&
      chown -R 1000:1000 /app/data &&
      chmod -R 755 /app/data &&
      chmod 666 /app/data/*.db &&
      echo 'Permissions set'"
    restart: "no"

  translarr-api:
    image: jmcjm/translarr-api:nightly
    container_name: translarr-api
    restart: unless-stopped
    ports:
      - "${API_PORT:-5000}:8080"
    environment:
      - ConnectionStrings__translarr-db=Data Source=/app/data/translarr.db
      - ConnectionStrings__translarr-auth=Data Source=/app/data/translarr-auth.db
      - DataProtection__KeysPath=/app/data/dp-keys
      - MediaRootPath=/app/mediaroot
      - ASPNETCORE_URLS=http://+:8080
      - ASPNETCORE_ENVIRONMENT=Production
    volumes:
      - translarr-db:/app/data
      - ${MEDIA_ROOT_PATH:-./media}:/app/mediaroot:rw,z
    user: "1000:1000"
    depends_on:
      translarr-db-init:
        condition: service_completed_successfully

  translarr-web:
    image: jmcjm/translarr-havit-web:nightly
    container_name: translarr-web
    restart: unless-stopped
    ports:
      - "${WEB_PORT:-5001}:8080"
    environment:
      - ApiBaseUrl=http://translarr-api:8080
      - DataProtection__KeysPath=/app/data/dp-keys
      - ASPNETCORE_URLS=http://+:8080
      - ASPNETCORE_ENVIRONMENT=Production
    volumes:
      # Shared volume for Data Protection keys (API creates keys, WebApp reads them)
      - translarr-db:/app/data
    depends_on:
      - translarr-api

volumes:
  translarr-db:
    driver: local
```

Note: WebApp volume is **not** `:ro` because if it starts before API, it may need to create the dp-keys directory. The `depends_on: translarr-api` ordering helps but doesn't guarantee API has written keys yet.

- [ ] **Step 3: Update env.example**

Add a comment about the auth database. Replace `env.example`:
```
# Translarr Docker Compose Configuration

# Directory path on host machine containing your media files
# This will be mounted to /app/mediaroot in the API container
MEDIA_ROOT_PATH=./media

# Port for API (default: 5000)
API_PORT=5000

# Port for Web UI (default: 5001)
WEB_PORT=5001
```

- [ ] **Step 4: Verify full solution build**

Run: `dotnet build --configuration Release`
Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit**

```bash
git add AppHost/AppHost.cs compose.yaml env.example
git commit -m "Update Aspire and Docker config for auth database and Data Protection keys"
```

---

### Task 9: Integration Verification

- [ ] **Step 1: Full solution build**

Run: `dotnet build --configuration Release`
Expected: Build succeeded, 0 errors

- [ ] **Step 2: Launch with Aspire**

Run: `cd AppHost && dotnet run`

Verify in logs:
- Auth database `EnsureCreatedAsync` completes
- No missing connection string errors
- Both API and WebApp start

- [ ] **Step 3: Verify setup flow**

1. Open WebApp URL in browser
2. Should redirect to `/login`
3. Login page should check setup status - if no users, show link/redirect to `/setup`
4. Create admin account with username + password
5. Should auto-login and redirect to dashboard

- [ ] **Step 4: Verify login flow**

1. Open WebApp in incognito
2. Should redirect to `/login`
3. Login with admin credentials
4. Should show dashboard with all data

- [ ] **Step 5: Verify auth guard**

1. In incognito (not logged in), navigate to `/settings`
2. Should redirect to `/login`
3. Call API directly: `curl http://localhost:5000/api/settings` should return 401

- [ ] **Step 6: Verify logout**

1. Click logout button in navbar
2. Should redirect to `/login`
3. Previously accessible pages should redirect to `/login`

- [ ] **Step 7: Verify change password**

1. Go to Settings page
2. Fill in change password form
3. Should succeed with correct current password
4. Should fail with wrong current password

- [ ] **Step 8: Fix any issues and commit**

```bash
git add -A
git commit -m "Fix integration issues from auth testing"
```
