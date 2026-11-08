FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY backend/src/ backend/src/
COPY backend/tests/FleetFlow.BrowserHost/ backend/tests/FleetFlow.BrowserHost/
RUN dotnet restore backend/tests/FleetFlow.BrowserHost/FleetFlow.BrowserHost.csproj --locked-mode
RUN dotnet publish backend/tests/FleetFlow.BrowserHost/FleetFlow.BrowserHost.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false
RUN dotnet publish backend/src/FleetFlow.Api/FleetFlow.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 ASPNETCORE_TEST_CONTENTROOT_FLEETFLOW_API=/app
COPY --from=build /app/publish ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FleetFlow.BrowserHost.dll"]
