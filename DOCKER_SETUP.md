# Translarr - Docker Compose Setup

## Requirements

- Docker
- Docker Compose
- Container images (or ability to build them)

## Quick Start

### 1. Environment Preparation

Copy the example configuration file:
```bash
cp env.example .env
```

### 2. Configuration

Edit the `.env` file and adjust the following variables:

```bash
# Path to media files directory on the host
MEDIA_ROOT_PATH=/path/to/your/media

# Ports (optional)
API_PORT=5000
WEB_PORT=5001
```

**Important:** The `MEDIA_ROOT_PATH` must point to an existing directory on your host containing video files.

### 3. Launch

```bash
docker compose up -d
```

### 4. Access the Application

- **Web UI:** http://localhost:5001
- **API:** http://localhost:5000
- **API Swagger:** http://localhost:5000/swagger

## Directory Structure

```
/path/to/your/media/          # Source directory (MEDIA_ROOT_PATH)
├── Series1/
│   ├── Season 1/
│   │   ├── Episode1.mkv
│   │   └── Episode2.mkv
│   └── Season 2/
│       └── Episode1.mkv
└── Series2/
    └── Season 1/
        └── Episode1.mp4
```

## Persistent Storage

### SQLite Database

The database is stored in a named volume `translarr-db`:
- Container location: `/app/data/translarr.db`
- Docker volume: `translarr-db`

### Media Files

The video files directory is mounted as a read-only volume:
- Container location: `/app/mediaroot`
- Source: value of `MEDIA_ROOT_PATH` variable from `.env` file

## Management

### Viewing Logs

```bash
# All services
docker compose logs -f

# API only
docker compose logs -f translarr-api

# Web only
docker compose logs -f translarr-web
```

### Restart Services

```bash
docker compose restart
```

### Stop

```bash
docker compose down
```

### Stop with Volume Removal (WARNING: will delete the database!)

```bash
docker compose down -v
```

## Database Backup

### Creating a Backup

```bash
docker compose exec translarr-api sh -c "cp /app/data/translarr.db /app/data/translarr.db.backup"
docker cp translarr-api:/app/data/translarr.db.backup ./translarr-backup-$(date +%Y%m%d).db
```

### Restoring from Backup

```bash
docker compose down
docker cp ./translarr-backup-YYYYMMDD.db translarr-api:/app/data/translarr.db
docker compose up -d
```

## Troubleshooting

### Cannot Access Media Files

1. Check if `MEDIA_ROOT_PATH` is correct
2. Check directory permissions
3. Check API logs: `docker compose logs translarr-api`

### Database Issues

1. Check if `translarr-db` volume exists: `docker volume ls`
2. Check API logs during startup: `docker compose logs translarr-api`

### Web UI Cannot Connect to API

1. Check if API responds: `curl http://localhost:5000/health`
2. Check if containers are on the same network: `docker network inspect translarr_default`
3. Check Web logs: `docker compose logs translarr-web`

## Updates

### Updating Images

```bash
# Stop services
docker compose down

# Pull latest images
docker compose pull

# Start again
docker compose up -d
```

## Building Your Own Images

If you want to build images locally instead of pulling them from the registry:

```bash
# Build API image
cd Core/Api
dotnet publish /t:PublishContainer

# Build Web image
cd ../../Frontend/WebApp
dotnet publish /t:PublishContainer

# Run with local images
cd ../..
docker compose up -d
```

## Security

⚠️ **Warning:** The current configuration is intended for local use. Before deploying to production:

1. Configure HTTPS
2. Add authentication
3. Restrict API access
4. Use strong passwords for all services
5. Regularly backup the database

