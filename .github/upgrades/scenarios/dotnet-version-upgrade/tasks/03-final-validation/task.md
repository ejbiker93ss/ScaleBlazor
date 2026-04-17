# 03-final-validation: Run full build and test validation for the upgraded solution

Perform full-solution validation after the atomic upgrade is complete. Execute restore/build/test verification and address any remaining warnings or behavioral regressions caused by framework and package changes.

This final gate ensures the upgraded solution is stable and ready for commit as a single coherent migration outcome.

**Done when**: Solution restore/build/tests complete successfully, warnings in modified projects are resolved, and final task artifacts capture outcomes and any follow-up recommendations.

## Research Findings

- This task scope is full-solution validation after all-at-once upgrade completion.
- Build orchestration skill guidance confirms `dotnet build`/`dotnet test` are appropriate because projects are SDK-style modern .NET projects.
- Prior task already updated all project TFMs and packages; this task verifies final restore/build/test state and warning cleanliness in modified projects.
