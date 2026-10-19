# M2 local relational acceptance journal

Owner authorization (2026-10-19): disposable local Docker PostgreSQL, existing migrations, own ephemeral keys/certificate, isolated actual Host/restarts/controlled faults, fictional data and owned terminal supplier wire only. No supplier/payment/model/identity API, real credentials, deployment, load profile, new migration files, stage/commit/push/PR, sync or archive. This explicitly supersedes the earlier source-only local Host/DB restriction for this contour.

Base/HEAD: `75dfba532c0f321286630e3d04fbea70347903a7`; no branch switch or Git mutation. Existing source freeze `TestResults/hotels-m2/root-source-final.json` source-inventory digest (`manifestSha256` field) `e714f916e33b336ad31794ecad87b5c28d92d751527269955f3a60b1f24eab85`, actual manifest-file SHA256 `c07f9cb092d586af33b10be9764c913d14caaa9568bef50d69b9bd10385df2f7` remains historical; this stage adds tests/fixtures and evidence.

## Environment and ownership

- Docker server29.8.2, context `desktop-linux`.
- Baseline container: `692f04d16796`, existing BuildKit; never touched.
- Existing local image `pgvector/pgvector:pg17`, image ID `sha256:7ae6051efd0e60444282c27c7e141af07f322ce033300e727a49c3dd11075e38`.
- Testcontainers owns each PostgreSQL and Ryuk resource. Per-case container IDs, exact owner PID and unique temporary directory are captured in TRX output. Own `travel-hotels-host-<guid>` directories contain ephemeral PFX/keyring; no system certstore.
- SQL probes/triggers are fault injection in those empty disposable DBs, not production migrations/schema redesign. Only existing module/Host migrations are applied by fixtures.
- Actual `Program.cs`, EF store, PostgreSQL CAS/outbox and DataProtection are retained. Both actual supplier adapters use typed terminal owned fictional handlers, never socket-backed supplier transport.

## Running receipts and corrections

- `TestResults/hotels-m2-relational-build-initial.log`: initial fixture extension build succeeded; existing AD0001 analyzer warnings.
- `TestResults/hotels-m2-relational/host-m2-red1.log`: compile RED, nullable test assertion CS8604; assertion fixed with verified nonnull party envelope. No product change.
- `TestResults/hotels-m2-relational/host-m2-red2.log` / `.trx`: first runtime attempt. HBX search was absent because WAF late configuration cannot enable registration-time aggregation. Fixture corrected to set `Hotels:Aggregation:Enabled` before Program registration. This attempt is RED; no acceptance inferred from it.
- At this initial journal checkpoint, functional tests were still being completed/run and tasks6.1–6.3 remained open; the final receipts and closure below supersede that checkpoint.

## Implementer receipts after fixture corrections

| Receipt | Result | Meaning |
|---|---|---|
| `demo-build2.log` | compile RED, EF1002 | Inline test-only SQL interpolation moved to an explicitly constructed owned-fault string, as in the nearest existing fixture. Only generated GUID is interpolated; no request input. |
| `demo-build3.log` | build green | Owned child executable builds,0 errors; existing AD0001/Verify discovery warnings. |
| `host-m2-smoke3.trx` | 1/1 passed,0 skipped | Real SQL admission rollback/no claim/no outbox, then exactly1 Hotelbeds Create and exactly1 durable work. |
| `demo-build4.log` / `host-m2-attempt4.trx` | build green;19/22 passed,3 failed | Two Duffel test helpers improperly tried to quote a Search indicative offer (`LoadRatesRequired`); fixed to Search→LoadRates→Quote. Retention assertion incorrectly used purchase time; existing policy is max(checkout,finishedAt)+90days, now asserted exactly. All3 failures retained; production unchanged. |
| `demo-build5.log` / `host-m2-target5.trx` | build green;6/6 passed,0 skipped | Both genuine SQL contender cases, strengthened atomic outbox fault, real typed retention, v2 signed shared Duffel ingress and request-abort-after-effect. |
| `module-relational-final.trx` | 11/11 passed,0 skipped | Existing10 PostgreSQL durable cases plus1 real HTTP case, including stale cleanup selection versus intervening admission, failed atomic save,50 healthy contenders, encrypted owner/revision binding and retention. |
| `root-host-final.trx` | 44/45 passed,1 failed,0 skipped | Independent main-agent entire Hotels Host namespace. All25 new M2 relational cases passed;19 inherited Host/schema/transport/concurrency cases passed. Only the reviewed obsolete expected OpenAPI snapshot failed; this receipt stays RED. |
| `root-openapi-final.trx` | 2/2 passed,0 skipped | Independent main-agent repeat of the corrected expected snapshot and public-schema privacy case. This supplements, rather than relabels, the preceding44/45 RED run. |
| `root-standalone-final.trx` | 114/114 passed,0 skipped | Independent main-agent standalone Hotels integration regression after reusing the owned wire fixture; DB-heavy durable/HTTP cases are accounted separately above. |

