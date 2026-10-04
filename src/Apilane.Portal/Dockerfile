# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5000


# This stage builds the Portal UI (src/Apilane.Portal.Ui) into static files. Node exists only
# here: the final image gets the files, not the toolchain. The folder layout mirrors the
# repository because the UI builds into ../Apilane.Portal/wwwroot/ui (see vite.config.ts).
FROM node:24.13.0-alpine AS ui
WORKDIR /src/src/Apilane.Portal.Ui
COPY ["src/Apilane.Portal.Ui/package.json", "src/Apilane.Portal.Ui/package-lock.json", "./"]
RUN npm ci --no-audit --no-fund
COPY src/Apilane.Portal.Ui/ ./
RUN npm run build


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Apilane.Portal/Apilane.Portal.csproj", "src/Apilane.Portal/"]
COPY ["src/Apilane.Common/Apilane.Common.csproj", "src/Apilane.Common/"]
RUN dotnet restore "./src/Apilane.Portal/Apilane.Portal.csproj"
COPY . .
WORKDIR "/src/src/Apilane.Portal"

ARG VERSION # Accept version as a build argument
RUN echo "Building version: $VERSION"

RUN dotnet build "./Apilane.Portal.csproj" -c $BUILD_CONFIGURATION -o /app/build /p:Version=$VERSION

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Apilane.Portal.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:Version=$VERSION

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=ui /src/src/Apilane.Portal/wwwroot/ui ./wwwroot/ui
ENTRYPOINT ["dotnet", "Apilane.Portal.dll"]
