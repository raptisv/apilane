# The runtime image the final stage is built on
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
COPY ["src/Apilane.Portal.MCP/Apilane.Portal.MCP.csproj", "src/Apilane.Portal.MCP/"]
COPY ["src/Apilane.Common/Apilane.Common.csproj", "src/Apilane.Common/"]
RUN dotnet restore "./src/Apilane.Portal/Apilane.Portal.csproj"
COPY . .
WORKDIR "/src/src/Apilane.Portal"

ARG VERSION # Accept version as a build argument
RUN echo "Building version: $VERSION"

RUN dotnet build "./Apilane.Portal.csproj" -c $BUILD_CONFIGURATION -o /app/build /p:Version=$VERSION

# This stage publishes the project for Linux and the architecture of the image only (TARGETARCH is set by
# Docker: amd64 or arm64), so no native files of other systems end up in the image
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
RUN dotnet publish "./Apilane.Portal.csproj" -c $BUILD_CONFIGURATION -a ${TARGETARCH:-amd64} --self-contained false -o /app/publish /p:Version=$VERSION

# The image that is shipped
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# The license of Apilane and the notices of the third-party code it ships
COPY LICENSE ./LICENSE
COPY licenses ./licenses
COPY --from=ui /src/src/Apilane.Portal/wwwroot/ui ./wwwroot/ui
ENTRYPOINT ["dotnet", "Apilane.Portal.dll"]
