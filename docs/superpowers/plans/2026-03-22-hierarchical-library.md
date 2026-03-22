# Hierarchical Library Navigation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace flat library grid + separate series management with hierarchical navigation: dynamic library menu → series list → series detail with expandable seasons.

**Architecture:** New `Library` column on `SubtitleEntries` auto-populated from first path segment. New `/api/library/browse` endpoints for hierarchical queries. Two new Blazor pages (`LibraryBrowser`, `SeriesDetail`) with dynamic NavMenu. Existing "All Entries" view preserved at `/library/all`.

**Tech Stack:** .NET 10, EF Core + SQLite, Blazor Server (Havit), MudBlazor-free (uses Havit HxGrid), ErrorOr pattern, Clean Architecture.

**Spec:** `docs/superpowers/specs/2026-03-22-hierarchical-library-design.md`

**Worktree:** `.worktrees/hierarchical-library` on branch `feature/hierarchical-library`

**No test project exists** — verification via `dotnet build` + manual testing with Aspire (`cd AppHost && dotnet run`).

### Codebase Conventions (READ FIRST)

- **Primary constructors** — all services/repos use `class Foo(IDep dep) : IFoo` syntax. Parameters accessed directly (e.g. `repository`, `unitOfWork`), NOT as `_repository`.
- **DbContext access** — `SubtitleEntryRepository` uses `context.SubtitleEntries` (typed DbSet), NOT `context.Set<SubtitleEntryDao>()`.
- **ErrorOr pattern** — endpoints use `if (result.IsError) return ErrorTypeMapper.MapErrorsToProblemResponse(result);` pattern, NOT `.Match()`.
- **Error factory** — two-param: `Error.Validation("ServiceName.Method", "Description")`, `Error.NotFound("ServiceName.Method", "Description")`.
- **Frontend API** — `apiClientFactory.CreateClient()` (synchronous, no underscore prefix).
- **Endpoint registration** — use `[FromServices]`, `[FromQuery]`, `.Produces<T>()` annotations.

---

## File Map

### Files to Create

| File | Responsibility |
|------|---------------|
| `Core/Application/Models/SeasonGroupDto.cs` | Extracted from SeriesGroupDto.cs (one-file-per-class) |
| `Core/Application/Models/SeriesDetailDto.cs` | Series detail with seasons + entries |
| `Core/Application/Models/SeasonDetailDto.cs` | Season detail with entries list |
| `Frontend/HavitWebApp/Helpers/SlugHelper.cs` | Shared slug generation utility |
| `Core/Infrastructure/Persistence/Migrations/<AddLibraryColumn>.cs` | EF Core migration (auto-generated) |
| `Frontend/HavitWebApp/Components/Pages/LibraryBrowser.razor` | Per-library series list page |
| `Frontend/HavitWebApp/Components/Pages/SeriesDetail.razor` | Per-series season/episode detail page |

### Files to Modify

| File | Changes |
|------|---------|
| `Core/Infrastructure/Persistence/Daos/SubtitleEntryDao.cs` | Add `Library` property |
| `Core/Infrastructure/Persistence/Configurations/SubtitleEntryConfiguration.cs` | Configure `Library` column + composite index |
| `Core/Application/Models/SubtitleEntryDto.cs` | Add `Library` property |
| `Core/Application/Models/VideoFile.cs` | Add `Library` property |
| `Core/Application/Models/SeriesGroupDto.cs` | Remove `SeasonGroupDto` (moved to own file) |
| `Core/Application/Abstractions/Repositories/ISubtitleEntryRepository.cs` | Add 3 new methods, modify `BulkUpdateWantedAsync` |
| `Core/Infrastructure/Repositories/SubtitleEntryRepository.cs` | Implement new methods, update MapToDto/MapToDao/UpdateAsync |
| `Core/Application/Abstractions/Services/ILibraryService.cs` | Add 3 new methods, modify `BulkSetWantedAsync` |
| `Core/Application/Services/LibraryService.cs` | Implement new methods, add ISeriesWatchService dependency |
| `Core/Application/Abstractions/Services/ISeriesWatchService.cs` | Add `library` param to `GetSeriesGroupsWithWatchStatusAsync` |
| `Core/Application/Services/SeriesWatchService.cs` | Filter by library in `GetSeriesGroupsWithWatchStatusAsync` |
| `Core/Application/Services/MediaScannerService.cs` | Extract `Library` from path in `ScanFilesystemAsync` + `AnalyzeVideoFilesAsync` |
| `Core/Api/Endpoints/LibraryEndpoints.cs` | Add 3 new browse endpoints, modify bulk wanted |
| `Frontend/HavitWebApp/Services/LibraryApiService.cs` | Add 3 new methods |
| `Frontend/HavitWebApp/Components/Layout/NavMenu.razor` | Dynamic library submenu |
| `Frontend/HavitWebApp/Components/Pages/Library.razor` | Change route to `/library/all` |
| `Frontend/HavitWebApp/Components/Pages/SeriesManagement.razor` | Delete this file |

---

## Task 1: Add `Library` to Data Model

