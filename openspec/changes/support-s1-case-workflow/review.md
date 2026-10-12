# Review — historical plan and current whole source

## Current implementation review — 2026-10-12

Independent read-only `/root/support_source_review` first reviewed stable production source, then every root integration piece including demo/browser/CI/inventory/migration/SQL/key-only command/generic architecture/OpenAPI candidate. Baseaf725448e6632708d5129971f8570c1d047f4233, uncommitted tracked+untracked S1 source. Reviewer did not edit or run runtime/tests. Root executed the safe checks in [verification](verification.md); current scenario map covers38cases. Final recheck:0openCritical/Important.

| Finding | Resolution and evidence | Status |
| --- | --- | --- |
| Browser: nonempty queue callback passed Array.map index as expectedId | Explicit `(value)=>decodeSupportCase(value)`; regression RED→GREEN,198client and67browser checks passed | Closed |
| Important: controlled HostHealthFactory omitted new Support dependency, so disabled initializer/unconfigured keys would make dependency endpoint503 in healthy tests | Added explicit `Replace(options,"support",Healthy)` only to controlled health fixture; production fail-closed Support health unchanged. Reviewer recheck closed; actual Host test remains CI | Closed in source |
| D7/task3.3 coverage gap: persisted title AND messages after actual certificate rotation not jointly tested | New EF regression creates old ciphertext, restarts/rotates with retained reader, writes new reply, rereads both generations, checks sentinel absence, removes old reader and preserves metadata receipts without decrypt. Reviewer recheck closed; compile-only here | Closed in source |
| ArchUnitNET generic placeholder inherited wrong declaring-module metadata | Pinned0.13.3source confirmed; classify `IsGenericParameter` as variable, preserve concrete type and Cecil constraints/arguments checks;183architecture tests green, including foreign type/method constraint and concrete generic argument regressions | Closed |
| Support method-group test fixture added analyzer warnings | Typed Func→Delegate mappings retain actual method metadata;43HTTP/Wolverine tests green, new warnings removed without suppression; original27Flights warnings remain | Closed |

Snapshot review confirms every pre-existing path and component schema remains semantically identical. New Support metadata was generated with actual isolated Wolverine and merged into the source candidate; fullHost equality and actual EF/cert/issuer acceptance are pending CI, not waived. No publication, DB/key/operator grant or Laya runtime authority comes from this review.

## Historical planning review

2026-10-12. Self-review и отдельный read-only reviewer `/root/fare_plan_review`. Проверен весь canonical S1 corpus относительно source на `af725448e6632708d5129971f8570c1d047f4233`, scope roadmap и actual auth/storage/UI seams. Product/source tests/runtimes/API reviewer не запускал; файлы не менял.

## Findings and disposition

| Finding | Изменение плана | Результат |
|---|---|---|
| Important/P2: frontend session seam не устраняет фактическую выдачу booking scope оператору — existing `travel-web` имеет default `flights:book` | D4/proposal/audit/spec выбирают отдельный public PKCE operator client, optional role-gated support scope, backend scope+azp, separate session context; task4.5 проверяет actual disposable issuer tokens и unchanged customer flow. Реальные realm/grants не менялись. | Reviewer re-read: закрыто, соответствует official Keycloak client-scope permission contract. |
| Minor: private Support routes попадали бы в existing Prerender wildcard, текущая auth return whitelist только Flights | D4/tasks5.2–5.3/spec добавляют explicit RenderMode.Client, own safe customer/operator callback/return paths и ClientMode generation; existing Flights behavior/uncertainty сохраняется. | Reviewer re-read: закрыто. |
| Self-review: retained idempotency lifetime / pagination ordering / случайный термин task4.3 | Уточнена retained-case scope; immutable keyset ordering, no cursor owner/no cross-request snapshot promise; исправлен термин. | Локальные strict/link/placeholder checks passed. |

## Independent assessment before the requested self-review

**Ready. Critical/Important/Minor открытых замечаний нет.** Reviewer подтвердил14requirements/33scenarios,0/25implemented tasks, ручной S1 workflow и отдельные Laya S2/S3/schema/keys/grants/runtime/publication gates. Written plan готов для человеческого review; это не source acceptance или разрешение на apply.

## Requested self-review — 2026-10-12

По запросу пользователя «сделай саморевью» выполнен новый полный coherence/feasibility pass. Проверены lifecycle/receipts/retention, authoritative order access, actual JWT claims и browser session boundaries; source evidence дополнительно сверено с `SavedTravelerService`, `saved-travelers-state.service.ts`, `PiiProtectionTests`/`PiiKeyLifecycleTests`, `IdentityServiceCollectionExtensions` и existing server routes. Реализация Support по-прежнему отсутствует: findings ниже — gaps written plan, не обнаруженные product bugs.

| Finding | Исправление и критерий проверки | Disposition |
|---|---|---|
| P2/SR1: lost response не сопоставлен с direct action read; одна page/404может ошибочно считаться NoEffect или не показывать сохранённое сообщение | D4/D5/spec/tasks2.3/3.3/4.3/5.1/5.4–5.5 добавляют common per-case mutation IDs и actor-bound receipt GET, atomic retained receipt, positive proof отдельно от current state; pending/expired/forbidden/unavailable negative read не снимает uncertainty. Проверка: >20messages, early404while write in flight, changed kind/actor, old receipt/new snapshot и zero resubmissions. | Closed in plan; execution evidence остаётся будущими задачами. |
| P2/SR2: privacy generation учитывала GUID/client mode, но не потерю permission/401/403при том же subject | D4/spec/tasks4.2/5.4–5.5 очищают private RAM и отменяют old callbacks по effective capability change/current authorization denial; stale403не очищает новую session. Unknown Support/Flights barriers не сбрасываются. Role membership проверяется issuer; per-request server использует verified JWT snapshot, без обещания instant revocation. | Closed in plan; expected behavior явно проверяется будущими fixtures. |
| P2/SR3: fake-protector tests не доказывали actual at-rest protection/restart/key lifecycle | D7/spec/tasks3.1/3.3 и verification map требуют настоящий Support Data Protection provider/temporary keys и serialized envelope, затем persisted EF ciphertext после появления schema3.2в disposable CI: tamper/wrong bindings/missing key/restart/rotation/no bootstrap. Fake tests отделены от actual provider proof; local/target keys не создаются. | Closed in plan; порядок tasks не требует EF до его создания, реальная проверка выполняется только при будущей source/CI acceptance. |

Current self-review assessment: **Ready for written-plan review, 0 открытых Critical/Important findings.** Корпус содержит14requirements/38scenarios и25planned source tasks. Предыдущий independent Ready относится к14/33edition; этому self-review не приписывается отдельная новая независимая проверка. Product/auth/realm/CI/tooling source, .NET runtime, БД, ключи, модели и внешние системы не менялись/не запускались.
