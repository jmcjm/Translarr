// var builder = DistributedApplication.CreateBuilder(args);
//
// builder.AddDockerComposeEnvironment("prod");
//
// var areWePublishing = builder.ExecutionContext.IsPublishMode;
//
// // Media path configurations
// var mediaRootPathOnHostParam = builder.AddParameter("MediaRootOnHost");
// var mediaRootPathOnHost = mediaRootPathOnHostParam.Resource.GetValueAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult() 
//                           ?? throw new ArgumentException("MediaRootOnHost parameter not found");
//
// var mediaRootPathInsideContainer = areWePublishing ? "/app/media" : mediaRootPathOnHost;
//
// // Database initialization
// const string dbVolumeName = "translarr-db";
// const string dbVolumePath = "/app/data";
//
// // Hack around Aspire, we can't add .WithVolume to ProjectResource.
// // We have to first create it and then use it as an annotation.
// // https://github.com/dotnet/aspire/issues/4359
// var dbMountAnnotation = new ContainerMountAnnotation(dbVolumeName, dbVolumePath, ContainerMountType.Volume, false);
// IResourceBuilder<SqliteResource> sqlite;
// IResourceBuilder<ContainerResource> dbInit;
//
// // There is no point in running this in dev
// if (areWePublishing)
// {
//     dbInit = builder.AddContainer("translarr-db-init", "alpine:latest")
//         .WithAnnotation(dbMountAnnotation)
//         .WithEntrypoint("sh")
//         .WithArgs("-c",
//             "if [ ! -f /app/data/translarr.db ]; then " +
//             "touch /app/data/translarr.db && echo 'Database file created'; " +
//             "else echo 'Database file already exists'; fi && " +
//             "chown -R 1000:1000 /app/data && chmod -R 755 /app/data && chmod 666 /app/data/translarr.db && " +
//             "echo 'Permissions set correctly'");
//     
//     sqlite = builder.AddSqlite("translarr-db", "/app/data", "translarr.db");
// }
// else
// {
//     sqlite = builder.AddSqlite("translarr-db", "translarr.db").WithSqliteWeb();
// }
//     
//     
//
// // Api and WebApp
// var mediaMountAnnotation = new ContainerMountAnnotation(mediaRootPathOnHost, mediaRootPathInsideContainer, ContainerMountType.BindMount, false);
//
// var api = builder.AddProject<Projects.Api>("Translarr-Api")
//     .WaitFor(sqlite)
//     .WithReference(sqlite)
//     // .WithHealthCheck("/health")
//     .WithAnnotation(mediaMountAnnotation)
//     .WithAnnotation(dbMountAnnotation)
//     .WithEnvironment("MediaRootPath", mediaRootPathInsideContainer);
//
// if (areWePublishing)
//     api.WaitForCompletion(dbInit);
//
// builder.AddProject<Projects.WebApp>("Translarr-Web")
//     .WaitFor(api)
//     .WithReference(api)
//     // .WithHealthCheck("/health")
//     .WithExternalHttpEndpoints();
//
// builder.Build().Run();
