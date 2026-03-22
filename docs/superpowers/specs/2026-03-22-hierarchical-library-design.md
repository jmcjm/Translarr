# Hierarchical Library Navigation

## Overview

Replace the current flat Library grid + separate SeriesManagement page with a hierarchical navigation system. Top-level media folders become dynamically detected "libraries" in the nav menu. Each library has its own page showing series, each series has a detail page with expandable seasons and file-level management.

## Decisions

- **Library detection:** Automatic from top-level folders in `MediaRootPath` (no config)
- **Movies/flat libraries:** Detected by directory depth — if entries have no season-level subdirectories (Series == Season or only one level of nesting), render as flat file table
- **Data storage:** New `Library` column on `SubtitleEntries` table (queryable via SQL, indexed)
- **Old Library.razor:** Kept as "All Entries" at `/library/all`
- **SeriesManagement.razor:** Removed — functionality moved to `SeriesDetail.razor`
- **Menu refresh:** On app start + polling scan status endpoint, refresh on completion
- **Series detail view (not inline):** Separate page per series with management panel + expandable seasons
- **API naming uses query params** for library/series names (not path segments) to avoid URL encoding issues

## Data Model Changes

### SubtitleEntryDao

New column:

```csharp
public string Library { get; set; } = "";
```

- Populated by `MediaScannerService` from first path segment relative to `MediaRootPath`
- Example: `/Videos/test/TV Shows/Breaking Bad/S01/ep.mkv` with `MediaRootPath=/Videos/test` → `Library = "TV Shows"`
- Files directly in `MediaRootPath` (no subfolder) → `Library = "Uncategorized"`
- EF Core migration required

### SubtitleEntryConfiguration

- `Library` column: `IsRequired()`, `HasMaxLength(256)`, `HasDefaultValue("")`
- New index: composite `{Library, Series, Season}` (replaces or supplements existing `{Series, Season}` index if one exists)

### Data Migration

Migration includes a SQL `UPDATE` that derives `Library` from `FilePath` for all existing rows:

```sql
UPDATE SubtitleEntries
SET Library = -- extract first path segment after MediaRootPath from FilePath
```

Fallback: if migration is too complex for path parsing in SQL, set all existing rows to `Library = ""` and force a rescan notification in the UI ("Library data needs refresh — please run a scan").

### SubtitleEntryDto

Add `Library` property:

```csharp
public string Library { get; set; } = "";
```

### VideoFile

Add `Library` property:

```csharp
public string Library { get; set; } = "";
```

Populated in `ScanFilesystemAsync` when parsing path segments (same place where Series/Season are extracted).

### Repository Mapping

Update in `SubtitleEntryRepository`:
- `MapToDto()` — map `Library` from DAO to DTO
- `MapToDao()` — map `Library` from DTO to DAO
- `UpdateAsync()` — include `Library` in property copy

### New DTOs (one file per record)

`SeriesDetailDto.cs`:
```csharp
public record SeriesDetailDto(
    string SeriesName,
    bool IsWatched,
    List<SeasonDetailDto> Seasons
);
```

`SeasonDetailDto.cs`:
```csharp
public record SeasonDetailDto(
    string SeasonName,
    bool IsWatched,
    int TotalFiles,
    int WantedFiles,
    int ProcessedFiles,
    List<SubtitleEntryDto> Entries
);
```

These are separate from existing `SeriesGroupDto`/`SeasonGroupDto` which stay for the existing `GetSeriesGroupsAsync` flow. The Detail DTOs include the full `Entries` list; the Group DTOs only have aggregate stats.

## API Changes

### New Endpoints

All new endpoints use query parameters for names to avoid URL encoding issues with spaces/special chars:

```
GET  /api/library/libraries
     → List<string>
     Returns distinct library names from SubtitleEntries.

GET  /api/library/libraries/series?library={name}
     → List<SeriesGroupDto>
     Returns series within a library with aggregated stats + watch status.

GET  /api/library/libraries/series/detail?library={name}&series={seriesName}
     → SeriesDetailDto
     Returns full series detail: metadata, watch status, seasons with entries.
```

Registered in `LibraryEndpoints.MapLibraryEndpoints()` alongside existing endpoints.

### Existing Endpoints — Changes

- `PUT /api/library/bulk/wanted` — add optional `library` query parameter to scope bulk operations and avoid cross-library collisions when series share names

### Existing Endpoints — No Changes

- `GET /api/library/entries` (paginated flat list) — stays for "All Entries"
- `SeriesWatchEndpoints` — reused for auto-watch toggles

## Repository Changes

### ISubtitleEntryRepository — New Methods

```csharp
Task<List<string>> GetDistinctLibrariesAsync();
Task<List<SeriesGroupDto>> GetSeriesGroupsByLibraryAsync(string library);
Task<List<SubtitleEntryDto>> GetEntriesByLibraryAndSeriesAsync(string library, string series);
```

### ISubtitleEntryRepository — Modified Methods

- `BulkUpdateWantedAsync` — add optional `string? library` parameter to filter by library

## Service Changes

### MediaScannerService

