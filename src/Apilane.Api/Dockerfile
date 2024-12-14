# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5001


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
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

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Apilane.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:Version=$VERSION

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Apilane.Api.dll"]