**Files:**
- Modify: `Core/Infrastructure/Persistence/Daos/SubtitleEntryDao.cs:5`
- Modify: `Core/Application/Models/SubtitleEntryDto.cs:5`
- Modify: `Core/Application/Models/VideoFile.cs:8`
- Modify: `Core/Infrastructure/Persistence/Configurations/SubtitleEntryConfiguration.cs:14,54`

- [ ] **Step 1: Add `Library` to `SubtitleEntryDao`**

In `SubtitleEntryDao.cs`, add after `public int Id { get; set; }` (line 5):

```csharp
public string Library { get; set; } = "";
```

- [ ] **Step 2: Add `Library` to `SubtitleEntryDto`**

In `SubtitleEntryDto.cs`, add after `public int Id { get; set; }` (line 5):

```csharp
public string Library { get; set; } = "";
```

- [ ] **Step 3: Add `Library` to `VideoFile`**

In `VideoFile.cs`, add after `SeasonNumber` (line 8):

```csharp
public required string Library { get; set; }
```

- [ ] **Step 4: Configure `Library` column in EF Core**

In `SubtitleEntryConfiguration.cs`, add after the `Id` configuration (around line 14):

```csharp
builder.Property(e => e.Library)
    .IsRequired()
    .HasMaxLength(256)
    .HasDefaultValue("");
```

Add composite index (keep existing `{Series, Season}` index, add new one):

```csharp
builder.HasIndex(e => new { e.Library, e.Series, e.Season });
```

- [ ] **Step 5: Generate EF Core migration**

```bash
cd Core/Infrastructure && dotnet ef migrations add AddLibraryColumn --startup-project ../Api
```

- [ ] **Step 6: Verify build**

```bash
dotnet build --configuration Release
```

- [ ] **Step 7: Commit**

```bash
git add Core/Infrastructure/Persistence/ Core/Application/Models/
git commit -m "feat: add Library column to SubtitleEntries data model"
```

---

## Task 2: Split `SeriesGroupDto.cs` + Create New DTOs

**Files:**
- Modify: `Core/Application/Models/SeriesGroupDto.cs`
- Create: `Core/Application/Models/SeasonGroupDto.cs`
- Create: `Core/Application/Models/SeriesDetailDto.cs`
- Create: `Core/Application/Models/SeasonDetailDto.cs`

- [ ] **Step 1: Extract `SeasonGroupDto` to its own file**

The current `SeriesGroupDto.cs` contains both classes. Create `Core/Application/Models/SeasonGroupDto.cs`:

```csharp
namespace Translarr.Core.Application.Models;

public class SeasonGroupDto
{
    public required string SeasonName { get; set; }
    public int TotalFiles { get; set; }
    public int WantedFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public bool IsWatched { get; set; }
}
```

Remove the `SeasonGroupDto` class from `SeriesGroupDto.cs` (keep only `SeriesGroupDto`).

- [ ] **Step 2: Create `SeriesDetailDto.cs`**

```csharp
namespace Translarr.Core.Application.Models;

public class SeriesDetailDto
{
    public string SeriesName { get; set; } = "";
    public bool IsWatched { get; set; }
    public List<SeasonDetailDto> Seasons { get; set; } = [];
}
```

- [ ] **Step 3: Create `SeasonDetailDto.cs`**

```csharp
namespace Translarr.Core.Application.Models;

public class SeasonDetailDto
{
    public string SeasonName { get; set; } = "";
    public bool IsWatched { get; set; }
    public int TotalFiles { get; set; }
    public int WantedFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public List<SubtitleEntryDto> Entries { get; set; } = [];
}
```

- [ ] **Step 4: Verify build + Commit**

```bash
dotnet build --configuration Release
git add Core/Application/Models/
git commit -m "refactor: split SeasonGroupDto to own file, add SeriesDetailDto and SeasonDetailDto"
```

---

## Task 3: Update Repository — Mapping + New Methods

**Files:**
- Modify: `Core/Application/Abstractions/Repositories/ISubtitleEntryRepository.cs`
- Modify: `Core/Infrastructure/Repositories/SubtitleEntryRepository.cs`

- [ ] **Step 1: Update `MapToDto` (line 236-254)**

Add after `Id = dao.Id,`:

```csharp
Library = dao.Library,
```

- [ ] **Step 2: Update `MapToDao` (line 256-274)**

Add after `Id = dto.Id,`:

```csharp
Library = dto.Library,
```

- [ ] **Step 3: Update `UpdateAsync` (line 55-78)**

Add in the property copy block (after `dao.Series = entry.Series;`):

```csharp
dao.Library = entry.Library;
```

- [ ] **Step 4: Add new methods to `ISubtitleEntryRepository.cs`**

```csharp
Task<List<string>> GetDistinctLibrariesAsync();
Task<List<SeriesGroupDto>> GetSeriesGroupsByLibraryAsync(string library);
Task<List<SubtitleEntryDto>> GetEntriesByLibraryAndSeriesAsync(string library, string series);
```

- [ ] **Step 5: Modify `BulkUpdateWantedAsync` signature in interface**

