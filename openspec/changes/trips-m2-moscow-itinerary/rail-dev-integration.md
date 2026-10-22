# Rail dev integration and dependency receipt

2026-10-21, source/local scope only. The user expressly authorised extending Trips with Rail and pulling its accepted dev changes, then selected concrete train selection followed by adding an ordinary intention into the editor and explicit Save. Source and fictional demo are supported OSS scenarios; real source activation and supplier-data persistence rights remain separate.

## Source identity and preservation

- Original freshly fetched implementation base: `e654c76915103a82083ef2abc01c0579117c9749`.
- Current upstream/HEAD after ordinary `git merge --ff-only --no-edit --quiet origin/dev`: `a8721effd9056459d217966083816455fbea599d`.
- Branch: `codex/trips-m2-moscow-itinerary`, original base remains an ancestor, verified again after dependency setup.
- No stage, feature commit, reset/rebase/force/stash, push or enablement. Primary D checkout was not switched or edited.
- Pre-sync backup: 695 owned source/evidence files, 20,353,984 bytes, create-only copy with SHA verification. Before/after reviewable WIP inventory: the same 139 paths and bytes; upstream25 paths and WIP32 tracked paths had zero overlap. Later planning/new Rail edits are outside that historical comparison.
- Backup, actual human authorisation excerpts, working-tree.patch and local-dev-integration-receipt.json: `D:/_Projects/_github/travel-agency/TestResults/trips-m2-pre-rail-sync/20261021T1439423939Z/`. Backup manifest SHA256: `edec135a552a79c24eef5e6760540680ca818684dfe10bd333062d9f930f54b5`.
- Pre-Rail S/browser/build/Q-harness-build receipts remain evidence for their own source identities. They do not prove the new Rail consumer or changed dependency closure. Affected current-source acceptance belongs to4.5–4.8 and the final independent audit.

## Dependency installation and local checks

Owned dependency root: `D:/_Projects/_github/travel-agency/TestResults/trips-m2-dependencies-a8721eff`.

Current package.json SHA256: `9bdf9692353206cdfb6b1d44f37eb96903051374522e1fda61b2a3f415d2a6e8`; package-lock.json: `69a09b317c29e502b81487e1c1853ae0cfa5f0e3eb31940a873fb20cd9b8766b`.

`npm.cmd ci --ignore-scripts --no-audit --no-fund` installed1481 packages from exact copied manifest/lock and verified local tarball. No lifecycle hooks, force/legacy-peer options or lock edits. Old e654 cache retained. Actual nested SDK1.32.0, Angular builder Vite7.3.7, root/Vitest/mocker Vite8.3.4; local Node26.11.1 differs from CI Node22.

The initial root node_modules junction was convenient for disk space but the unchanged installed-backport verifier rejects links. The first local security attempt recorded40pass/1file-load failure. With C free3.84GB, replaced ONLY the verified owned junction with a physical copy in the same worktree:59,458 files/741,458,297 bytes, every copied SHA checked, D source retained; C free2.99GB after copy, minimum1.5GiB preserved. Copy manifest SHA256 `37535d5d35fb050a2984ade4a2ae295aac4bedaaeb6a0fe7064632f79f2c263c`. This satisfies the physical-tree rule; no verifier/allowlist changes. No primary dependency tree reuse.

| Check | Result | Raw log SHA256 |
|---|---|---|
| Existing dependency consumer suite, fictional memory/loopback only |7/7 pass,0skip|`b069ccb197912d63e658dec66ce6cd110c1f7cd7f9421750bd0d547683aa30d4`|
| Existing local audit-validator tests (no network) |15/15 pass,0skip|`189d3142765d13de1ea7c9d341dc3be1f4fc5449cce0587e11efca855888259d`|
| Existing security/backport tests on physical installation |60/60 pass,0skip|`cf67f0852c9b3902027277213d39ada598bb4352de1324a5539dadfc8f5ce3c1`|
| Artifact-only gate through official npm entrypoint |exit0, lock/tarball only; no installed proof from this command|`0b2fba78c4274f4354aaa53318546c08a8ef54e3e579812155b7ca61f30702d3`|

The preliminary direct-node artifact command failed because npm_execpath is required; raw diagnostic retained. Official npm invocation was then used. Despite its name, artifact-only also invokes public npm audit; this earlier approved execution completed with18root/4OpenSpec entries matching the existing approved backport policy, and its raw reports were retained separately. Those carrier counts are not advisory exceptions or an upstream fixed-release claim.

A later full check:dependencies command was rejected BEFORE execution by automatic approval review because public audit sends dependency graph metadata to registry.npmjs.org without specific human permission. An explicit async request is pending; no indirect audit retry, policy bypass or invented complete-gate success. The local-only suites above do not invoke audit. Raw outputs and metadata are in the owned dependency root. Full fresh network/ordinary CI acceptance remains separate; source work continues.

## Pending source work

New Rail planning and independent review, reusable picker and Trips consumer, fictional browser acceptance, current-source structural checks, then pre-output review/frozen Q_demo run and fresh independent full-source audit/fixes. Original quality-cases bytes/thresholds and immutable Moscow package remain unchanged; no Q/S results observed at this receipt. No real provider, model, Host, DB, keys, schema, migration or target action.