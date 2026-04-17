# 02-upgrade-solution: Upgrade all projects and dependencies to .NET 10

Upgrade all projects in the solution together: update target frameworks to net10.0, apply recommended NuGet package version updates, and resolve source-incompatible and behavioral API issues identified in the assessment. This task includes Shared, Client (Blazor WebAssembly), and Server updates as one coordinated change set.

Assessment context shows low per-project complexity but non-trivial API issue volume in Server and Client, especially around System.IO.Ports and related runtime behavior changes. This task should resolve API changes inline rather than deferring compatibility work.

**Done when**: All solution projects target .NET 10, package updates are applied, restore succeeds, and compilation completes without unresolved upgrade-related errors.