- `ScanFilesystemAsync`: when building `VideoFile` objects, extract `Library` from `pathParts[0]` (first segment of relative path). If `pathParts.Length == 1` (file directly in root), use `"Uncategorized"`.
- `AnalyzeVideoFilesAsync`: propagate `Library` from `VideoFile` to `SubtitleEntryDto`

### LibraryService / ILibraryService

New methods (all return `ErrorOr<T>`):

```csharp
Task<ErrorOr<List<string>>> GetLibrariesAsync();
Task<ErrorOr<List<SeriesGroupDto>>> GetSeriesByLibraryAsync(string library);
Task<ErrorOr<SeriesDetailDto>> GetSeriesDetailAsync(string library, string series);
```

`GetSeriesDetailAsync` calls both `ISubtitleEntryRepository` for entries and `ISeriesWatchService` for watch status, combining them into `SeriesDetailDto`.

### SeriesWatchService

- `GetSeriesGroupsWithWatchStatusAsync()` — add optional `string? library` parameter for filtering

## Frontend Changes

### Navigation (NavMenu)

Dynamic submenu under "Library":

```
Library
  ├── All Entries          → /library/all
  ├── TV Shows             → /library/tv-shows
  ├── Movies               → /library/movies
  └── Anime                → /library/anime
```

- Inject `LibraryApiService` into NavMenu, fetch library list in `OnInitializedAsync`
- Refresh mechanism: NavMenu polls scan status when a scan is active; on completion, re-fetches library list
- Slug generation: lowercase, spaces → hyphens, non-alphanumeric stripped
- Slug uniqueness: if collision, append `-2`, `-3` etc.
- Libraries appear/disappear based on scan results
- Handle API failure gracefully: show "Library" without submenu items, retry on next navigation

### Routing

```
/library/all                      → Library.razor (flat grid, renamed from /library)
/library/{librarySlug}            → LibraryBrowser.razor
/library/{librarySlug}/{series}   → SeriesDetail.razor
```

`/library/all` avoids route conflict with `/library/{librarySlug}` — "all" is a reserved slug.

### New Pages

#### LibraryBrowser.razor — `/library/{librarySlug}`

- Resolves slug back to library name via fetched library list
- Fetches series list from API
- **Normal library (TV Shows):** Displays series as cards/rows with: name, episode count, translated count, click → navigate to series detail
- **Flat library (Movies):** Detected when all entries have Series == Season or only one distinct Series value → render flat file table directly (same columns as Library.razor but filtered by library)
- Loading state: spinner while fetching
- Empty state: "No media found in this library"
- Error state: retry button
- Sorting: alphabetical by series name (default)

#### SeriesDetail.razor — `/library/{librarySlug}/{seriesSlug}`

- Header: series name
- Management panel: auto-watch toggle, bulk Mark All Wanted / Mark All Unwanted buttons
- Expandable season list:
  - Each season row: name, stats (total/wanted/processed), auto-watch toggle per season, bulk wanted button per season
  - Expanded: file table with columns matching current Library.razor (FileName, Status badge, Wanted toggle, AlreadyHas, LastScanned, Actions)
- Sorting: seasons sorted naturally (Season 1, Season 2, ... Season 10 — not lexicographic)
- Loading/empty/error states as per LibraryBrowser

### Existing Pages

- **Library.razor** — moved to `/library/all`, otherwise unchanged, serves as "All Entries" flat view
- **SeriesManagement.razor** — removed, replaced by SeriesDetail.razor

### LibraryApiService — New Methods

```csharp
Task<List<string>> GetLibrariesAsync();
Task<List<SeriesGroupDto>> GetSeriesByLibraryAsync(string libraryName);
Task<SeriesDetailDto> GetSeriesDetailAsync(string libraryName, string seriesName);
```

## Edge Cases

### Flat Libraries (Movies)

Detection: all entries in a library have the same `Series` value, or `Series == Season` for all entries (indicating no real hierarchy). Also covers cases like `Movies/Action/film.mkv` where scanner creates Series="Action" but there's no season structure — detected because Series == Season.

When detected, `LibraryBrowser.razor` skips series list and renders flat file table.

### Uncategorized

Files directly in `MediaRootPath` without a top-level folder get `Library = "Uncategorized"`. Appears in menu only if such entries exist.

### Library Disappearing

If a scan removes all entries for a library (folder deleted), it disappears from menu on next library list refresh.

### Slug Collision

If two library names produce the same slug (e.g., "TV Shows" and "TV-Shows"), append incrementing suffix (`-2`, `-3`). Frontend maintains slug→name mapping from the fetched library list. Collision is detected at mapping time, not stored in DB.

### Case Sensitivity (Linux)

Folder names are case-sensitive on Linux. "Anime" and "anime" are different libraries with different slugs ("anime" vs "anime-2"). This is expected filesystem behavior — no normalization.

### Cross-Library Series Name Collision

Two libraries can have series with the same name. `BulkUpdateWantedAsync` scoped by `library` parameter prevents unintended updates across libraries.

## What Does NOT Change

- `SeriesWatchConfigs` table and endpoints
- Translation workflow
- Settings and Stats pages
- Bitmap OCR flow
- FFmpeg integration
- Existing `GET /api/library/entries` pagination/filter logic (just route moved to `/library/all`)
- `DependencyInjection.cs` — no new services, just new methods on existing `LibraryService`
