# ==============================================================================
# Stage 1: Build Uno Platform WebAssembly Frontend
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-wasm
WORKDIR /src

# Install python3 and ensure `python` is in PATH (required by Emscripten/emcc toolchain) and wasm-tools workload
RUN apt-get update && apt-get install -y --no-install-recommends python3 python-is-python3 \
    && ln -sf /usr/bin/python3 /usr/bin/python \
    && rm -rf /var/lib/apt/lists/*
RUN dotnet workload install wasm-tools

# Copy solution files and project descriptors for caching
COPY global.json ./
COPY Cantus.slnx ./
COPY src/Cantus.Client/Directory.Build.props src/Cantus.Client/
COPY src/Cantus.Client/Directory.Packages.props src/Cantus.Client/

# Copy Core and Client source code
COPY src/Cantus.Core/ src/Cantus.Core/
COPY src/Cantus.Client/ src/Cantus.Client/

# Publish WASM Client
RUN dotnet publish src/Cantus.Client/Cantus.Client/Cantus.Client.csproj \
    -f net10.0-browserwasm \
    -c Release \
    -o /app/wasm_publish

# ==============================================================================
# Stage 2: Build ASP.NET Core 10 Server Backend
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-server
WORKDIR /src

COPY global.json ./
COPY Cantus.slnx ./
COPY src/Cantus.Core/ src/Cantus.Core/
COPY src/Cantus.Infrastructure/ src/Cantus.Infrastructure/
COPY src/Cantus.Server/ src/Cantus.Server/

# Publish ASP.NET Core Web API / SignalR Host
RUN dotnet publish src/Cantus.Server/Cantus.Server.csproj \
    -c Release \
    -o /app/server_publish

# ==============================================================================
# Stage 3: Lightweight Production Runtime Image
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Configure default container environment
ENV ASPNETCORE_URLS=http://+:5000 \
    ASPNETCORE_ENVIRONMENT=Production \
    DATA_DIR=/app/data \
    DOTNET_RUNNING_IN_CONTAINER=true

# Expose default HTTP port
EXPOSE 5000

# Create persistent state directory and static web root
RUN mkdir -p /app/data /app/wwwroot

# Copy server binaries
COPY --from=build-server /app/server_publish .

# Copy compiled Uno WASM frontend assets into server wwwroot
COPY --from=build-wasm /app/wasm_publish/wwwroot/ ./wwwroot/

# Declare persistent storage mount for SQLite & DataProtection keys
VOLUME ["/app/data"]

ENTRYPOINT ["dotnet", "Cantus.Server.dll"]
