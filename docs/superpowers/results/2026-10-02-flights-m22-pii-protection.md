# Flights M2.2 verification and delivery

Date: 2026-10-02. Approved scope: protected new one-passenger snapshots/inbox, compatible replay/projection, private key lifecycle, failure privacy and memory-only Travel booking intent. Base freshly fetched `origin/dev`: `11cbb657f3cfe4f24a2d712be0c1246e1522378c`, descended from `d75186054a33de0e1cb688da7e3e7cc42927dadf`. No archived worktree restored.

## Local evidence

All inputs and keys are fictional. No local schema creation/application, migration generation, Host/AppHost startup, deployment, real supplier/payment/email, Anthropic or paid AI eval occurred. Selected fixtures were inspected first. SMTP privacy tests use an owned loopback rejecting server; crypto/CLI tests own disposable directories/certificates. Full database/durable suites are CI-only.

| Check | Result |
| --- | --- |
| Flights unit, Release | 511 passed |
| Selected no-DB HTTP and key-only command tests | 44 passed |
| Serialization-only `BookingEventCompatibilityTests` | 5 passed |
| Architecture | 167 passed |
| Contract | 9 passed |
| Angular / API client | 236 / 63 passed |
| `web`, `api-client`, `travel-e2e` lint | Passed |
| Fictional demo browser | 14 passed; blocked external traffic, memory/storage and keyboard/mobile assertions |
| Full Release solution build | Passed; existing ASP.NET analyzer AD0001 warnings remain |

RED/GREEN evidence: empty crypto implementation refused valid initialization; key CLI initially returned 3 instead of 0; V2 replay remained None; hold command contained the passenger sentinel; V2 projection was unsupported; webhook codec failed roundtrip; malformed legacy field shape was incorrectly acknowledged; payment body/SMTP chains leaked sentinels; callback consumed a persisted draft and storage failure blocked login; malformed decrypted passenger shapes were accepted; missing legacy command envelopes reached dependencies; invalid gender input was echoed in ProblemDetails. Each behavior received a regression test and passed after implementation. The unknown-hold → privacy-503 retry preserves its original key/body barrier.

Database acceptance tests cover actual V2 event storage/replay, copied projection ciphertext through checkpoint catch-up/reset, encrypted ingress/dedup/unavailable no-commit, and unavailable-key inbox recovery with one event/notification. These were compiled locally, not run against a local database. Auth source/realm/JWT contract is unchanged; fake callback tests are not proof of newly issued real tokens.

## Implementation rulings

- The ASP.NET shared-framework reference makes three local Microsoft.Extensions references redundant (NU1510); remove those duplicate references while leaving central versions unchanged. Consumers now use the shared framework assemblies.
- Key initialization stays on the owned crypto provider instead of a redundant separate initializer class. The Api facade and early Host CLI retain the planned boundary. Test ring source is linked into three test assemblies, leaving Shared.TestInfrastructure PostgreSQL-only.
- Normalized webhook facts share the port file. Safe existing BookingProjectionTransient/TerminalException categories carry constant PII codes and the existing bounded retry/DLQ policy; no duplicate exception family, policy engine or analyzer suppression.

Desktop and 360px demo screenshots were visually inspected: content readable, no horizontal overflow. Production and flights-demo frontend builds passed. README examples, .NET inventory, CSharpier and Biome checks passed; baseline warnings/info remain. Harness validation caught an incorrect CLAUDE adapter edit; restored exact same-directory imports instead of weakening the check.

## Independent review

A fresh read-only reviewer found one Important issue and no Critical/Minor findings: a protected inbox envelope missing its discriminator could be treated as a legacy no-op. The codec/reader regression failed first, then passed after rejecting reserved envelope members without a valid discriminator; the CI handler test also requires no acknowledgement/event/notification. No other source blocker was found. Production custody/recovery and real provider/OIDC validation were explicitly declined as excluded operations: local results do not claim that proof; using this demo for those purposes would need separate acceptance.

The review package used the staged diff against the fresh base because the authorized sequence requires review before commit. It included all new files; the committed diff remains subject to mandatory CI.

## Delivery boundary

Publication and merge require all non-paid CI checks on the actual PR head; the PR checks and merge record supply that later evidence. This report records local verification, not an assertion that database acceptance or merge happened locally. Operational key initialization/restore/deployment was documented, not executed. Historical plaintext and unkeyed idempotency fingerprints remain explicit limitations, not newly protected history.
