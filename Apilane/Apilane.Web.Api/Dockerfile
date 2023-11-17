#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5001

# Fonts
RUN apt-get update && \
    apt-get install -y wget \
    fontconfig && \
    wget http://ftp.br.debian.org/debian/pool/contrib/m/msttcorefonts/ttf-mscorefonts-installer_3.8_all.deb && \
    apt --fix-broken install -y ./ttf-mscorefonts-installer_3.8_all.deb && \
    rm ttf-mscorefonts-installer_3.8_all.deb && \
    fc-cache -f -v

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Apilane/Apilane.Web.Api/Apilane.Web.Api.csproj", "Apilane/Apilane.Web.Api/"]
COPY ["Apilane/Apilane.Api/Apilane.Api.csproj", "Apilane/Apilane.Api/"]
COPY ["Apilane/Apilane.Data/Apilane.Data.csproj", "Apilane/Apilane.Data/"]
COPY ["Apilane/Apilane.Common/Apilane.Common.csproj", "Apilane/Apilane.Common/"]
RUN dotnet restore "Apilane/Apilane.Web.Api/Apilane.Web.Api.csproj"
COPY . .
WORKDIR "/src/Apilane/Apilane.Web.Api"

ARG VERSION # Accept version as a build argument
RUN echo "Building version: $VERSION"

RUN dotnet build "Apilane.Web.Api.csproj" -c Release -o /app/build /p:Version=$VERSION

FROM build AS publish
RUN dotnet publish "Apilane.Web.Api.csproj" -c Release -o /app/publish /p:Version=$VERSION

FROM base AS final
WORKDIR /app

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Apilane.Web.Api.dll"]