`host-m2-attempt4.trx` also retains actual process-crash evidence: own child PID60676 stopped after owned Hotelbeds booking effect with durable marker `{source:hotelbeds,creates:1}`, and own child PID58412 stopped after committed claim before send with marker `{source:hotelbeds,creates:0}`. Parent replacement Host used the same DB/keyring, retained the spent claim, made0 new Create/read/discovery calls and rejected another action ID. Markers and exact per-case owned PostgreSQL IDs are in TRX stdout. These were OS child process kills; other `RestartAsync` cases are cold WebApplicationFactory/Host restarts.

Seven-file code freeze: `TestResults/hotels-m2-relational/source-freeze.json`, SHA256 `b40a78e56d0dd35729aea3953725155363f35efcb2ed807371d1bf9eddc3de20`. It contains Host fixture/test project, two new test/process files, reused owned memory wire fixture, demo Program/project. `unexpectedPriorSourceChanges=[]`: all earlier source-inventory files outside those explicitly changed test/demo files match. No production, migration, dependency pin/lock or accepted-spec changes in this stage.

SQL contention uses a **test-only** all-contenders capacity and save barrier, records two identical expected versions, one failed save, positive exactly1 selected-source Create,0 other-source Create and0 active permits afterward. Separate refusal/drain tests retain production1-active source budgets. The atomic fault fires only if the durable work probe is already visible inside that transaction; rollback leaves both probe and claim absent, while the subsequent healthy path commits one of each.

The physical privacy cases move only checked absolute paths inside their own unique temporary directory, replace the Host cold with the same unavailable PFX/ring, retain local no-store owner GET/list, and refuse a new claim/effect. Real protector assertions cover owner/revision/purpose integrity and tamper. Retention removes SQL terms, encrypted context/party, actual/mismatch and typed AcceptedPrice/AcceptedConditions while retaining spent/source authority; unresolved Unknown rows remain unexpired even at a future cleanup horizon.

After the implementer's successful receipts, the7 frozen code files were reread and matched (`sourceFreezeMatches=true`, no mismatches). Docker cleanup snapshot showed only untouched baseline BuildKit container `692f04d16796`: no owned PostgreSQL or Ryuk remained. Both child PIDs above were explicitly awaited after kill. No broad/system cleanup was used. At that handoff, the independent full Host namespace run and final task closure still awaited the main agent; the completed receipts are recorded above.

### Exact covering commands

Run from physical repository root in PowerShell. Each invocation uses one MSBuild project/node lane; no restore or paid/external API.

```powershell
dotnet build tools/demo/Travel.Hotels.Fixture/Travel.Hotels.Fixture.csproj --no-restore -m:1 /nodeReuse:false --disable-build-servers
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --no-build --no-restore -m:1 /nodeReuse:false --disable-build-servers --filter 'FullyQualifiedName~HotelsM2RelationalTests.Sql_contenders|FullyQualifiedName~HotelsM2RelationalTests.Real_envelope|FullyQualifiedName~HotelsM2RelationalTests.V2_Duffel|FullyQualifiedName~HotelsM2RelationalTests.Request_abort|FullyQualifiedName~HotelsM2RelationalTests.Real_sql_admission' --logger 'trx;LogFileName=host-m2-target5.trx' --results-directory TestResults/hotels-m2-relational
dotnet test tests/hotels/Travel.Modules.Hotels.Tests.Integration/Travel.Modules.Hotels.Tests.Integration.csproj --no-restore -m:1 /nodeReuse:false --disable-build-servers --filter 'FullyQualifiedName~HotelDurablePurchaseTests|FullyQualifiedName~HotelHttpTests' --logger 'trx;LogFileName=module-relational-final.trx' --results-directory TestResults/hotels-m2-relational
```

The ordinary process-crash tests require the preceding owned demo build; they fail clearly if its executable is absent. The child reads its parent's private manifest from the parent's unique temp directory; connection details are never printed. Parent and attached child both run real Program.cs against that same owned DB/keyring. The child attaches without creating/deleting the parent's DB/keys.

### Independent main-agent Host snapshot finding

The main agent's full namespace run found the historical expected OpenAPI snapshot stale against the already approved M2 public contract. Read-only structural comparison is retained in `TestResults/hotels-m2-relational/openapi-structural-diff.json`:

- Exactly the existing6 Hotels200 responses changed from generic `IResult` to the actual typed Search/Rates/View/array metadata declared by `HotelEndpoints.ProducesResponseType`.
- `HotelRatesDto` adds nullable `propertyGroupKey`, makes `selectionKey` nullable, and requires only `searchId`; D4 retains the runtime exactly-one-selector validation. No assertion or validator was weakened.
- Those typed responses make31 normalized public schemas reachable. They describe terms, price/condition scope, attributed source aliases, source statuses, groups/ranking and owner views. Public `source.account` is the configured `AccountAlias`; private transport account/credential fingerprints remain absent.
- Every path key is unchanged, there are0 non-Hotels path changes, and security schemes are identical. Received text contains0 prohibited names for opaque context, supplier rateKey, admission binding, protected party/context, private credential/account fingerprint or reviewed-map audit material.