```csharp
Task<int> BulkUpdateWantedAsync(string seriesName, string? seasonName, bool isWanted, string? library = null);
```

- [ ] **Step 6: Implement new methods in `SubtitleEntryRepository.cs`**

Add before `MapToDto` method. Note: uses `context.SubtitleEntries` (NOT `_context.Set<>()`):

```csharp
public async Task<List<string>> GetDistinctLibrariesAsync()
{
    return await context.SubtitleEntries
        .AsNoTracking()
        .Select(e => e.Library)
        .Where(l => l != "")
        .Distinct()
        .OrderBy(l => l)
        .ToListAsync();
}

public async Task<List<SeriesGroupDto>> GetSeriesGroupsByLibraryAsync(string library)
{
    var groupedData = await context.SubtitleEntries
        .AsNoTracking()
        .Where(e => e.Library == library)
        .GroupBy(e => new { e.Series, e.Season })
        .Select(g => new
        {
            g.Key.Series,
            g.Key.Season,
            TotalFiles = g.Count(),
            WantedFiles = g.Count(e => e.IsWanted),
            ProcessedFiles = g.Count(e => e.IsProcessed)
        })
        .ToListAsync();

    return groupedData
        .GroupBy(g => g.Series)
        .Select(sg => new SeriesGroupDto
        {
            SeriesName = sg.Key,
            TotalFiles = sg.Sum(s => s.TotalFiles),
            WantedFiles = sg.Sum(s => s.WantedFiles),
            ProcessedFiles = sg.Sum(s => s.ProcessedFiles),
            Seasons = sg.Select(s => new SeasonGroupDto
            {
                SeasonName = s.Season,
                TotalFiles = s.TotalFiles,
                WantedFiles = s.WantedFiles,
                ProcessedFiles = s.ProcessedFiles
            }).OrderBy(s => s.SeasonName).ToList()
        })
        .OrderBy(s => s.SeriesName)
        .ToList();
}

public async Task<List<SubtitleEntryDto>> GetEntriesByLibraryAndSeriesAsync(string library, string series)
{
    var entries = await context.SubtitleEntries
        .AsNoTracking()
        .Where(e => e.Library == library && e.Series == series)
        .OrderBy(e => e.Season)
        .ThenBy(e => e.FileName)
        .ToListAsync();

    return entries.Select(MapToDto).ToList();
}
```

- [ ] **Step 7: Modify `BulkUpdateWantedAsync` implementation (line 185-194)**

```csharp
public async Task<int> BulkUpdateWantedAsync(string seriesName, string? seasonName, bool isWanted, string? library = null)
{
    var query = context.SubtitleEntries.Where(e => e.Series == seriesName);

    if (library != null)
        query = query.Where(e => e.Library == library);

    if (seasonName != null)
        query = query.Where(e => e.Season == seasonName);

    return await query.ExecuteUpdateAsync(setters =>
        setters.SetProperty(e => e.IsWanted, isWanted));
}
```

- [ ] **Step 8: Verify build + Commit**

```bash
dotnet build --configuration Release
git add Core/Application/Abstractions/Repositories/ Core/Infrastructure/Repositories/
git commit -m "feat: add library-aware repository methods and mapping"
```

---

## Task 4: Update `MediaScannerService`

**Files:**
- Modify: `Core/Application/Services/MediaScannerService.cs:52-194`

- [ ] **Step 1: Update `ScanFilesystemAsync` — add Library to VideoFile construction**

In the `pathParts.Length < 3` block (line 76-88), change the VideoFile construction to include Library:

```csharp
videoFiles.Add(new VideoFile
{
    FilePath = filePath,
    FileName = Path.GetFileName(filePath),
    SeriesNumber = seriesName,
    SeasonNumber = seasonName,
    Library = pathParts.Length >= 2 ? pathParts[0] : "Uncategorized"
});
```

In the `else` block (line 90-104), add Library:

```csharp
videoFiles.Add(new VideoFile
{
    FilePath = filePath,
    FileName = Path.GetFileName(filePath),
    SeriesNumber = seriesName,
    SeasonNumber = seasonName,
    Library = pathParts[0]
});
```

- [ ] **Step 2: Update `AnalyzeVideoFilesAsync` — propagate Library**

In the new entry creation (around line 137-148), add to the `SubtitleEntryDto` initializer:

```csharp
Library = videoFile.Library,
```

For existing entries update block (around line 155-168), add:

```csharp
existingEntry.Library = videoFile.Library;
```

- [ ] **Step 3: Verify build + Commit**

```bash
dotnet build --configuration Release
git add Core/Application/Services/MediaScannerService.cs
git commit -m "feat: extract Library from path in media scanner"
```

---

## Task 5: Update Services

**Files:**
- Modify: `Core/Application/Abstractions/Services/ILibraryService.cs`
- Modify: `Core/Application/Services/LibraryService.cs`
- Modify: `Core/Application/Abstractions/Services/ISeriesWatchService.cs`
- Modify: `Core/Application/Services/SeriesWatchService.cs`

- [ ] **Step 1: Add new methods to `ILibraryService.cs`**

