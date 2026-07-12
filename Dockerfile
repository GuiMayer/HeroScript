# HeroScript API - Production Docker Image
# Multi-stage build for optimized image size

# ============================================
# Stage 1: Build
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["HeroScript.slnx", "./"]
COPY ["src/Core/Core.csproj", "src/Core/"]
COPY ["src/API/API.csproj", "src/API/"]

# Restore dependencies
RUN dotnet restore "src/API/API.csproj"

# Copy source code
COPY src/ src/

# Build and publish
WORKDIR "/src/src/API"
RUN dotnet build "API.csproj" -c Release -o /app/build
RUN dotnet publish "API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ============================================
# Stage 2: Runtime
# ============================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN groupadd -r heroscript && useradd -r -g heroscript heroscript

# Copy published application
COPY --from=build /app/publish .

# Copy configuration files
COPY --from=build /src/data/configs ./data/configs

# Create directories for persistence
RUN mkdir -p /app/data/events /app/data/runs && \
    chown -R heroscript:heroscript /app/data

# Switch to non-root user
USER heroscript

# Expose port
EXPOSE 8080

# Environment variables (can be overridden)
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/api/health || exit 1

# Entry point
ENTRYPOINT ["dotnet", "API.dll"]
