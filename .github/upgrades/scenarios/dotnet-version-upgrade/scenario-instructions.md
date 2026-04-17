# .NET Version Upgrade

## Preferences
- **Flow Mode**: Automatic
- **Target Framework**: .NET 10.0 (LTS)

## Source Control
- **Source Branch**: master
- **Working Branch**: dotnet-version-upgrade-net10
- **Commit Strategy**: Single Commit at End

## Upgrade Options
**Source**: .github/upgrades/scenarios/dotnet-version-upgrade/upgrade-options.md

### Strategy
- Upgrade Strategy: All-at-Once

### Compatibility
- Unsupported API Handling: Fix Inline

## Strategy
**Selected**: All-At-Once
**Rationale**: 3 projects, all SDK-style on modern .NET (net7.0), low migration complexity, and no incompatible packages.

### Execution Constraints
- Upgrade all projects in one coordinated pass; do not introduce tiered or phased ordering.
- Apply project and package updates before restore/build validation.
- Resolve API changes inline within the upgrade task; do not defer with stubs.
- Run full solution build and test validation after the atomic upgrade completes.

## Key Decisions Log
- Selected All-at-Once strategy for this upgrade.
