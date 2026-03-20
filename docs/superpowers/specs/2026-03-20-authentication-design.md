# Authentication Design - Translarr

## Context

Translarr is a self-hosted subtitle translation tool exposed to the internet via reverse proxy (Nginx/Caddy) + Cloudflare Tunnel. Currently has zero authentication - all API endpoints and UI are publicly accessible, including sensitive data like the Gemini API key.

**Requirements:**
- Single admin account (one household, shared login)
- Setup wizard on first launch (user sets login + password in browser)
- Cookie-based auth with "Remember me" option (session vs 30 days)
- Must work both via HTTP on LAN (`http://192.168.x.x:5000`) and HTTPS through Cloudflare Tunnel
- Hardened against brute force (rate limiting, lockout)

## Architecture

### Login Flow

1. User opens WebApp -> `AuthenticationStateProvider` checks auth state by calling `/api/auth/me` -> 401 -> redirect to `/login`
2. User enters login + password -> WebApp Razor page POSTs to `/api/auth/login` server-side
3. API validates via Identity -> issues auth cookie in response
4. WebApp forwards the `Set-Cookie` header to the browser in its own response
5. Browser stores cookie -> redirect to dashboard
6. Subsequent page loads: browser sends cookie to WebApp, WebApp forwards it to API

**Login is a full page form POST**, not a Blazor interactive operation. This avoids the SignalR/HttpContext lifecycle problem entirely for the login flow.

### Setup Flow (First Launch)

1. App starts, checks if any user exists in auth database
2. No user -> frontend shows setup wizard instead of login
3. User sets login + password -> API creates admin account via Identity -> auto-login -> dashboard
4. Setup endpoint returns 404 from that point forward
5. **Race condition protection:** Setup endpoint uses a lock. After creating the user, verifies `Users.CountAsync() == 1`. If >1 (concurrent race), rolls back and returns conflict.

## Data Layer

### Two Separate SQLite Databases

- **`TranslarrDbContext : DbContext`** - existing database (`translarr.db`), no changes to existing migrations
- **`AuthDbContext : IdentityDbContext<IdentityUser>`** - new database (`translarr-auth.db`)

**Rationale:** Clean isolation. Reset auth by deleting one file without touching subtitle data. No Identity tables polluting the main database.

**Database initialization:** `AuthDbContext` uses `EnsureCreatedAsync()` at startup (not EF migrations). Since the auth DB can be freely blown away and recreated, migrations are unnecessary complexity. Initialization called alongside existing `TranslarrDatabaseInitializer`.

**Setup detection:** `UserManager.Users.AnyAsync() == false` means setup mode. No extra flags needed.

**Password change:** Via Settings page using `UserManager.ChangePasswordAsync()`.

### Identity Password Configuration

Explicit `IdentityOptions` configuration (overriding defaults):
- `Password.RequiredLength = 8`
- `Password.RequireDigit = false`
- `Password.RequireUppercase = false`
- `Password.RequireLowercase = false`
- `Password.RequireNonAlphanumeric = false`

Identity's defaults require all of these, so they must be explicitly disabled.

### Docker Compose Changes

- `translarr-db-init`: creates and chmods both `translarr.db` and `translarr-auth.db` in the same volume
- `translarr-api`: new env var `ConnectionStrings__translarr-auth=Data Source=/app/data/translarr-auth.db`
- Same volume, same user (1000:1000), same permissions

Updated init command:
```sh
sh -c "for f in translarr.db translarr-auth.db; do [ ! -f /app/data/$$f ] && touch /app/data/$$f && echo \"$$f created\" || echo \"$$f exists\"; done && chown -R 1000:1000 /app/data && chmod -R 755 /app/data && chmod 666 /app/data/*.db && echo 'Permissions set'"
```

### Aspire Configuration

New SQLite resource in `AppHost.cs`:
```csharp
var sqliteAuth = builder.AddSqlite(name: "translarr-auth", databaseFileName: "translarr-auth.db");

var api = builder.AddProject<Projects.Api>("Translarr-Api")
    .WaitFor(sqlite)
    .WaitFor(sqliteAuth)
    .WithReference(sqlite)
    .WithReference(sqliteAuth)
    // ...
```

## API Endpoints

### New `AuthEndpoints.cs`

| Endpoint | Method | Description | Auth |
|---|---|---|---|
| `/api/auth/setup/status` | GET | Returns `{ needsSetup: bool }` | Anonymous |
| `/api/auth/setup` | POST | Creates admin account (login + password) | Only when no users exist, locked |
| `/api/auth/login` | POST | Login, issues cookie | Anonymous, rate limited |
| `/api/auth/logout` | POST | Logout, clears cookie | Requires auth |
| `/api/auth/me` | GET | Returns current user info | Requires auth |
| `/api/auth/change-password` | POST | Change password (requires old password) | Requires auth |

### Security on Login Endpoint

