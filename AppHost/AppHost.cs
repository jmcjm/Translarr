var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("prod");

// Media path configurations
const string mediaRootPathInsideContainer = "/app/mediaroot";
var mediaRootPathOnHostParam = builder.AddParameter("MediaRootOnHost");
var mediaRootPathOnHost = mediaRootPathOnHostParam.Resource.GetValueAsync(CancellationToken.None).GetAwaiter().GetResult() 
                          ?? throw new ArgumentException("MediaRootOnHost parameter not found");

// Database initialization
const string dbVolumeName = "translarr-db";
const string dbVolumePath = "/app/data";
var dbMountAnnotation = new ContainerMountAnnotation(dbVolumeName, dbVolumePath, ContainerMountType.Volume, false);

var dbInit = builder.AddContainer("translarr-db-init", "alpine:latest")
    .WithAnnotation(dbMountAnnotation)
    .WithEntrypoint("sh")
    .WithArgs("-c",
        "if [ ! -f /app/data/translarr.db ]; then " +
        "touch /app/data/translarr.db && echo 'Database file created'; " +
        "else echo 'Database file already exists'; fi && " +
        "chown -R 1000:1000 /app/data && chmod -R 755 /app/data && chmod 666 /app/data/translarr.db && " +
        "echo 'Permissions set correctly'");

var sqlite = builder.AddSqlite(name: "translarr-db", databaseFileName: "translarr.db")
    .WithSqliteWeb();

// Api and WebApp
var mediaMountAnnotation = new ContainerMountAnnotation(mediaRootPathOnHost, mediaRootPathInsideContainer, ContainerMountType.BindMount, false);

var api = builder.AddProject<Projects.Api>("Translarr-Api")
    .WaitFor(sqlite)
    .WithReference(sqlite)
    .WaitForCompletion(dbInit)
    // .WithHealthCheck("/health")
    .WithAnnotation(mediaMountAnnotation)
    .WithAnnotation(dbMountAnnotation)
#if DEBUG
    .WithEnvironment("MediaRootPath", mediaRootPathOnHost);
#elif PRODUCTION
    .WithEnvironment("MediaRootPath", mediaRootPathInsideContainer);
#endif

builder.AddProject<Projects.WebApp>("Translarr-Web")
    .WaitFor(api)
    .WithReference(api)
    // .WithHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.Build().Run();
