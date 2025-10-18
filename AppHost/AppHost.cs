var builder = DistributedApplication.CreateBuilder(args);

var sqlite = builder.AddSqlite(name: "translarr-db", databaseFileName: "translarr.db")
    .WithSqliteWeb();

// Media path configuration — in production this will be a volume mount
var mediaRootPath = builder.Configuration["MediaRootPath"] ?? "/app/mediaroot";

var api = builder.AddProject<Projects.Api>("api")
    .WaitFor(sqlite)
    .WithReference(sqlite)
    .WithEnvironment("MediaRootPath", mediaRootPath);

// builder.AddProject<Projects.Worker>("worker")
//     .WaitFor(sqlite)
//     .WithReference(sqlite);

builder.AddProject<Projects.WebApp>("webapp")
    .WaitFor(api)
    .WithReference(api);

builder.Build().Run();
