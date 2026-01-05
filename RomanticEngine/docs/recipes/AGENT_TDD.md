Some failing tests have been added in TDD fashion. Your goal is to make all tests pass by following the implementation plan in the sub-tasks.

1. Read each top-level task one at a time from @`task.md`.
2. For each top-level task, diligently investigate the problem and follow the implementation plan in the task's sub-tasks.
3. Never modify or delete any of the existing tests unless the task explicitly asks you to do so.
4. Run `dotnet test` to ensure the test you're investigating now passes.
5. Repeat this process until you have completed all tasks.

> [!IMPORTANT] You must THOROUGHLY follow the instructions from the implementation plan in the sub-tasks. Do not deviate without an exceptionally good reason and empirical evidence.

* When you have successfully completed all tasks and all tests are passing, run `git add . && git commit -m "<your commit message>"`.
* Structure your commit message like this: `fix: <what you fixed>; model: Antigravity, Gemini 3 Flash (Fast)`.