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

### Auth Flow

1. User opens app -> middleware checks cookie -> no cookie -> redirect to `/login`
2. User enters login + password -> POST `/api/auth/login`
3. API validates via Identity -> issues auth cookie -> redirect to dashboard
4. Subsequent requests carry cookie, middleware allows through
5. API endpoints have `RequireAuthorization()` - no cookie returns 401

### Setup Flow (First Launch)

1. App starts, checks if any user exists in auth database
2. No user -> frontend shows setup wizard instead of login
3. User sets login + password -> API creates admin account via Identity -> auto-login -> dashboard
4. Setup endpoint returns 404 from that point forward

## Data Layer

### Two Separate SQLite Databases

- **`TranslarrDbContext : DbContext`** - existing database (`translarr.db`), no changes to existing migrations
- **`AuthDbContext : IdentityDbContext<IdentityUser>`** - new database (`translarr-auth.db`), separate migrations

**Rationale:** Clean isolation. Reset auth by deleting one file without touching subtitle data. No Identity tables polluting the main database.

**Setup detection:** `UserManager.Users.AnyAsync() == false` means setup mode. No extra flags needed.

**Password change:** Via Settings page using `UserManager.ChangePasswordAsync()`.

### Docker Compose Changes

- `translarr-db-init`: creates and chmods both `translarr.db` and `translarr-auth.db` in the same volume
- `translarr-api`: new env var `ConnectionStrings__translarr-auth=Data Source=/app/data/translarr-auth.db`
- Same volume, same user (6969:6969), same permissions

## API Endpoints

### New `AuthEndpoints.cs`

| Endpoint | Method | Description | Auth |
|---|---|---|---|
| `/api/auth/setup/status` | GET | Returns `{ needsSetup: bool }` | Anonymous |
| `/api/auth/setup` | POST | Creates admin account (login + password) | Only when no users exist |
| `/api/auth/login` | POST | Login, issues cookie | Anonymous |
| `/api/auth/logout` | POST | Logout, clears cookie | Requires auth |
| `/api/auth/me` | GET | Returns current user info | Requires auth |
| `/api/auth/change-password` | POST | Change password (requires old password) | Requires auth |

### Security on Login Endpoint

- **Rate limiting:** ASP.NET built-in `RateLimiterOptions`, fixed window 5 requests/minute per IP, returns 429
- **Identity lockout:** 5 failed attempts -> account locked for 15 minutes
- **No info leakage:** Same "Invalid credentials" message regardless of whether login or password is wrong

### Security on Setup Endpoint

- Only works when `UserManager.Users.AnyAsync() == false`
- Returns 404 after account creation (permanently)

### Existing Endpoint Groups

All get `.RequireAuthorization()`:
- `/api/library/*`
- `/api/translation/*`
- `/api/settings/*`
- `/api/stats/*`
- `/api/series/*`

## Frontend (Blazor Server)

### New Pages

- **`/setup`** - Setup wizard. Fields: login, password, confirm password. Visible only when `setup/status` returns `needsSetup: true`. Success -> auto-login -> redirect to `/`.
- **`/login`** - Login form. Fields: login, password, "Remember me" checkbox (30 days vs session). Success -> redirect to original page or `/`.

### Auth Guard

- `AuthenticationStateProvider` + `CascadingAuthenticationState` in `App.razor`
- `<AuthorizeRouteView>` replaces `<RouteView>` - unauthenticated users redirected to `/login`
- `/login` and `/setup` marked with `@attribute [AllowAnonymous]`

### Cookie Forwarding

Blazor Server makes API requests server-side (from web server, not user's browser). Browser cookie doesn't automatically forward.

**Solution:** Custom `DelegatingHandler` in HTTP pipeline that extracts cookie from `IHttpContextAccessor` and attaches it to outgoing API requests.

### UI Changes

- **Logout button** in navbar (next to theme switcher). POST to `/api/auth/logout`, clear cookie, redirect to `/login`.
- **Change password** section on Settings page. Fields: old password, new password, confirm new password.

## Cookie Configuration

- `HttpOnly: true` - no JavaScript access
- `Secure: false` - app listens on HTTP behind reverse proxy; proxy handles Secure flag externally
- `SameSite: Strict` - no cross-origin CSRF
- `Name: .Translarr.Auth`
- **Expiration:** Session (browser close) by default; 30 days sliding with "Remember me"

## Security Hardening

### CORS

Lock down from `AllowAnyOrigin()`. Blazor Server communicates with API server-side, so CORS can be restricted to internal WebApp -> API URL or removed entirely for cookie auth.

### Rate Limiting

ASP.NET built-in rate limiter on `/api/auth/login`:
- Fixed window: 5 requests per minute per IP
- Returns `429 Too Many Requests`

### Identity Password Policy

- Minimum 8 characters
- No forced uppercase/digits/special characters (annoying, doesn't improve security)
- Identity defaults to PBKDF2 with 600k iterations

### Forwarded Headers

App behind reverse proxy must trust `X-Forwarded-For` and `X-Forwarded-Proto` for:
- Rate limiting to work on real client IP, not proxy IP
- Correct scheme detection

`app.UseForwardedHeaders()` configured in `Program.cs`.

### No HTTPS Redirect

Already handled: `ASPNETCORE_URLS=http://+:8080` in compose, no `UseHttpsRedirection()`. Works for both LAN HTTP access and HTTPS through tunnel.

## Packages Used

Already in `Directory.Packages.props` (currently unused):
- `Microsoft.AspNetCore.Authentication.JwtBearer` (not needed for cookie auth, can remove)
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (will be used)

Need to add to Infrastructure and Api `.csproj` files.

## Files Changed/Created

### New Files
- `Core/Infrastructure/Persistence/AuthDbContext.cs`
- `Core/Infrastructure/Migrations/Auth/` (new migration folder)
- `Core/Api/Endpoints/AuthEndpoints.cs`
- `Frontend/HavitWebApp/Components/Pages/Login.razor`
- `Frontend/HavitWebApp/Components/Pages/Setup.razor`
- `Frontend/HavitWebApp/Auth/CookieForwardingHandler.cs`

### Modified Files
- `Core/Api/Program.cs` - Identity config, cookie scheme, rate limiter, forwarded headers, auth middleware, CORS lockdown
- `Core/Infrastructure/DependencyInjection.cs` - register AuthDbContext, Identity services
- `Core/Api/Endpoints/*` - add `.RequireAuthorization()` to all groups
- `Frontend/HavitWebApp/Program.cs` - add CookieForwardingHandler, IHttpContextAccessor
- `Frontend/HavitWebApp/Components/App.razor` - CascadingAuthenticationState, AuthorizeRouteView
- `Frontend/HavitWebApp/Components/Layout/MainLayout.razor` - logout button
- `Frontend/HavitWebApp/Components/Pages/Settings.razor` - change password section
- `AppHost/AppHost.cs` - second SQLite resource for auth db
- `compose.yaml` - init container creates both db files, new env var
- `env.example` - document new connection string