```csharp
Task<ErrorOr<List<string>>> GetLibrariesAsync();
Task<ErrorOr<List<SeriesGroupDto>>> GetSeriesByLibraryAsync(string library);
Task<ErrorOr<SeriesDetailDto>> GetSeriesDetailAsync(string library, string series);
```

- [ ] **Step 2: Modify `BulkSetWantedAsync` in `ILibraryService.cs`**

Change to:
```csharp
Task<ErrorOr<int>> BulkSetWantedAsync(string seriesName, string? seasonName, bool isWanted, string? library = null);
```

- [ ] **Step 3: Update `LibraryService.cs` — add dependency + implement new methods**

Add `ISeriesWatchService` to primary constructor:

```csharp
public class LibraryService(
    ISubtitleEntryRepository repository,
    IUnitOfWork unitOfWork,
    ISeriesWatchService seriesWatchService) : ILibraryService
```

Add implementations (note: no underscore prefixes on params):

```csharp
public async Task<ErrorOr<List<string>>> GetLibrariesAsync()
{
    var libraries = await repository.GetDistinctLibrariesAsync();
    return libraries;
}

public async Task<ErrorOr<List<SeriesGroupDto>>> GetSeriesByLibraryAsync(string library)
{
    if (string.IsNullOrWhiteSpace(library))
        return Error.Validation("LibraryService.GetSeriesByLibraryAsync", "Library name is required.");

    var groups = await repository.GetSeriesGroupsByLibraryAsync(library);
    return groups;
}

public async Task<ErrorOr<SeriesDetailDto>> GetSeriesDetailAsync(string library, string series)
{
    if (string.IsNullOrWhiteSpace(library))
        return Error.Validation("LibraryService.GetSeriesDetailAsync", "Library name is required.");
    if (string.IsNullOrWhiteSpace(series))
        return Error.Validation("LibraryService.GetSeriesDetailAsync", "Series name is required.");

    var entries = await repository.GetEntriesByLibraryAndSeriesAsync(library, series);
    if (entries.Count == 0)
        return Error.NotFound("LibraryService.GetSeriesDetailAsync", "Series not found in library.");

    var isSeriesWatched = await seriesWatchService.ShouldAutoMarkWantedAsync(series, "");

    var seasons = new List<SeasonDetailDto>();
    foreach (var seasonGroup in entries.GroupBy(e => e.Season).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
    {
        var isSeasonWatched = isSeriesWatched ||
            await seriesWatchService.ShouldAutoMarkWantedAsync(series, seasonGroup.Key);

        seasons.Add(new SeasonDetailDto
        {
            SeasonName = seasonGroup.Key,
            TotalFiles = seasonGroup.Count(),
            WantedFiles = seasonGroup.Count(e => e.IsWanted),
            ProcessedFiles = seasonGroup.Count(e => e.IsProcessed),
            IsWatched = isSeasonWatched,
            Entries = seasonGroup.OrderBy(e => e.FileName).ToList()
        });
    }

    return new SeriesDetailDto
    {
        SeriesName = series,
        IsWatched = isSeriesWatched,
        Seasons = seasons
    };
}
```

- [ ] **Step 4: Update `BulkSetWantedAsync` in `LibraryService.cs` (line 70-77)**

```csharp
public async Task<ErrorOr<int>> BulkSetWantedAsync(string seriesName, string? seasonName, bool isWanted, string? library = null)
{
    if (string.IsNullOrWhiteSpace(seriesName))
        return Error.Validation("LibraryService.BulkSetWantedAsync", "Series name cannot be empty.");

    var updatedCount = await repository.BulkUpdateWantedAsync(seriesName, seasonName, isWanted, library);
    return updatedCount;
}
```

- [ ] **Step 5: Modify `ISeriesWatchService.cs`**

Change:
```csharp
Task<List<SeriesGroupDto>> GetSeriesGroupsWithWatchStatusAsync(string? library = null);
```

- [ ] **Step 6: Update `SeriesWatchService.cs` (line 73-111)**

```csharp
public async Task<List<SeriesGroupDto>> GetSeriesGroupsWithWatchStatusAsync(string? library = null)
{
    var seriesGroups = library != null
        ? await subtitleEntryRepository.GetSeriesGroupsByLibraryAsync(library)
        : await subtitleEntryRepository.GetSeriesGroupsAsync();

    var watchConfigs = await watchConfigRepository.GetAllAsync();

    // ... rest of existing watch status enrichment logic unchanged (lines 78-110) ...
```

Only the first line changes. Everything from `var watchConfigs` onwards stays exactly as-is.

- [ ] **Step 7: Verify build + Commit**

```bash
dotnet build --configuration Release
git add Core/Application/ Core/Api/Endpoints/SeriesWatchEndpoints.cs
git commit -m "feat: add library-aware service methods"
```

---

## Task 6: Add API Browse Endpoints

**Files:**
- Modify: `Core/Api/Endpoints/LibraryEndpoints.cs`

- [ ] **Step 1: Add new routes in `MapLibraryEndpoints` (after line 48)**

