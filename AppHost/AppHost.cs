var builder = DistributedApplication.CreateBuilder(args);

// For now building complete Docker Compose with Aspire is too hard
// as we have to mount volumes to projects and have db in different location basen on environment.
//builder.AddDockerComposeEnvironment("prod");

// Media path configurations
var mediaRootPathOnHostParam = builder.AddParameter("MediaRootOnHost");
var mediaRootPathOnHost = mediaRootPathOnHostParam.Resource.GetValueAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult() 
                          ?? throw new ArgumentException("MediaRootOnHost parameter not found");

var sqlite = builder.AddSqlite(name: "translarr-db", databaseFileName: "translarr.db")
    .WithSqliteWeb();

var api = builder.AddProject<Projects.Api>("Translarr-Api")
    .WaitFor(sqlite)
    .WithReference(sqlite)
    // .WithHealthCheck("/health")
    .WithEnvironment("MediaRootPath", mediaRootPathOnHost);

builder.AddProject<Projects.WebApp>("Translarr-Web")
    .WaitFor(api)
    .WithReference(api)
    // .WithHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.HavitWebApp>("Translarr-Havit-Web")
    .WaitFor(api)
    .WithReference(api)
    // .WithHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.Build().Run();
