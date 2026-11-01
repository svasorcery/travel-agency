# Compiler metadata lifetime — implementation plan

> Execution: existing isolated worktree, superpowers:executing-plans, one fresh final review. Source/tests only; no Host/DB/load/UI/ETW/provider/model run, stage/commit/push. Canonical OpenSpec continuation of task2.3, not a new acceptance task.

**Goal:** stop per-compilation full-DLL native metadata from surviving Emit through GC-dependent references.
**Architecture:** Host-owned implementation of existing JasperFx IAssemblyGenerator. Keep roots/code generation/load-context semantics; create disposable AssemblyMetadata owners within each Generate/Compile call and release them in finally. No process-wide metadata cache, reflection cleanup, forced GC or dependency bump.
**Tech stack:** existing .NET10, JasperFx2.0.0/RuntimeCompiler5.0.0, Roslyn5.0.0,xUnitv3/Shouldly.
**Spec:** current proposal/design/task2.3 plus native-heap-diagnostic-v2-result.md. H6 exact managed caller remains unproven; source fix addresses the verified API lifetime mechanism, runtime memory benefit remains unaccepted.

## Evidence / design decisions

Roslyn exact commit6c4a46a documents eager whole-image native copy in MetadataReference.CreateFromFile, lifetime tied to unreachable reference/GC. PortableExecutableReference.GetMetadata returns a non-owning copy; disposing it cannot release the owner. AssemblyMetadata.CreateFromFile/GetReference/Dispose provides ownership and lazy metadata reading. Three MetadataOwnershipTests pass against installed5.0.0 without GC or Host.

Options considered: (1) process cache retains long-lived images/needs invalidation; (2) pre-generated Static mode changes deployment/codegen workflow; (3) scoped metadata ownership uses existing compiler seam and does not retain images after Emit. Choose3. This is the executor's implementation choice within the authorized source step. Global Host composition is appropriate because Marten/Wolverine compilation is process-wide, not a Hotels domain concern.

## Task1 — library contract (complete)

- [x] Add and run3actual Roslyn ownership tests: distinct eager references, non-owning copy Dispose, explicit owner sharing/release.
- [x] Preserve H6frozenZIP and272historical/currentraw evidence; no new runtime.

## Task2 — scoped compiler (TDD)

Files: apps/Travel.Host/CodeGeneration/ScopedMetadataAssemblyGenerator.cs; tests/Travel.Host.Tests.Integration/CodeGeneration/ScopedMetadataAssemblyGeneratorTests.cs; Hostcsproj test friend.
Consumes IAssemblyGenerator,GeneratedAssembly,GenerationRules,IServiceVariableSource,SourceWriter,CustomAssemblyLoadContext and real Roslyn AssemblyMetadata. Produces existing interface behavior with per-call owned metadata.
- [x] RED: successful Generate leaves owners disposed while emitted type executes; compilation/reference failures dispose prior owners; repeated/concurrent calls have independent owners; explicit Generate name and SourceWriter path preserved; Compile attaches GeneratedAssembly and returns source.
- [x] GREEN: per-call scope, distinct paths, same supported dependency traversal and optional missing-platform assembly handling; no inherited private-field mutation, GC or long-lived metadata list. Keep loaded emitted assembly alive via existing noncollectible context behavior.
- [x] Verify focused tests and formatting.

## Task3 — Host composition / final review

Files: apps/Travel.Host/CodeGeneration/RuntimeCompilationServices.cs; Program.cs one registration after Wolverine; focused composition tests.
- [x] RED/GREEN: replace ordinary upstream IAssemblyGenerator registration with one singleton whose calls own independent scopes; same interface consumer executes real generated type. No Host startup needed.
- [x] Run all new compiler tests, relevant permitted Hotels standalone, architecture boundary checks and wrapper build. No DB/service suites or load.
- [x] Fresh read-only gpt-6-astra/high review; one TDD fix pass for Critical/Important, minor ledger, no re-review. Report source readiness separately from runtime memory acceptance. Keep6/8.

## Review focus

Ownership on syntax/metadata/load/attach failures and no double ownership; generated-type/dependency/name semantics; concurrency without shared disposable metadata; late DI registration/default upstream compiler bypass; single-file/dynamic/missing references must keep explicit bounds; no new runtime claim or historical hash overwrite.

## Authority / retained decisions

Existing isolated branch/basec3fd3a6 is retained; index stays empty. User asked to continue lifetime investigation and delegated correction of assistant errors. No new load or UI, thresholds unchanged. No commits/worktree ledger deletion because publication/commit was not requested. A runtime rerun is a later step; source tests do not close3.2/3.3.