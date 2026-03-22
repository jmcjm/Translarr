# Hierarchical Library Navigation

## Overview

Replace the current flat Library grid + separate SeriesManagement page with a hierarchical navigation system. Top-level media folders become dynamically detected "libraries" in the nav menu. Each library has its own page showing series, each series has a detail page with expandable seasons and file-level management.

## Decisions

- **Library detection:** Automatic from top-level folders in `MediaRootPath` (no config)
- **Movies/flat libraries:** Detected automatically (all entries share same Series value) — rendered as flat file table instead of series→season hierarchy
- **Data storage:** New `Library` column on `SubtitleEntries` table (queryable via SQL)
- **Old Library.razor:** Kept as "All Entries" at `/library`
- **SeriesManagement.razor:** Removed — functionality moved to `SeriesDetail.razor`
- **Menu refresh:** On app start + after each scan
- **Series detail view (not inline):** Separate page per series with management panel + expandable seasons

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

### New DTOs

```csharp
public record SeriesDetailDto(
    string SeriesName,
    bool IsWatched,
    List<SeasonDetailDto> Seasons
);

public record SeasonDetailDto(
    string SeasonName,
    bool IsWatched,
    int TotalFiles,
    int WantedFiles,
    int ProcessedFiles,
    List<SubtitleEntryDto> Entries
);
```

## API Changes

### New Endpoints

```
GET  /api/library/libraries
     → List<string>
     Returns distinct library names from SubtitleEntries.

GET  /api/library/libraries/{name}/series
     → List<SeriesGroupDto>
     Returns series within a library with aggregated stats + watch status.

GET  /api/library/libraries/{name}/series/{seriesName}
     → SeriesDetailDto
     Returns full series detail: metadata, watch status, seasons with entries.
```

### Existing Endpoints — No Changes

- `GET /api/library/entries` (paginated flat list) — stays for "All Entries"
- `PUT /api/library/bulk/wanted` — reused for bulk ops from new UI
- `SeriesWatchEndpoints` — reused for auto-watch toggles

## Repository Changes

### ISubtitleEntryRepository — New Methods

```csharp
Task<List<string>> GetDistinctLibrariesAsync();
Task<List<SeriesGroupDto>> GetSeriesGroupsByLibraryAsync(string library);
Task<List<SubtitleEntryDto>> GetEntriesByLibrarySeriesAndSeasonAsync(
    string library, string series, string? season = null);
```

## Service Changes

### MediaScannerService

- When creating/updating `SubtitleEntryDto`, extract `Library` from first segment of path relative to `MediaRootPath`
- Logic: `relativePath.Split(Path.DirectorySeparatorChar)[0]` — if only filename (no folder), use `"Uncategorized"`

### LibraryService

New methods wrapping repository calls:

```csharp
Task<List<string>> GetLibrariesAsync();
Task<List<SeriesGroupDto>> GetSeriesByLibraryAsync(string library);
Task<SeriesDetailDto> GetSeriesDetailAsync(string library, string series);
```

### SeriesWatchService

- `GetSeriesGroupsWithWatchStatusAsync()` — add optional `library` parameter for filtering

## Frontend Changes

### Navigation (NavMenu)

Dynamic submenu under "Library":

```
Library
  ├── All Entries          → /library
  ├── TV Shows             → /library/tv-shows
  ├── Movies               → /library/movies
  └── Anime                → /library/anime
```

- Fetches library list from `GET /api/library/libraries` on layout load
- Refreshes after scan completes
- Slug generation: lowercase, spaces → hyphens
- Libraries appear/disappear based on scan results

### New Pages

#### LibraryBrowser.razor — `/library/{librarySlug}`

- Resolves slug back to library name
- Fetches series list from API
- **Normal library (TV Shows):** Displays series as cards/rows with: name, episode count, translated count, click → navigate to series detail
- **Flat library (Movies):** If all entries share same Series or Series == library name → render flat file table directly (same as current Library.razor grid but filtered)

#### SeriesDetail.razor — `/library/{librarySlug}/{seriesSlug}`

- Header: series name
- Management panel: auto-watch toggle, bulk Mark All Wanted / Mark All Unwanted buttons
- Expandable season list:
  - Each season row: name, stats (total/wanted/processed), auto-watch toggle, bulk wanted button
  - Expanded: file table with columns matching current Library.razor (FileName, Status badge, Wanted toggle, AlreadyHas, LastScanned, Actions)

### Existing Pages

- **Library.razor** — stays at `/library`, unchanged, serves as "All Entries" flat view
- **SeriesManagement.razor** — removed, replaced by SeriesDetail.razor

### LibraryApiService — New Methods

```csharp
Task<List<string>> GetLibrariesAsync();
Task<List<SeriesGroupDto>> GetSeriesByLibraryAsync(string libraryName);
Task<SeriesDetailDto> GetSeriesDetailAsync(string libraryName, string seriesName);
```

## Edge Cases

### Flat Libraries (Movies)

Detection: all entries in a library have the same `Series` value, or `Series` equals the library name. When detected, `LibraryBrowser.razor` skips series list and renders flat file table.

### Uncategorized

Files directly in `MediaRootPath` without a top-level folder get `Library = "Uncategorized"`. Appears in menu only if such entries exist.

### Library Disappearing

If a scan removes all entries for a library (folder deleted), it disappears from menu on next library list refresh.

### Slug Mapping

URL slugs are derived from library/series names: lowercase, spaces → hyphens, special chars stripped. API endpoints use original names (not slugs). Frontend handles slug↔name mapping via the fetched library/series lists.

## What Does NOT Change

- `SeriesWatchConfigs` table and endpoints
- Translation workflow
- Settings and Stats pages
- Bitmap OCR flow
- FFmpeg integration
- Existing `GET /api/library/entries` pagination/filter logic
