# Upgrade Options — ScaleBlazor

Assessment: 3 SDK-style net7.0 projects (Blazor WebAssembly + ASP.NET Core + shared library), 6 package upgrades recommended, 90 source-incompatible and 17 behavioral API changes.

## Strategy

### Upgrade Strategy
The solution is already on modern .NET with a small project count and low per-project complexity, so a single coordinated pass is suitable.

| Value | Description |
|-------|-------------|
| **All-at-Once** (selected) | Upgrade all projects together in one coordinated pass, then validate the full solution. |
| Top-Down | Upgrade entry applications first and keep shared libraries temporarily multi-targeted until consolidation. |

## Compatibility

### Unsupported API Handling
Assessment detected platform API changes, so this option controls how any complex replacement work is handled during execution.

| Value | Description |
|-------|-------------|
| **Fix Inline** (selected) | Resolve all API changes in the same task, including complex replacements, without deferring work. |
| Defer Complex Changes | Apply simple replacements now, add compilable stubs for complex cases, and create follow-up resolution subtasks. |
