# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Translarr is a self-hosted subtitle translation automation tool built with .NET 9, Aspire orchestration, Blazor Server UI, and Google Gemini AI. It scans media libraries, detects missing subtitles in a preferred language, and translates them automatically using FFmpeg for stream extraction and Gemini for translation.

## Architecture

The project follows a distributed application model orchestrated by .NET Aspire:

- **AppHost**: Aspire orchestration layer that manages service dependencies, configuration parameters, and database provisioning
- **Core/Api**: REST API backend with minimal API endpoints organized by feature groups (Library, Translation, Settings, Stats)
- **Core/Application**: Business logic layer with service interfaces and DTOs. Uses ErrorOr pattern for error handling
- **Core/Infrastructure**: Data access and external integrations (EF Core + SQLite, FFmpeg wrapper, Gemini API client)
- **Frontend/WebApp**: Blazor Server UI built with MudBlazor
- **Frontend/Worker**: Background worker service (planned for automated scheduling via TickerQ)
- **ServiceDefaults**: Shared Aspire configuration for health checks, resilience, and OpenTelemetry

### Key Architectural Patterns

- **Clean Architecture**: Clear separation between Application (interfaces/DTOs), Infrastructure (implementations), and Api (HTTP endpoints)
- **ErrorOr**: Functional error handling throughout Application layer instead of exceptions
- **Repository Pattern**: Data access abstracted via interfaces (ISubtitleEntryRepository, IAppSettingsRepository, IApiUsageRepository)
- **Dependency Injection**: All service registration happens in `Core/Infrastructure/DependencyInjection.cs`

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
# Add migration (from Core/Infrastructure)
cd Core/Infrastructure
dotnet ef migrations add <MigrationName> --startup-project ../Api

# Apply migrations manually (normally handled by TranslarrDatabaseInitializer at startup)
dotnet ef database update --startup-project ../Api
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
- `MediaScannerService`: Scans media directories, detects existing subtitles, identifies translation candidates
- `SubtitleTranslationService`: Orchestrates translation workflow (extract, translate via Gemini, save)
- `LibraryService`: Manages subtitle entry CRUD and wanted/force-process flags
- `SettingsService`: Application settings management
- `ApiUsageService`: Tracks Gemini API usage statistics

**Infrastructure Layer** (`Core/Infrastructure/Services/`):
- `FfmpegService`: FFmpeg wrapper for subtitle stream detection and extraction
- `GeminiClient`: Google Gemini API integration with retry logic
- `TranslarrDatabaseInitializer`: Database setup and default settings seeding

### API Endpoint Organization

Minimal API endpoints in `Core/Api/Endpoints/` mapped by feature groups in `Program.cs`:
- `LibraryEndpoints`: GET entries, pagination, search/filter, update wanted status
- `TranslationEndpoints`: Start translation, status monitoring
- `SettingsEndpoints`: CRUD for app settings (Gemini key, preferred language, etc.)
- `StatsEndpoints`: Dashboard statistics

## FFmpeg Dependency

FFmpeg must be installed and accessible in PATH. The application uses FFMpegCore package for:
- Analyzing video files for embedded subtitle streams
- Extracting subtitle tracks for translation
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
- No authentication on API or WebApp (planned)
- Worker service for automated scheduling (planned using TickerQ)
- Aspire cannot yet generate full Docker Compose due to volume mounting complexity

## Settings Configuration

First-run configuration via WebApp Settings page:
- Google Gemini API Key (get from https://aistudio.google.com/app/apikey)
- Preferred subtitle language (ISO 639-1 two-letter code: pl, es, fr, etc.)
- AI model selection, temperature, rate limits

Settings stored in SQLite and seeded with defaults by `TranslarrDatabaseInitializer`.