After the main agent reviewed and explicitly approved that result, only `Web/Verified/HostWebContractTests.OpenApi.verified.txt` was updated. Original received bytes remain in `TestResults/hotels-m2-relational/openapi-received-before-acceptance.txt`; original received and new expected SHA256 are `8b8df621794b817769df9ceb3b43cf56679a53bc271a306d833eb982e5f2632b`. The old RED is retained. The main agent subsequently repeated only the snapshot case and schema privacy test after this text-only fixture correction:2/2 passed.

The original7-file freeze is unchanged. New8-artifact freeze: `TestResults/hotels-m2-relational/source-freeze-after-snapshot.json`, SHA256 `b382e5c70d4b4e59cacac3bddfa9a4b804315ab2c6521f61a8ba859ff6e14ae7`. All7 code hashes were checked again and match; the eighth item is the reviewed expected snapshot. No production or test-assertion change accompanied the snapshot correction.

### Read-only Unknown runbook

Open the existing owner reservation result and keep the admitted source/attempt/action authority. Owner GET/list reads only retained metadata and does not decrypt guests or call suppliers. Without a saved source reference there is no details/list/discovery request. With a known reference, only the admitted account/environment/profile may consume the existing3-read/5-minute window; duplicate work, restart or late duplicate identity does not restart it. Exhaustion, mismatch, identity changes and transport uncertainty retain review and the spent barrier. Do not reset the attempt, create another action/resource, switch source, resend Booking or dispatch compensation to bypass Unknown.

The controlled clock is one hour ahead of wall time, keeping durable observation work scheduled while explicit runner calls test duplicate-delivery semantics deterministically. This is functional verification, not load/performance proof. Child-process crash tests use a separately built owned demo executable, attach to the same disposable DB/keyring, write a durable source/Create counter marker, and kill only the exact child PID. App restarts elsewhere are labelled cold Host/app restarts, not process crash proof.

## Final local acceptance and remaining external gates

Tasks6.1–6.3 are complete under the explicitly authorized disposable local contour. Pinned apply receipts record23/25 after6.1,24/25 after6.2, and25/25 `all_done` after6.3 (`apply-after-20/21/22.json`). The independent full Host run passed all25 new M2 relational cases and19 inherited cases, with one explicitly retained snapshot RED; the corrected snapshot/privacy repeat passed2/2. Module PostgreSQL durable/HTTP passed11/11; independent standalone regression passed114/114. No skipped or invented waiver closes these tasks.

Evidence covers real SQL/outbox atomicity and competing saves, healthy exactly1 selected-source Create, pre-claim source/account/environment/context refusal, replay/content conflict, request abort and two actual child-process kills without resend, bounded known-reference recovery across cold Host restarts, duplicate/late identity and exhausted-window behavior, reader binding changes, both-supplier writer-off drain, v2 shared signed Duffel ingress/separate inboxes, local reads during physical cold PFX/ring outage, pre-M2 serialized history, real envelope integrity/owner checks, and SQL retention of authority while removing private fields. The inherited stale-cleanup/admission race and Host transport/concurrency cases passed. Browser logout/epochs and production architecture remain covered by the unchanged earlier source receipts; no frontend/production files changed in this phase.

The main agent owns final harness/format checks and its own resource cleanup/provenance appendix. Source M1 memory acceptance remains failed16/17 (`workingSetRecovery=false`), with no waiver transferred to M2. CI, M2 load/soak/performance, real supplier API/mTLS, target keys/schema/grants and deployment/rollout remain separate gates. No sync/archive/publication or Git mutation was performed.

## Main-agent final verification

The main agent completed the remaining checks personally: CSharpier7/7 files, harness134/134, pinned apply25/25 `all_done` and strict validation valid=true/issues=[]. The first harness launch was blocked by sandbox Windows spawn EPERM (`root-harness-final.log`); the scoped local retry passed (`root-harness-elevated.log`). No authenticated verifier or external API was used.

Final source inspection matched all8 phase artifacts and all63 earlier source files outside the three explicitly changed test/demo consumers. The M1 corpus and three synced main contracts remain unchanged, and no migration/dependency/lock changes were introduced. HEAD/origin/dev remain75dfba532c0f321286630e3d04fbea70347903a7, with the owner's M1 checkpoint still an ancestor.

`TestResults/hotels-m2-relational/root-cleanup-final.json` records71 own container/directory receipts and4 own crash-child PIDs: none remain live/present. A separate Temp directory inspection found0 `travel-hotels-host-*` directories. Docker contains only the untouched baseline BuildKit692f04d16796. No manual broad cleanup was performed. `git diff --check` passed; Verify left no received snapshots. The [main verification](verification.md) contains the final exact commands, counts and retained RED-to-focused-GREEN explanation.