```csharp
group.MapGet("/browse", GetLibraries)
    .WithName("GetLibraries")
    .Produces<List<string>>();

group.MapGet("/browse/series", GetSeriesByLibrary)
    .WithName("GetSeriesByLibrary")
    .Produces<List<SeriesGroupDto>>();

group.MapGet("/browse/series/detail", GetSeriesDetail)
    .WithName("GetSeriesDetail")
    .Produces<SeriesDetailDto>()
    .Produces(StatusCodes.Status404NotFound);
```

- [ ] **Step 2: Implement handler methods**

```csharp
private static async Task<IResult> GetLibraries([FromServices] ILibraryService libraryService)
{
    var result = await libraryService.GetLibrariesAsync();

    if (result.IsError)
        return ErrorTypeMapper.MapErrorsToProblemResponse(result);

    return Results.Ok(result.Value);
}

private static async Task<IResult> GetSeriesByLibrary(
    [FromQuery] string library,
    [FromServices] ISeriesWatchService seriesWatchService)
{
    var result = await seriesWatchService.GetSeriesGroupsWithWatchStatusAsync(library);
    return Results.Ok(result);
}

private static async Task<IResult> GetSeriesDetail(
    [FromQuery] string library,
    [FromQuery] string series,
    [FromServices] ILibraryService libraryService)
{
    var result = await libraryService.GetSeriesDetailAsync(library, series);

    if (result.IsError)
        return ErrorTypeMapper.MapErrorsToProblemResponse(result);

    return Results.Ok(result.Value);
}
```

Add `using Translarr.Core.Application.Abstractions.Services;` if `ISeriesWatchService` is not already imported.

- [ ] **Step 3: Modify `BulkUpdateWantedStatus` handler (line 180-192)**

Add `library` parameter:

```csharp
private static async Task<IResult> BulkUpdateWantedStatus(
    [FromQuery] string series,
    [FromQuery] string? season,
    [FromQuery] bool wanted,
    [FromQuery] string? library,
    [FromServices] ILibraryService service)
{
    var result = await service.BulkSetWantedAsync(series, season, wanted, library);

    if (result.IsError)
        return ErrorTypeMapper.MapErrorsToProblemResponse(result);

    return Results.Ok(new BulkUpdateResult(result.Value));
}
```

- [ ] **Step 4: Verify build + Commit**

```bash
dotnet build --configuration Release
git add Core/Api/Endpoints/LibraryEndpoints.cs
git commit -m "feat: add /api/library/browse endpoints for hierarchical navigation"
```

---

## Task 7: Create `SlugHelper` + Update Frontend API Service

**Files:**
- Create: `Frontend/HavitWebApp/Helpers/SlugHelper.cs`
- Modify: `Frontend/HavitWebApp/Services/LibraryApiService.cs`

- [ ] **Step 1: Create `SlugHelper.cs`**

```csharp
namespace Translarr.Frontend.HavitWebApp.Helpers;

public static class SlugHelper
{
    public static string ToSlug(string name)
    {
        return name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("'", "")
            .Replace(".", "");
    }

    /// <summary>
    /// Resolves a slug back to the original name from a list of known names.
    /// Returns the slug itself if no match found.
    /// </summary>
    public static string FromSlug(string slug, IEnumerable<string> knownNames)
    {
        return knownNames.FirstOrDefault(n => ToSlug(n) == slug) ?? slug;
    }
}
```

- [ ] **Step 2: Add new methods to `LibraryApiService.cs`**

Uses `apiClientFactory.CreateClient()` (synchronous, no underscore):

```csharp
public async Task<List<string>?> GetLibrariesAsync()
{
    var client = apiClientFactory.CreateClient();
    var response = await client.GetAsync("/api/library/browse");
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<List<string>>();
}

public async Task<List<SeriesGroupDto>?> GetSeriesByLibraryAsync(string libraryName)
{
    var client = apiClientFactory.CreateClient();
    var encodedLibrary = Uri.EscapeDataString(libraryName);
    var response = await client.GetAsync($"/api/library/browse/series?library={encodedLibrary}");
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<List<SeriesGroupDto>>();
}

public async Task<SeriesDetailDto?> GetSeriesDetailAsync(string libraryName, string seriesName)
{
    var client = apiClientFactory.CreateClient();
    var encodedLibrary = Uri.EscapeDataString(libraryName);
    var encodedSeries = Uri.EscapeDataString(seriesName);
    var response = await client.GetAsync(
        $"/api/library/browse/series/detail?library={encodedLibrary}&series={encodedSeries}");
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<SeriesDetailDto>();
}
```

- [ ] **Step 3: Verify build + Commit**

```bash
dotnet build --configuration Release
git add Frontend/HavitWebApp/Helpers/ Frontend/HavitWebApp/Services/LibraryApiService.cs
git commit -m "feat: add SlugHelper and library browse methods to LibraryApiService"
```

---

## Task 8: Dynamic NavMenu

**Files:**
- Modify: `Frontend/HavitWebApp/Components/Layout/NavMenu.razor`

- [ ] **Step 1: Rewrite NavMenu with dynamic library submenu**

