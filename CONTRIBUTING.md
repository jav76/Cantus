# Contributing to Cantus

Thank you for your interest in contributing to Cantus! 

For the comprehensive, full-length guide on project architecture, debugging, and local environment setup, please visit the [Official Contributing Guide](https://cantus.docs.jav26122.net/contributing/).

---

## Quickstart for Contributors

### 1. Prerequisites
- **[.NET 10 SDK](https://dotnet.microsoft.com/)** (`v10.0.100+`)
- **[Uno Platform wasm-tools](https://platform.uno/)**: `dotnet workload install wasm-tools`
- **[Spotify Developer Account](https://developer.spotify.com/dashboard)** (Free Client ID for local API testing)

### 2. Fork & Clone
```bash
git clone https://github.com/<your-username>/Cantus.git
cd Cantus
git checkout -b feature/my-feature
```

### 3. Build & Test
```bash
# Build solution
dotnet build Cantus.slnx

# Run all test suites
dotnet test Cantus.slnx

# Format code
dotnet format Cantus.slnx
```

### 4. Running Locally
```bash
# Start ASP.NET Core backend server
dotnet run --project src/Cantus.Server

# Run Desktop Client
dotnet run --project src/Cantus.Client/Cantus.Client/Cantus.Client.csproj -f net10.0-desktop
```

---

## Coding Standards

Cantus adheres to strict C# conventions:
- **No `var`**: Always use explicit types (`string title = "..."`, `int count = 0`).
- **Target-typed `new()`**: Use `List<LyricLine> list = new();` when the type is declared on the left.
- **File-scoped namespaces**: `namespace Cantus.Core;`.
- **Allman bracing**: Opening `{` on its own line.
- **Structured logging**: Never interpolate strings into logger calls (use `_logger.LogInformation("Message {Param}", param)`).

---

## Submitting a Pull Request

1. Verify that your code builds cleanly (`dotnet build Cantus.slnx --configuration Release`).
2. Verify that all tests pass (`dotnet test Cantus.slnx`).
3. Run code formatting (`dotnet format Cantus.slnx`).
4. Submit your PR against the `main` branch with a clear title and description of your changes.

For full details, please read our [Contributing Documentation](https://cantus.docs.jav26122.net/contributing/).
