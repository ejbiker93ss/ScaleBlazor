# 01-prerequisites: Verify toolchain and runtime upgrade readiness

Validate the local and solution-level prerequisites for a .NET 10 upgrade across all projects before applying any project file changes. This includes confirming .NET 10 SDK availability, checking global.json compatibility (if present), and confirming baseline restore/build behavior on the current branch.

The goal of this task is to ensure the upgrade can proceed in one atomic pass without toolchain surprises and to capture any prerequisite adjustments needed before touching TFMs or package versions.

**Done when**: .NET 10 SDK readiness is validated, any global.json constraints are resolved or documented, and baseline restore/build prerequisites are confirmed for the solution.