Current NavMenu is pure static HTML (no `@code` block). Add `LibraryApiService` injection and dynamic rendering.

Replace the Library and Series Management `<NavLink>`s with:

```razor
@inject LibraryApiService LibraryApiService
@using Translarr.Frontend.HavitWebApp.Helpers
```

```razor
<div class="nav-item px-3">
    <NavLink class="nav-link" href="library/all" Match="NavLinkMatch.All">
        <span class="bi bi-book-half" aria-hidden="true"></span> All Entries
    </NavLink>
</div>

@if (_libraries != null)
{
    @foreach (var library in _libraries)
    {
        var slug = SlugHelper.ToSlug(library);
        <div class="nav-item px-3 ps-4">
            <NavLink class="nav-link" href="@($"library/{slug}")" Match="NavLinkMatch.Prefix">
                <span class="bi bi-folder" aria-hidden="true"></span> @library
            </NavLink>
        </div>
    }
}
```

Remove the old Series Management nav link. Add `@code` block:

```csharp
@code {
    private List<string>? _libraries;

    protected override async Task OnInitializedAsync()
    {
        await LoadLibraries();
    }

    public async Task RefreshLibraries()
    {
        await LoadLibraries();
        StateHasChanged();
    }

    private async Task LoadLibraries()
    {
        try
        {
            _libraries = await LibraryApiService.GetLibrariesAsync();
        }
        catch
        {
            _libraries = null;
        }
    }
}
```

- [ ] **Step 2: Verify build + Commit**

```bash
dotnet build --configuration Release
git add Frontend/HavitWebApp/Components/Layout/NavMenu.razor
git commit -m "feat: dynamic library submenu in NavMenu"
```

---

## Task 9: Move Library.razor to `/library/all` + Delete SeriesManagement

**Files:**
- Modify: `Frontend/HavitWebApp/Components/Pages/Library.razor:1`
- Delete: `Frontend/HavitWebApp/Components/Pages/SeriesManagement.razor`

- [ ] **Step 1: Change route in Library.razor**

Change `@page "/library"` to `@page "/library/all"`.

- [ ] **Step 2: Delete SeriesManagement.razor**

```bash
rm Frontend/HavitWebApp/Components/Pages/SeriesManagement.razor
```

- [ ] **Step 3: Verify build + Commit**

```bash
dotnet build --configuration Release
git add Frontend/HavitWebApp/Components/Pages/Library.razor
git add -u Frontend/HavitWebApp/Components/Pages/SeriesManagement.razor
git commit -m "refactor: move Library page to /library/all, remove SeriesManagement"
```

---

## Task 10: Create `LibraryBrowser.razor`

**Files:**
- Create: `Frontend/HavitWebApp/Components/Pages/LibraryBrowser.razor`

- [ ] **Step 1: Create the page**

This page shows series for a library. For flat libraries (all entries have Series == Season or one distinct Series), shows a flat file table instead.

```razor
@page "/library/{LibrarySlug}"
@inject LibraryApiService LibraryApiService
@inject NavigationManager NavigationManager
@using Translarr.Core.Application.Models
@using Translarr.Frontend.HavitWebApp.Helpers

<PageTitle>@_libraryName</PageTitle>

<h2>@_libraryName</h2>

@if (_isLoading)
{
    <div class="text-center my-5">
        <div class="spinner-border" role="status">
            <span class="visually-hidden">Loading...</span>
        </div>
    </div>
}
else if (_error)
{
    <div class="alert alert-danger">
        <p>Failed to load library.</p>
        <button class="btn btn-outline-danger btn-sm" @onclick="LoadData">Retry</button>
    </div>
}
else if (_seriesGroups == null || _seriesGroups.Count == 0)
{
    <div class="alert alert-info">No media found in this library.</div>
}
else if (_isFlat)
{
    @* Flat library (e.g. Movies) — single series, show all entries directly *@
    <p class="text-muted">@_seriesGroups[0].TotalFiles files &middot; @_seriesGroups[0].ProcessedFiles translated</p>
    @* TODO: Integrate flat file table (reuse Library.razor grid pattern with library filter) *@
    <div class="alert alert-info">
        Flat library view — use <a href="/library/all">All Entries</a> filtered view for now.
    </div>
}
else
{
    <div class="row">
        @foreach (var series in _seriesGroups)
        {
            var seriesSlug = SlugHelper.ToSlug(series.SeriesName);
            <div class="col-md-4 mb-3">
                <div class="card h-100" style="cursor: pointer"
                     @onclick="() => NavigateToSeries(seriesSlug)">
                    <div class="card-body">
                        <h5 class="card-title">@series.SeriesName</h5>
                        <p class="card-text text-muted">
                            @series.TotalFiles files
                            &middot; @series.ProcessedFiles translated
                            @if (series.IsWatched)
                            {
                                <span class="badge bg-success ms-2">Watched</span>
                            }
                        </p>
                        @{
                            var pct = series.TotalFiles > 0
                                ? (int)(100.0 * series.ProcessedFiles / series.TotalFiles)
                                : 0;
                        }
                        <div class="progress" style="height: 6px">
                            <div class="progress-bar bg-success" style="width: @(pct)%"></div>
                        </div>
                    </div>
                </div>
            </div>
        }
    </div>
}

@code {
    [Parameter] public string LibrarySlug { get; set; } = "";

    private string _libraryName = "";
    private List<SeriesGroupDto>? _seriesGroups;
    private bool _isFlat;
    private bool _isLoading = true;
    private bool _error;

    protected override async Task OnParametersSetAsync()
    {
        await LoadData();
    }

    private async Task LoadData()
    {
        _isLoading = true;
        _error = false;
        StateHasChanged();

        try
        {
            var libraries = await LibraryApiService.GetLibrariesAsync();
            _libraryName = SlugHelper.FromSlug(LibrarySlug, libraries ?? []);

            _seriesGroups = await LibraryApiService.GetSeriesByLibraryAsync(_libraryName);

            // Flat library detection: single series, or all seasons match series name
            _isFlat = _seriesGroups != null && _seriesGroups.Count == 1 &&
                _seriesGroups[0].Seasons.All(s => s.SeasonName == _seriesGroups[0].SeriesName);
        }
        catch
        {
            _error = true;
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private void NavigateToSeries(string seriesSlug)
    {
        NavigationManager.NavigateTo($"/library/{LibrarySlug}/{seriesSlug}");
    }
}
```

