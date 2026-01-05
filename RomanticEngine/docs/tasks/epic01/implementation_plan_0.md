# Implementation Plan

## Project Baseline and Build Hygiene <!-- id: 0 -->

**Problem**: The repository builds and tests today, but it’s easy to accidentally introduce silent correctness regressions (nullability mismatch, inconsistent compiler settings, drifting package versions, test harness not stable). We need a reliable “floor” so every subsequent change is verifiable and repeatable. This covers tasks **1–4**.

**Correctness Conditions**:

1. `dotnet build` succeeds for the full solution on a clean checkout without manual steps.
2. `dotnet test` succeeds reliably (no flaky “sometimes fails” tests).
3. Core logic (`RomanticEngine.Core`) and the console app (`RomanticEngine`) maintain clear separation: Core contains UCI/state/search logic; Console contains stdin/stdout plumbing only.
4. Compiler settings (nullable, implicit usings, language version, warnings) are consistent across projects so we don’t “fix” a bug in one project and reintroduce it in another via different rules.

**Proposed Changes**:

* Audit the solution and project boundaries and document the intended structure so we stop drifting. <!-- id: 1 -->

  * Confirm: `RomanticEngine.Core` = engine/search/state/options/UCI adapter; `RomanticEngine` = minimal UCI loop wiring; `RomanticEngine.Tests` = unit/integration tests.
  * Confirm the console app does **not** implement protocol parsing itself (that becomes a core responsibility later; but we at least pin the intent here).

* Pin dependencies and make package resolution deterministic. <!-- id: 2 -->

  * Ensure `Rudzoft.ChessLib` is referenced from the correct project(s) and is pinned to an explicit version.
  * Ensure test project references are explicit.
  * If multiple projects need the same package, prefer a single consistent version (avoid “Core uses X, Tests uses Y”).

* Unify compiler/runtime settings across all projects. <!-- id: 3 -->

  * Turn on nullable reference types consistently (either via `Directory.Build.props` or per-csproj, but *one* consistent approach).
  * Make sure warning behavior is consistent (we should not have Core “warning-clean” but Tests full of warnings that mask real issues).
  * Ensure language version/framework target is consistent and explicit.

> [!IMPORTANT]
> Do **not** “fix” build issues by swallowing exceptions or disabling warnings wholesale. We want problems to surface early, not get buried.

**Verification Plan**:

* Add a minimal “engine can be constructed” smoke test that runs under the test runner, not manually. <!-- id: 4 -->

  * Construct `Engine`.
  * Assert it exposes a non-empty `Options` list.
  * (If construction is currently expensive, keep it as a single test and avoid repeating in every suite.)

---
