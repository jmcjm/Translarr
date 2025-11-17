FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
RUN apk add --no-cache \
    ffmpeg \
    tzdata \
    icu-libs \
    icu-data-full \
    zlib

USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["Core/Api/Api.csproj", "Core/Api/"]
COPY ["ServiceDefaults/ServiceDefaults.csproj", "ServiceDefaults/"]
COPY ["Core/Application/Application.csproj", "Core/Application/"]
COPY ["Core/Infrastructure/Infrastructure.csproj", "Core/Infrastructure/"]

RUN dotnet restore "Core/Api/Api.csproj"

COPY . .
WORKDIR "/src/Core/Api"
RUN dotnet build "./Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Translarr.Core.Api.dll"]