- [ ] **Step 2: Verify build + Commit**

```bash
dotnet build --configuration Release
git add Frontend/HavitWebApp/Components/Pages/LibraryBrowser.razor
git commit -m "feat: add LibraryBrowser page with flat library detection"
```

---

## Task 11: Create `SeriesDetail.razor`

**Files:**
- Create: `Frontend/HavitWebApp/Components/Pages/SeriesDetail.razor`

- [ ] **Step 1: Create the page**

Study `SeriesManagement.razor` for expand/collapse and `Library.razor` for file table patterns before writing.

```razor
@page "/library/{LibrarySlug}/{SeriesSlug}"
@inject LibraryApiService LibraryApiService
@inject SeriesWatchApiService SeriesWatchApiService
@inject NavigationManager NavigationManager
@using Translarr.Core.Application.Models
@using Translarr.Frontend.HavitWebApp.Helpers

<PageTitle>@_seriesName</PageTitle>

<nav aria-label="breadcrumb">
    <ol class="breadcrumb">
        <li class="breadcrumb-item">
            <a href="/library/@LibrarySlug">@_libraryName</a>
        </li>
        <li class="breadcrumb-item active">@_seriesName</li>
    </ol>
</nav>

<h2>@_seriesName</h2>

@if (_isLoading)
{
    <div class="text-center my-5">
        <div class="spinner-border" role="status">
            <span class="visually-hidden">Loading...</span>
        </div>
    </div>
}
else if (_error)
{
    <div class="alert alert-danger">
        <p>Failed to load series.</p>
        <button class="btn btn-outline-danger btn-sm" @onclick="LoadData">Retry</button>
    </div>
}
else if (_detail == null)
{
    <div class="alert alert-info">Series not found.</div>
}
else
{
    <!-- Management Panel -->
    <div class="card mb-4">
        <div class="card-body d-flex align-items-center gap-3">
            <div class="form-check form-switch">
                <input class="form-check-input" type="checkbox" id="autoWatch"
                       checked="@_detail.IsWatched"
                       @onchange="ToggleSeriesWatch" />
                <label class="form-check-label" for="autoWatch">Auto-watch</label>
            </div>
            <button class="btn btn-sm btn-outline-primary"
                    @onclick="() => BulkMarkWanted(true)">
                Mark All Wanted
            </button>
            <button class="btn btn-sm btn-outline-secondary"
                    @onclick="() => BulkMarkWanted(false)">
                Mark All Unwanted
            </button>
        </div>
    </div>

    <!-- Seasons -->
    @foreach (var season in _detail.Seasons)
    {
        var isExpanded = _expandedSeasons.Contains(season.SeasonName);
        <div class="card mb-2">
            <div class="card-header d-flex align-items-center"
                 style="cursor: pointer"
                 @onclick="() => ToggleExpanded(season.SeasonName)">
                <span class="me-2">@(isExpanded ? "▼" : "▶")</span>
                <strong class="me-auto">@season.SeasonName</strong>
                <span class="text-muted me-3">
                    @season.ProcessedFiles/@season.TotalFiles translated
                </span>
                @if (season.IsWatched)
                {
                    <span class="badge bg-success me-2">Watched</span>
                }
                <button class="btn btn-sm btn-outline-primary me-1"
                        @onclick:stopPropagation="true"
                        @onclick="() => BulkMarkSeasonWanted(season.SeasonName, true)">
                    Wanted
                </button>
                <button class="btn btn-sm btn-outline-secondary"
                        @onclick:stopPropagation="true"
                        @onclick="() => BulkMarkSeasonWanted(season.SeasonName, false)">
                    Unwanted
                </button>
            </div>

            @if (isExpanded)
            {
                <div class="card-body p-0">
                    <table class="table table-sm table-hover mb-0">
                        <thead>
                            <tr>
                                <th>File Name</th>
                                <th>Status</th>
                                <th>Wanted</th>
                                <th>Already Has</th>
                                <th>Last Scanned</th>
                            </tr>
                        </thead>
                        <tbody>
                            @foreach (var entry in season.Entries)
                            {
                                <tr>
                                    <td>@entry.FileName</td>
                                    <td>
                                        @if (entry.IsProcessed)
                                        {
                                            <span class="badge bg-success">Processed</span>
                                        }
                                        else if (!string.IsNullOrEmpty(entry.ErrorMessage))
                                        {
                                            <span class="badge bg-danger" title="@entry.ErrorMessage">Error</span>
                                        }
                                        else if (entry.HasBitmapSubtitlesOnly)
                                        {
                                            <span class="badge bg-info">Bitmap</span>
                                        }
                                        else if (entry.IsWanted)
                                        {
                                            <span class="badge bg-warning">Pending</span>
                                        }
                                        else
                                        {
                                            <span class="badge bg-secondary">Idle</span>
                                        }
                                    </td>
                                    <td>
                                        <div class="form-check form-switch">
                                            <input class="form-check-input" type="checkbox"
                                                   checked="@entry.IsWanted"
                                                   @onchange="() => ToggleWanted(entry)" />
                                        </div>
                                    </td>
                                    <td>
                                        @if (entry.AlreadyHad)
                                        {
                                            <span class="text-success">✓</span>
                                        }
                                    </td>
                                    <td>@entry.LastScanned.ToString("g")</td>
                                </tr>
                            }
                        </tbody>
                    </table>
                </div>
            }
        </div>
    }
}

@code {
    [Parameter] public string LibrarySlug { get; set; } = "";
    [Parameter] public string SeriesSlug { get; set; } = "";

    private string _libraryName = "";
    private string _seriesName = "";
    private SeriesDetailDto? _detail;
    private HashSet<string> _expandedSeasons = new();
    private bool _isLoading = true;
    private bool _error;

    protected override async Task OnParametersSetAsync()
    {
        await LoadData();
    }

    private async Task LoadData()
    {
        _isLoading = true;
        _error = false;
        StateHasChanged();

        try
        {
            // Resolve library name from slug
            var libraries = await LibraryApiService.GetLibrariesAsync();
            _libraryName = SlugHelper.FromSlug(LibrarySlug, libraries ?? []);

            // Resolve series name from slug
            var seriesGroups = await LibraryApiService.GetSeriesByLibraryAsync(_libraryName);
            _seriesName = SlugHelper.FromSlug(SeriesSlug,
                seriesGroups?.Select(s => s.SeriesName) ?? []);

            _detail = await LibraryApiService.GetSeriesDetailAsync(_libraryName, _seriesName);
        }
        catch
        {
            _error = true;
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private async Task ToggleSeriesWatch()
    {
        if (_detail == null) return;
        var newState = !_detail.IsWatched;
        await SeriesWatchApiService.SetAutoWatchAsync(_seriesName, null, newState);
        await LoadData();
    }

    private async Task BulkMarkWanted(bool wanted)
    {
        await SeriesWatchApiService.BulkSetWantedAsync(_seriesName, null, wanted);
        await LoadData();
    }

    private async Task BulkMarkSeasonWanted(string seasonName, bool wanted)
    {
        await SeriesWatchApiService.BulkSetWantedAsync(_seriesName, seasonName, wanted);
        await LoadData();
    }

    private async Task ToggleWanted(SubtitleEntryDto entry)
    {
        await LibraryApiService.UpdateWantedStatusAsync(entry.Id, !entry.IsWanted);
        await LoadData();
    }

    private void ToggleExpanded(string seasonName)
    {
        if (!_expandedSeasons.Add(seasonName))
            _expandedSeasons.Remove(seasonName);
    }
}
```

- [ ] **Step 2: Verify build + Commit**

```bash
dotnet build --configuration Release
git add Frontend/HavitWebApp/Components/Pages/SeriesDetail.razor
git commit -m "feat: add SeriesDetail page with expandable seasons and management panel"
```

---

## Task 12: Manual Verification

- [ ] **Step 1: Run the full stack**

```bash
cd AppHost && dotnet run
```

- [ ] **Step 2: Trigger a library scan**

In the Web UI, trigger a scan. Check API logs that `Library` field is populated on entries.

- [ ] **Step 3: Verify NavMenu**

After scan completes, refresh page. Nav menu should show dynamic library entries matching top-level folders.

- [ ] **Step 4: Verify Library Browser**

Click a library in nav. Series cards should appear with stats and progress bars. For Movies-type flat libraries, should show flat view.

- [ ] **Step 5: Verify Series Detail**

Click a series. Verify: breadcrumbs, auto-watch toggle, bulk wanted/unwanted, season expand/collapse, file table with status badges and wanted toggles.

- [ ] **Step 6: Verify All Entries**

Navigate to `/library/all`. Flat grid still works with pagination and filters.

- [ ] **Step 7: Commit any fixes**

```bash
git add -A && git commit -m "fix: adjustments from manual testing"
```
