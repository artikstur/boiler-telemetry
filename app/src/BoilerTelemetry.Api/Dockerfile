FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore
COPY src/BoilerTelemetry.Domain/*.csproj src/BoilerTelemetry.Domain/
COPY src/BoilerTelemetry.Application/*.csproj src/BoilerTelemetry.Application/
COPY src/BoilerTelemetry.Infrastructure/*.csproj src/BoilerTelemetry.Infrastructure/
COPY src/BoilerTelemetry.Api/*.csproj src/BoilerTelemetry.Api/
COPY BoilerTelemetry.sln .
RUN dotnet restore

# Copy everything and publish
COPY . .
RUN dotnet publish src/BoilerTelemetry.Api -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "BoilerTelemetry.Api.dll"]
