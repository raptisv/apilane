# The runtime image the final stage is built on
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5001


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Apilane.Api/Apilane.Api.csproj", "src/Apilane.Api/"]
COPY ["src/Apilane.Api.Core/Apilane.Api.Core.csproj", "src/Apilane.Api.Core/"]
COPY ["src/Apilane.Common/Apilane.Common.csproj", "src/Apilane.Common/"]
COPY ["src/Apilane.Data/Apilane.Data.csproj", "src/Apilane.Data/"]
RUN dotnet restore "./src/Apilane.Api/Apilane.Api.csproj"
COPY . .
WORKDIR "/src/src/Apilane.Api"

ARG VERSION # Accept version as a build argument
RUN echo "Building version: $VERSION"

RUN dotnet build "./Apilane.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build /p:Version=$VERSION

# This stage publishes the project for Linux and the architecture of the image only (TARGETARCH is set by
# Docker: amd64 or arm64), so no native files of other systems end up in the image
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
RUN dotnet publish "./Apilane.Api.csproj" -c $BUILD_CONFIGURATION -a ${TARGETARCH:-amd64} --self-contained false -o /app/publish /p:Version=$VERSION

# The image that is shipped
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# The license of Apilane and the notices of the third-party code it ships
COPY LICENSE ./LICENSE
COPY licenses ./licenses
ENTRYPOINT ["dotnet", "Apilane.Api.dll"]