- **Rate limiting:** ASP.NET built-in `RateLimiterOptions`, fixed window 5 requests/minute per IP, returns 429
- **Identity lockout:** 5 failed attempts -> account locked for 15 minutes
- **No info leakage:** Same "Invalid credentials" message regardless of whether login or password is wrong

### Security on Setup Endpoint

- Only works when `UserManager.Users.AnyAsync() == false`
- Protected by `SemaphoreSlim(1,1)` against race conditions
- After user creation, verifies single user exists, rolls back if raced
- Returns 404 after account creation (permanently)

### Existing Endpoint Groups

All get `.RequireAuthorization()`:
- `/api/library/*`
- `/api/translation/*`
- `/api/settings/*`
- `/api/stats/*`
- `/api/series/*`

### Swagger

Restrict to Development environment only. Remove the commented-out `if (app.Environment.IsDevelopment())` guard and enforce it. In production, Swagger is not accessible.

## Frontend (Blazor Server)

### WebApp Authentication State

The hardest part of this design. Blazor Server runs on the server over SignalR - `HttpContext` is only available during the initial HTTP request, not during subsequent SignalR interactions.

**Approach: Circuit-scoped cookie caching**

1. On initial HTTP request (circuit establishment), capture the auth cookie value from `HttpContext.Request.Cookies` and store it in a **circuit-scoped service** (`AuthCookieHolder`).
2. Custom `AuthenticationStateProvider` calls `/api/auth/me` once using the cached cookie on circuit start, caches the auth state for the circuit lifetime.
3. On 401 from any API call during the circuit, `AuthenticationStateProvider` notifies -> UI redirects to `/login` with `forceLoad: true` (full page reload to re-establish circuit).
4. `CookieForwardingHandler` (DelegatingHandler) reads from `AuthCookieHolder`, not from `IHttpContextAccessor`.

```
Browser --[cookie]--> WebApp (initial HTTP) --captures cookie--> AuthCookieHolder (scoped)
                                                                       |
Browser <--[SignalR]-- WebApp --[uses AuthCookieHolder]--> DelegatingHandler --[cookie]--> API
```

### Login/Setup Pages

These are **NOT interactive Blazor pages**. They are traditional Razor Pages (or static SSR Blazor pages with `@rendermode` explicitly set to null) that do full HTTP POST/redirect cycles. This avoids the SignalR/HttpContext problem entirely for auth flows.

- **`/setup`** - Fields: login, password, confirm password. Visible only when `setup/status` returns `needsSetup: true`. Success -> auto-login -> redirect to `/`.
- **`/login`** - Fields: login, password, "Remember me" checkbox. Success -> redirect to return URL or `/`.

Both pages POST to the API server-side, then forward the `Set-Cookie` from API response to the browser response.

### Auth Guard

- `CascadingAuthenticationState` wraps the router in `App.razor`
- `<AuthorizeRouteView>` replaces `<RouteView>`
- `<NotAuthorized>` template contains a `RedirectToLogin` component that calls `NavigationManager.NavigateTo("/login", forceLoad: true)` - `forceLoad` is critical to trigger a full HTTP request so cookie is picked up
- `/login` and `/setup` have `@attribute [AllowAnonymous]`

### Cookie Forwarding

Custom `DelegatingHandler` registered in the HTTP client pipeline:
- Reads cookie from circuit-scoped `AuthCookieHolder` service (NOT from `IHttpContextAccessor`)
- Attaches cookie to all outgoing requests to the API
- On 401 response, triggers auth state refresh

### UI Changes

- **Logout button** in navbar (next to theme switcher). Full page navigation to a logout endpoint that calls API `/api/auth/logout`, clears cookie, redirects to `/login`.
- **Change password** section on Settings page. Fields: old password, new password, confirm new password.

## Cookie Configuration

- `HttpOnly: true` - no JavaScript access
- `Secure: false` - **conscious tradeoff**: required for LAN HTTP access (`http://192.168.x.x`). Users accessing via Cloudflare Tunnel are protected by TLS at the transport layer. The cookie itself lacks the Secure attribute, meaning it will be sent over unencrypted LAN traffic. This is acceptable for a home network self-hosted app.
- `SameSite: Strict` - prevents cross-origin request forgery
- `Name: .Translarr.Auth`
- **Expiration:** Session (browser close) by default; 30 days sliding with "Remember me"

### Data Protection Key Persistence

ASP.NET Data Protection encrypts/signs cookies. Default behavior: keys stored in-memory, lost on container restart (all users logged out).

**Fix:** Persist keys to `/app/data/dp-keys/` on the shared volume:
```csharp
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/app/data/dp-keys"))
    .SetApplicationName("Translarr");
```

Both API and WebApp must share the same Data Protection keys and application name so cookies issued by the API are readable by the WebApp. Keys directory lives in the existing `translarr-db` volume.

### Cookie Domain

