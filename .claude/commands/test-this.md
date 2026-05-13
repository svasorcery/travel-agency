When this command is invoked, write tests for the current file.

Steps:
1. Identify target:
   - If a file path is provided as argument: use it
   - Otherwise: use the most recently edited .cs or .ts file (git diff HEAD --name-only | head -1)
   - If still unclear: ask "Which file should I write tests for?"
2. Read the file
3. Determine test type:
   - Value object, aggregate, domain service → unit test
   - Wolverine handler, EF query, Marten projection, provider adapter → integration test
   - Angular component, service → Vitest unit test
4. Invoke the test-author agent with the file path and determined test type
5. Show the generated test file content to the user before writing
6. On approval: write to the correct test project path
