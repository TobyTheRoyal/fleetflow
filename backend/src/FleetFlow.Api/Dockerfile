FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY backend/src/FleetFlow.Api/FleetFlow.Api.csproj backend/src/FleetFlow.Api/packages.lock.json backend/src/FleetFlow.Api/
COPY backend/src/FleetFlow.Application/FleetFlow.Application.csproj backend/src/FleetFlow.Application/packages.lock.json backend/src/FleetFlow.Application/
COPY backend/src/FleetFlow.Domain/FleetFlow.Domain.csproj backend/src/FleetFlow.Domain/packages.lock.json backend/src/FleetFlow.Domain/
COPY backend/src/FleetFlow.Infrastructure/FleetFlow.Infrastructure.csproj backend/src/FleetFlow.Infrastructure/packages.lock.json backend/src/FleetFlow.Infrastructure/
RUN dotnet restore backend/src/FleetFlow.Api/FleetFlow.Api.csproj --locked-mode
COPY backend/src/ backend/src/
RUN dotnet publish backend/src/FleetFlow.Api/FleetFlow.Api.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FleetFlow.Api.dll"]
