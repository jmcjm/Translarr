# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Translarr is a self-hosted subtitle translation automation tool built with .NET 10, Aspire orchestration, Blazor Server UI (Havit Blazor), and multiple LLM providers (Gemini, OpenAI, Anthropic, etc.). It scans media libraries, detects missing subtitles in a preferred language, and translates them automatically using FFmpeg for stream extraction and an LLM for translation. Supports both text-based and bitmap (PGS/VobSub) subtitles via LLM-powered OCR.

## Architecture

The project follows a distributed application model orchestrated by .NET Aspire:

- **AppHost**: Aspire orchestration layer that manages service dependencies, configuration parameters, and database provisioning
- **Core/Api**: REST API backend with minimal API endpoints organized by feature groups (Library, Translation, Settings, Stats), SignalR hub for real-time progress
- **Core/Application**: Business logic layer with service interfaces and DTOs. Uses ErrorOr pattern for error handling
- **Core/Infrastructure**: Data access and external integrations (EF Core + SQLite, FFmpeg wrapper, LLM client)
- **Frontend/HavitWebApp**: Blazor Server UI built with Havit Blazor (NOT MudBlazor)
- **Frontend/Worker**: Background worker service (planned for automated scheduling via TickerQ)
- **ServiceDefaults**: Shared Aspire configuration for health checks, resilience, and OpenTelemetry

### Key Architectural Patterns

- **Clean Architecture**: Clear separation between Application (interfaces/DTOs), Infrastructure (implementations), and Api (HTTP endpoints)
- **ErrorOr**: Functional error handling throughout Application layer instead of exceptions
- **Repository Pattern**: Data access abstracted via interfaces (ISubtitleEntryRepository, IAppSettingsRepository, IApiUsageRepository)
- **Dependency Injection**: All service registration happens in `Core/Infrastructure/DependencyInjection.cs`
- **Primary Constructors**: All services/repos use `class Foo(IDep dep) : IFoo` syntax with direct param access (no underscore prefix)
- **SignalR**: Real-time progress via `ProgressHub` at `/hubs/progress` — broadcasts `ScanProgress`, `TranslationProgress`, `BitmapProgress` events
- **One File = One Class**: Every class/record/interface in its own file, no exceptions

## Build and Development Commands

### Development with Aspire

```bash
# Run entire stack (API + WebApp + Aspire Dashboard)
cd AppHost
dotnet run

# Configure media path in AppHost/appsettings.Development.json:
# "Parameters": { "MediaRootOnHost": "/path/to/your/videos" }
```

The Aspire Dashboard will show URLs for all services. WebApp connects to API automatically via service discovery.

### Docker Compose (Production)

```bash
# Configure .env first:
cp env.example .env
# Edit MEDIA_ROOT_PATH, API_PORT, WEB_PORT

# Run containers
docker compose up -d

# Access:
# - Web UI: http://localhost:5001
# - API: http://localhost:5000
# - Swagger: http://localhost:5000/swagger
```

### Build Commands

```bash
# Build entire solution
dotnet build --configuration Release

# Build specific project
dotnet build Core/Api/Api.csproj

# Publish to containers (requires Docker login)
cd Core/Api && dotnet publish /t:PublishContainer
cd Frontend/WebApp && dotnet publish /t:PublishContainer
```

### Database Commands

```bash
# Add migration (from Core/Infrastructure — requires --context due to multiple DbContexts)
cd Core/Infrastructure
dotnet ef migrations add <MigrationName> --startup-project ../Api --context TranslarrDbContext

# Apply migrations manually (normally handled by TranslarrDatabaseInitializer at startup)
dotnet ef database update --startup-project ../Api --context TranslarrDbContext
```

## Code Organization

### Namespaces

Automatic namespaces based on folder structure via `Directory.Build.props`:
- Base namespace: `Translarr`
- Example: `Core/Application/Services/LibraryService.cs` -> `Translarr.Core.Application.Services`

### Centralized Package Management

All NuGet versions in `Directory.Packages.props` with `ManagePackageVersionsCentrally`. Individual projects reference packages without version numbers.

### Key Services

