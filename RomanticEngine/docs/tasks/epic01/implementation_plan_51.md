# Implementation Plan

## Options and Configuration Correctness <!-- id: 51 -->

**Problem**: We currently advertise options but do not consistently store/apply them (e.g., Hash not persisted), and we risk emitting option lines that don’t match required spec (defaults/min/max). This breaks GUI expectations and makes the engine hard to operate. We need correct option definitions, correct parsing/storage/validation, and predictable output backed by strict tests. This covers tasks **52–60**.

**Correctness Conditions**:

1. The `uci` handshake emits **exactly** the required option lines (name/type/default/min/max), stable ordering, and no duplicates.
2. `setoption` correctly updates configuration for supported options and rejects invalid values safely (no exceptions, no silent corruption).
3. `Threads` and `Hash` have correct validation:

   * `Threads` ∈ [1, maxThreads]
   * `Hash` ∈ [1, maxHashMb]
4. Hash value is persisted in configuration even if actual TT resize logic is not yet implemented.
5. `Clear Hash` (button) triggers a defined action (even if minimal) and is observable (e.g., via `info string cleared hash`).
6. `MultiPV` behavior is not misleading:

   * either implement it, or if not implemented, clearly degrade to 1 while keeping the option stable per spec
7. String/path options treat `<empty>` and empty consistently (normalized and predictable).

**Proposed Changes**:

* Ensure advertised option lines exactly match the spec and are produced from a single source of truth. <!-- id: 52 -->

  * Requirements:

    * Do not build option lines via ad-hoc string concatenation scattered around the code.
    * Use option definition objects (`OptionDefinition`) that include:

      * Name (exact string)
      * Type (`spin`, `string`, `check`, `button`)
      * Default
      * Min/Max (if spin)
    * Emit options in a stable order (choose and lock it down; tests will enforce).

* Implement correct storage and validation for `Threads`. <!-- id: 53 -->

  * Must-haves:

    * Determine `maxThreads` from a single system-info provider (see below).
    * Validate value is an integer and within range.
    * Store the value in configuration immediately on success.
    * If invalid:

      * do not change the current value
      * emit `info string invalid Threads value: <x>` (operator-friendly)

* Implement correct storage and validation for `Hash`. <!-- id: 54 -->

  * Must-haves:

    * Determine `maxHashMb` from a single system-info provider.
    * Validate value is an integer and within range.
    * Store value in configuration immediately.
    * If resize logic is not implemented yet, store anyway and optionally emit:

      * `info string Hash resize deferred (not implemented yet)`
    * Do not leave TODOs that silently ignore the new value.

* Ensure Hash value is persisted even if resize is deferred. <!-- id: 55 -->

  * This is specifically to prevent the “GUI set Hash=1024, engine still uses 16” situation.
  * Persist means:

    * configuration field updates
    * `uci` output reflects the default, not necessarily the last set value (UCI typically shows default; runtime state is separate), but the engine should actually operate with the configured value.

* Implement `Clear Hash` button behavior. <!-- id: 56 -->

  * Define the action clearly:

    * If a transposition table exists: clear it
    * If not: clear any existing search caches/history you have, and still emit `info string cleared hash`
  * Do not implement as a silent no-op.

* Align `MultiPV` option behavior with search reality. <!-- id: 57 -->

  * You have two acceptable paths; pick one explicitly and document it:

    1. **Implement minimal MultiPV now** (more work, but truthful): search top N root lines and emit `info multipv i ... pv ...` for each.
    2. **Degrade gracefully** (simpler, but must be explicit):

       * accept and store the option value
       * if value > 1 and MultiPV isn’t implemented, force effective value to 1 for search
       * emit `info string MultiPV > 1 not implemented; using 1`
  * Do not silently ignore the user’s setting.

* Normalize `<empty>` and empty-string handling for file/path options. <!-- id: 58 -->

  * Concrete rule:

    * If user sets value to `<empty>` or provides no value, store `""` as the canonical “disabled” state.
  * Apply this uniformly to:

    * Debug Log File
    * SyzygyPath
    * any future path options

* Make system-dependent maxima testable via injection (avoid brittle tests). <!-- id: 52 -->

  * Create a small `ISystemInfo` abstraction used only for:

    * `MaxThreads`
    * `MaxHashMb`
  * Production implementation reads from environment/runtime.
  * Tests use a fake with fixed values so golden output is stable.

> [!IMPORTANT]
> Do not write tests that “expect whatever the machine currently reports” for Threads/Hash maxima.
> That produces non-reproducible tests and encourages people to loosen assertions. Inject fixed system info in tests instead.

**Verification Plan**:

* Add `setoption` behavior tests: updates, validation, and graceful rejection. <!-- id: 59 -->

  * For each supported option, assert:

    * valid value updates config
    * invalid value does not update config
    * invalid value emits `info string …` (if we choose diagnostics)
    * no exceptions are thrown

* Add strict golden tests for `uci` option output, including exact option lines. <!-- id: 60 -->

  * Use the fake `ISystemInfo` to force:

    * `Threads max 28`
    * `Hash max 120395`
  * Assert the emitted lines exactly match the expected strings and ordering (no `Contains` shortcuts).
  * Include a regression assertion that `uciok` occurs exactly once and at the end.

---
