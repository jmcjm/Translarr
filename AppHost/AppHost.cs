var builder = DistributedApplication.CreateBuilder(args);

var mediaRootPathOnHostParam = builder.AddParameter("MediaRootOnHost");
var mediaRootPathOnHost = mediaRootPathOnHostParam.Resource.GetValueAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult()
                          ?? throw new ArgumentException("MediaRootOnHost parameter not found");

var sqlite = builder.AddSqlite(name: "translarr-db", databaseFileName: "translarr.db")
    .WithSqliteWeb();

// Separate auth database (no web viewer needed - it's just Identity tables)
var sqliteAuth = builder.AddSqlite(name: "translarr-auth", databaseFileName: "translarr-auth.db");

var api = builder.AddProject<Projects.Api>("Translarr-Api")
    .WaitFor(sqlite)
    .WaitFor(sqliteAuth)
    .WithReference(sqlite)
    .WithReference(sqliteAuth)
    .WithEnvironment("MediaRootPath", mediaRootPathOnHost);

builder.AddProject<Projects.HavitWebApp>("Translarr-Havit-Web")
    .WaitFor(api)
    .WithReference(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