**Application Layer** (`Core/Application/Services/`):
- `MediaScannerService`: Scans media directories, detects existing subtitles, identifies translation candidates. Populates `Library` field from top-level folder.
- `SubtitleTranslationService`: Orchestrates text subtitle translation workflow (extract, translate via LLM, save). Detects bitmap-only subs and routes to OCR pipeline.
- `BitmapTranslationService`: Handles bitmap subtitle (PGS/VobSub) OCR via LLM — extracts images, sends to vision model, converts to SRT.
- `LibraryService`: Manages subtitle entry CRUD, wanted/force-process flags, hierarchical library browsing (libraries, series detail).
- `SeriesWatchService`: Auto-watch configuration per series/season, bulk wanted operations, watch status enrichment.
- `SettingsService`: Application settings management
- `ApiUsageService`: Tracks LLM API usage statistics

**Application Helpers** (`Core/Application/Helpers/`):
- `NaturalSort`: Shared natural sort key generation ("Season 2" before "Season 10")

**Infrastructure Layer** (`Core/Infrastructure/Services/`):
- `FfmpegService`: FFmpeg wrapper for subtitle stream detection and extraction (text and bitmap)
- `LlmClient` / provider implementations: LLM API integrations (Gemini, OpenAI, Anthropic) with retry logic
- `TranslarrDatabaseInitializer`: Database setup and default settings seeding

### API Endpoint Organization

Minimal API endpoints in `Core/Api/Endpoints/` mapped by feature groups in `Program.cs`:
- `LibraryEndpoints`: Entries CRUD, pagination, search/filter, wanted status, hierarchical browse (`/browse`, `/browse/series`, `/browse/series/detail`), bulk wanted with library scoping
- `TranslationEndpoints`: Start/stop text translation, start/stop bitmap OCR translation, status monitoring
- `SeriesWatchEndpoints`: Auto-watch configs, series groups with watch status
- `SettingsEndpoints`: CRUD for app settings (LLM API key, preferred language, etc.)
- `StatsEndpoints`: Dashboard statistics

### Frontend Pages

- `Library.razor` (`/library/all`): Flat paginated grid of all entries with filters (status, wanted, search)
- `LibraryBrowser.razor` (`/library/{slug}`): Per-library series list with cards, flat file table for Movies-type libraries
- `SeriesDetail.razor` (`/library/{slug}/{series}`): Series management — auto-watch, bulk wanted, expandable seasons with file tables
- `NavMenu.razor`: Dynamic library submenu, refreshed via SignalR on scan completion

### Shared Components

- `SubtitleEntryTable.razor`: Reusable file table with status badges, wanted toggle, error info, retry button

## FFmpeg Dependency

FFmpeg must be installed and accessible in PATH. The application uses FFMpegCore package for:
- Analyzing video files for embedded subtitle streams (text and bitmap)
- Extracting text subtitle tracks (SRT, ASS/SSA) for translation
- Extracting bitmap subtitle frames (PGS, VobSub) as images for OCR
- Prioritizing non-SDH English tracks when selecting translation sources

## Database

SQLite via EF Core. Connection string managed by Aspire in development (`translarr-db` resource) or via environment variable in Docker (`ConnectionStrings__translarr-db`).

Database initialization happens automatically at API startup via `DependencyInjection.InitializeDatabaseAsync()`.

## Aspire Configuration

AppHost requires `MediaRootOnHost` parameter set in `appsettings.Development.json`. This path is passed to the API container as `MediaRootPath` environment variable.

Services use `.WaitFor()` and `.WithReference()` for dependency management:
- API waits for SQLite database
- WebApp waits for API

## Docker Images

Published to Docker Hub as `jmcjm/translarr-api:nightly` and `jmcjm/translarr-web:nightly` via GitHub Actions on push to main.

Container configuration uses `/t:PublishContainer` target. Images run as non-root user 1000:1000.

## Known Issues and TODOs

- SELinux incompatibility: Docker volumes fail on systems with enforcing SELinux
- Worker service for automated scheduling (planned using TickerQ)
- After upgrade, existing entries need a rescan to populate the `Library` field for hierarchical navigation

## Settings Configuration

First-run configuration via WebApp Settings page:
- LLM provider selection and API key (e.g. Gemini key from https://aistudio.google.com/app/apikey)
- Preferred subtitle language (ISO 639-1 two-letter code: pl, es, fr, etc.)
- AI model selection, temperature, rate limits

Settings stored in SQLite and seeded with defaults by `TranslarrDatabaseInitializer`.