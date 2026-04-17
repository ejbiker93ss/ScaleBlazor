# .NET Version Upgrade Plan

## Overview

### Selected Strategy
**All-At-Once** — All projects upgraded simultaneously in a single operation.
**Rationale**: 3 projects, all on .NET 7, SDK-style, low dependency depth, and straightforward package/API upgrade signals.

**Target**: Upgrade the ScaleBlazor solution from .NET 7 to .NET 10 (LTS).
**Scope**: 3 projects (Client, Server, Shared), ~2.3k LOC, 6 package upgrades recommended, and API compatibility fixes concentrated in Server and Client.

## Tasks

### 01-prerequisites: Verify toolchain and runtime upgrade readiness

Validate the local and solution-level prerequisites for a .NET 10 upgrade across all projects before applying any project file changes. This includes confirming .NET 10 SDK availability, checking global.json compatibility (if present), and confirming baseline restore/build behavior on the current branch.

The goal of this task is to ensure the upgrade can proceed in one atomic pass without toolchain surprises and to capture any prerequisite adjustments needed before touching TFMs or package versions.

**Done when**: .NET 10 SDK readiness is validated, any global.json constraints are resolved or documented, and baseline restore/build prerequisites are confirmed for the solution.

---

### 02-upgrade-solution: Upgrade all projects and dependencies to .NET 10

Upgrade all projects in the solution together: update target frameworks to net10.0, apply recommended NuGet package version updates, and resolve source-incompatible and behavioral API issues identified in the assessment. This task includes Shared, Client (Blazor WebAssembly), and Server updates as one coordinated change set.

Assessment context shows low per-project complexity but non-trivial API issue volume in Server and Client, especially around System.IO.Ports and related runtime behavior changes. This task should resolve API changes inline rather than deferring compatibility work.

**Done when**: All solution projects target .NET 10, package updates are applied, restore succeeds, and compilation completes without unresolved upgrade-related errors.

---

### 03-final-validation: Run full build and test validation for the upgraded solution

Perform full-solution validation after the atomic upgrade is complete. Execute restore/build/test verification and address any remaining warnings or behavioral regressions caused by framework and package changes.

This final gate ensures the upgraded solution is stable and ready for commit as a single coherent migration outcome.

**Done when**: Solution restore/build/tests complete successfully, warnings in modified projects are resolved, and final task artifacts capture outcomes and any follow-up recommendations.
