# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore memakai csproj saja agar layer cache tidak batal saat source berubah.
COPY Directory.Build.props Traceability.sln ./
COPY src/Traceability.Domain/Traceability.Domain.csproj src/Traceability.Domain/
COPY src/Traceability.Application/Traceability.Application.csproj src/Traceability.Application/
COPY src/Traceability.Infrastructure/Traceability.Infrastructure.csproj src/Traceability.Infrastructure/
COPY src/Traceability.Api/Traceability.Api.csproj src/Traceability.Api/
RUN dotnet restore Traceability.sln

COPY src/ src/
RUN dotnet publish src/Traceability.Api/Traceability.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "Traceability.Api.dll"]
