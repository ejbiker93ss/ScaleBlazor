# 02-upgrade-solution: Upgrade all projects and dependencies to .NET 10

Upgrade all projects in the solution together: update target frameworks to net10.0, apply recommended NuGet package version updates, and resolve source-incompatible and behavioral API issues identified in the assessment. This task includes Shared, Client (Blazor WebAssembly), and Server updates as one coordinated change set.

Assessment context shows low per-project complexity but non-trivial API issue volume in Server and Client, especially around System.IO.Ports and related runtime behavior changes. This task should resolve API changes inline rather than deferring compatibility work.

**Done when**: All solution projects target .NET 10, package updates are applied, restore succeeds, and compilation completes without unresolved upgrade-related errors.

## Scope Inventory

- **Projects affected**:
  - `ScaleBlazor/Shared/ScaleBlazor.Shared.csproj`
  - `ScaleBlazor/Client/ScaleBlazor.Client.csproj`
  - `ScaleBlazor/Server/ScaleBlazor.Server.csproj`
- **Distinct concerns**:
  - Target framework updates from `net7.0` to `net10.0` across all projects.
  - NuGet package version updates in Client and Server.
  - Inline resolution/validation of API compatibility issues surfaced for Server (SerialPort-heavy) and Client (HTTP/TimeSpan/Uri-related behaviors).
- **Dependency and package signals**:
  - No Central Package Management detected; package versions are defined directly in each `.csproj`.
  - Client recommended package updates: `Microsoft.AspNetCore.Components.WebAssembly`, `Microsoft.AspNetCore.Components.WebAssembly.DevServer`, `Microsoft.Extensions.Http`.
  - Server recommended package updates: `Microsoft.AspNetCore.Components.WebAssembly.Server`, `Microsoft.EntityFrameworkCore.Sqlite`, `System.IO.Ports`.
  - Shared requires TFM update only.

## Research Findings

- Assessment project summaries and issue lists were queried for all three projects.
- Shared project has 1 mandatory issue (TFM change) and no package/API issues.
- Client project has 22 issues total (3 package update recommendations, 2 source-incompatible signals, 16 behavioral-change signals, plus TFM change).
- Server project has 93 issues total (3 package update recommendations, 88 source-incompatible signals, 1 behavioral-change signal, plus TFM change), with high concentration in `Services/ScaleReaderService.cs` around `System.IO.Ports` usage.
- Current project files confirm all three projects are SDK-style and currently target `net7.0`, so no SDK-style conversion is required.
