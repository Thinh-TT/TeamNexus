# Build stage using .NET 10 SDK
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution file and common props
COPY Directory.Build.props TeamNexus.sln ./

# Copy all source projects
COPY src/ ./src/

# Publish TeamNexus.Api in Release mode
RUN dotnet publish src/TeamNexus.Api/TeamNexus.Api.csproj -c Release -o /app/publish

# Runtime stage using ASP.NET Core 10.0 runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Copy published files from build stage
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "TeamNexus.Api.dll"]
