When this command is invoked, run a brainstorming cycle for a new feature or subproject spec.

Steps:
1. Ask: "What feature or subproject do you want to spec?" — one sentence answer
2. Read: CLAUDE.md (root), relevant per-module CLAUDE.md, concept doc at docs/superpowers/specs/2026-05-03-travel-platform-concept.md
3. Ask clarifying questions ONE AT A TIME: purpose, scope, constraints, success criteria
4. Propose 2-3 implementation approaches with trade-offs and a recommendation
5. Present the design section by section, asking "ok?" after each
6. Write the spec to docs/superpowers/specs/YYYY-MM-DD-{topic}-design.md
7. Run spec self-review: placeholders, contradictions, ambiguity, scope
8. Commit: git add docs/superpowers/specs/... && git commit -m "docs: add spec {topic}"
9. Ask user to review before transitioning to implementation

Rules:
- One question at a time — never ask multiple questions in one message
- No implementation until spec is written and user approves
- Every spec must reference the concept doc and not contradict it; if it does, flag the contradiction
