---
name: migration-authoring
description: Design, generate, and review safe EF Core source migrations for approved model changes. Use for schema changes, migration safety, generated SQL review, rollback, and deployment notes; never apply a database implicitly.
---

Canonical Travel workflow ID: travel-agency/migration-authoring.

# Migration Authoring

1. Read `docs/adr/0007-storage-strategy-marten-ef-coexistence.md`. Inspect the actual DbContext, design-time factory, migration history, provider, and output path.
2. Plan safe add, backfill, constraint, rename, and data-preservation behavior. Generate migration source, snapshot, or SQL only when requested.
3. Review `Up` and `Down`, generated SQL, rollout order, rollback, locking, compatibility, and data-loss risk. Use one-line commands, not Bash continuations.
4. Do not run `dotnet ef database update` implicitly.

## Authority

Invoking or auto-loading this skill does not grant additional authority. Follow the user's requested scope. Do not infer permission to stage, commit, push, create or switch branches, generate or apply a migration, deploy, or mutate an external system. A design or review request does not authorize implementation-file edits.