Login flow goes: Browser -> WebApp (POST) -> API (server-side) -> WebApp sets cookie in browser response. The cookie domain is the WebApp's domain (what the user sees in the browser). The API never sets cookies directly to the browser. This avoids cross-origin cookie issues entirely.

## Security Hardening

### CORS

Lock down from `AllowAnyOrigin()`. Blazor Server communicates with API server-side, so CORS can be restricted to the internal WebApp -> API URL. In Docker, this is `http://translarr-api:8080`.

### Rate Limiting

ASP.NET built-in rate limiter on `/api/auth/login`:
- Fixed window: 5 requests per minute per IP
- Returns `429 Too Many Requests`

### Forwarded Headers

Critical for correct client IP detection behind reverse proxy and Cloudflare Tunnel.

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = null; // Allow unlimited proxies in chain
    options.KnownNetworks.Clear(); // Trust all networks (required for Docker + CF)
    options.KnownProxies.Clear();  // Trust all proxies (required for Docker + CF)
});
```

`ForwardLimit = null` and clearing KnownNetworks/KnownProxies is necessary because:
- Docker bridge network means API sees the Docker gateway IP, not the real proxy IP
- Cloudflare adds its own `X-Forwarded-For` entry
- The chain is: Client -> Cloudflare -> Reverse Proxy -> Docker -> API

`UseForwardedHeaders()` must be called **before** `UseAuthentication()` and `UseRateLimiter()`.

### HTTPS Redirect Removal

Both `Core/Api/Program.cs` and `Frontend/HavitWebApp/Program.cs` currently have `app.UseHttpsRedirection()`. These must be **removed** - the app runs HTTP-only behind the proxy.

### Swagger Restriction

Current `Program.cs` has Swagger enabled unconditionally (the environment check is commented out). Fix: wrap Swagger in `if (app.Environment.IsDevelopment())` to prevent API surface exposure in production.

### CSRF

`SameSite: Strict` provides strong CSRF protection. Login endpoint is inherently CSRF-safe (no session to abuse). Authenticated POST endpoints (`/logout`, `/change-password`) are protected by `SameSite: Strict` preventing cross-origin cookie sending. Additional antiforgery tokens are not required but the login/setup forms should include them since they're standard Razor form POSTs.

## Service Registration

### DI Organization

Following existing pattern where `DependencyInjection.cs` handles all Infrastructure registration:

New extension method `AddAuthentication()` in `DependencyInjection.cs` (or `AuthenticationDependencyInjection.cs`) that handles:
- `AuthDbContext` registration with SQLite connection string
- `AddIdentity<IdentityUser>().AddEntityFrameworkStores<AuthDbContext>()`
- Identity options (password policy, lockout)
- Cookie authentication scheme configuration
- Data Protection configuration

`Program.cs` calls `builder.Services.AddTranslarrAuth(configuration)` and adds middleware:
```csharp
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
```

## Packages Used

Already in `Directory.Packages.props`:
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` - add to Infrastructure `.csproj`
- `Microsoft.AspNetCore.Authentication.JwtBearer` - **remove** (not needed for cookie auth)

## Files Changed/Created

### New Files
- `Core/Infrastructure/Persistence/AuthDbContext.cs`
- `Core/Api/Endpoints/AuthEndpoints.cs`
- `Frontend/HavitWebApp/Components/Pages/Login.razor` (static SSR, not interactive)
- `Frontend/HavitWebApp/Components/Pages/Setup.razor` (static SSR, not interactive)
- `Frontend/HavitWebApp/Auth/AuthCookieHolder.cs` (circuit-scoped service)
- `Frontend/HavitWebApp/Auth/CookieForwardingHandler.cs`
- `Frontend/HavitWebApp/Auth/TranslarrAuthStateProvider.cs`
- `Frontend/HavitWebApp/Components/Account/RedirectToLogin.razor`

### Modified Files
- `Core/Api/Program.cs` - forwarded headers, auth middleware, rate limiter, Swagger guard, remove UseHttpsRedirection
- `Core/Infrastructure/DependencyInjection.cs` - register AuthDbContext, Identity, cookie scheme, Data Protection
- `Core/Api/Endpoints/*` - add `.RequireAuthorization()` to all groups
- `Frontend/HavitWebApp/Program.cs` - add CookieForwardingHandler, AuthCookieHolder, AuthStateProvider, Data Protection, remove UseHttpsRedirection
- `Frontend/HavitWebApp/Components/App.razor` - CascadingAuthenticationState, AuthorizeRouteView with NotAuthorized template
- `Frontend/HavitWebApp/Components/Layout/MainLayout.razor` - logout button
- `Frontend/HavitWebApp/Components/Pages/Settings.razor` - change password section (or new SettingsPage partial)
- `AppHost/AppHost.cs` - second SQLite resource `translarr-auth`
- `compose.yaml` - init container handles both db files, new env var
- `env.example` - document new connection string
- `Directory.Packages.props` - remove JwtBearer package
