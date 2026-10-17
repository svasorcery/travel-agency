# Hotels M1 vocabulary and contract

`StayDates` counts accommodation-local calendar nights; checkout is exclusive. `RoomOccupancy` is one room with one or two adults. `StayMoney` is an exact whole-stay amount, never a per-night multiplier. Mandatory local payments are separate and nullable: missing information is not zero. Property identity, room, rate, board and scoped cancellation/check-in conditions have separate meanings.

Search sources expose `GetSupport`, search and rates. Purchase capability is a separate interface; a search-only implementation has no buying methods. Optional known-ID observation is another interface. Application code chooses an available capability by its retained provider key, with no Duffel-name branch. Provider protocol, HTTP, quote ID, balance funding and raw JSON stay in Infrastructure.

An ordinary call checks support offline, executes only when supported, and saves the **entire** normalized snapshot: offers, provider statuses, unsupported/failure/limited reason and expiry. A two-adult request is a meaningful edge: the small alternate test adapter accepts only one adult. Its direct execution and offline check agree on unsupported rather than returning complete-empty. Complete-empty remains a successful search with zero offers. Cache caps count whole search/rates snapshots plus serialized UTF-8 bytes and key.

Both controlled adapters use the same owned local quote/revision, consent, protected party, capacity-before-claim, single create and private-result application flow. One prepares without any supplier quote ID and confirms synchronously without a reader or webhook. This is a bounded semantic contract check; it does not prove readiness for another real supplier.

Source work and verification are recorded in `openspec/changes/hotels-m1-duffel-booking/verification.md`. Runtime, key provisioning, supplier account access and rollout require their separately authorized boundaries.

## Supported source subset

The Duffel adapter accepts future check-in1–330UTC calendar days,1–99calendar nights, one room/1–2adults and curated London/Paris/Rome areas (radius1–20km). A fictional destination is reserved for controlled semantic adapters. Search is indicative; rates distinguish room/board/conditions; preparation creates a local revision with a bounded private versioned context. M1 purchase requires explicit eligible immediate Test terms, known exact debit/local obligations and known cancellation policy. Board type alone does not establish guest coverage; source benefit descriptions retain their own limits. Source booking totals come from the actual nested accommodation/room/rate facts. Nullable hotel reference is optional, and malformed/status-unknown data cannot establish a booking.

Phone input uses structural E.164 (`+`, a nonzero first digit, at most15digits). This does not validate a country's dial plan or whether a number exists. Guests require only names, lead email/phone and explicit adult/check-in acknowledgements. No passport/DOB/cards/free-form requests or saved guests.

## Privacy and source operations

`Hotels:Enabled`, `BookingEnabled` and `ReceiverEnabled` defaultfalse. Production rejects the Test-only feature configuration. Keys are never created by ordinary startup: a separately authorized key-only initialization must establish the isolated `Travel.Hotels.Pii.v1` certificate-protected ring before enabling private work. Required settings are `Hotels:PiiProtection:KeyRingPath` and `ActiveCertificatePath`; read certificates support explicit key rotation. Do not copy Flights key/provider internals. Loss/tamper/binding failure fails closed; owner GET/list read metadata without guest/context decryption.

Fictional retention defaults: draft24h; finished private details90days after the later of checkout and terminal result; unresolved purchases have no automatic private expiry. Deadline views hide details even before bounded physical cleanup. Cleanup removes terms/context/party/actual/private reference but retains owner/resource/attempt/action, spent claim and safe source correlation. A consumed resource never becomes a fresh purchase after cleanup.

For diagnostic review use only owned reservation/attempt IDs, provider/environment/account, safe preparation/booking/request references, timestamps and normalized outcome/review reasons. Metadata reads never call the supplier. `Unknown`/pending/details missing/deadline are not no-creation evidence; never repeat create, reset a claim, edit a database outcome, cancel/refund automatically or invent a hotel reference. Different accepted/actual conditions remain confirmed facts with review; verified cancellation remains inactive without promising money movement.

The public `/webhooks/duffel` transport keeps exact RAM-only bytes, one signature header value and unique authenticated module ownership. Both receivers validate current v2/mode/account before dedup. Flights historical root-object reading is saved-content compatibility only. A healthy owning module can accept while unrelated private keys are unavailable; owning store/protection failure refuses acknowledgement for redelivery. Unknown owned source references remain bounded unmatched evidence. Keep the configured signing-key/account association; never choose an account from client input.

For a rollback of new buying set `BookingEnabled=false` while preserving `Enabled`, required receivers, schema/evidence, keys and drain. Do not remove the Hotels receiver while subscribed or retained purchases need it. No manual resolver is supplied in M1. Target schema/keys, `hotels:book` grants, Duffel Stays access/subscription and real-content rights remain separate provisioning/acceptance gates; these notes perform none of them.

## Isolated fictional demo

From the repository root, with existing pinned dependencies and the separately authorized disposable Docker contour:

```text
dotnet run --project tools/demo/Travel.Hotels.Fixture
npx.cmd nx serve web --configuration=hotels-demo
```

Open `http://127.0.0.1:4202/hotels`. The build-only auth replacement selects a fictional owner; the app calls real `Program.cs` onlocalhost5111, owned disposable PostgreSQL/outbox and a fake supplier HTTP handler. No provider endpoint override or alternate business API is introduced. Use London, future dates and1/2adults; review whole-stay120.25GBP and separate0GBP local amount, enter clearly fictional guests and complete the explicit acknowledgements. The shared result route supports local reload with zero repeat purchase. The normal production/development builds never select this auth replacement.

The wrapper uses Testing before configuration load and verifies memory-only settings; saved user secrets are not part of this contour. It reports its exact owned container/temp-root/PID without connection passwords. Use an interactive terminal (or a PTY when running through an agent), then send `stop` on stdin or Ctrl+C for awaited cleanup; it also has a30-minute bound. Never delete other Docker resources or glob temporary directories. Browser evidence uses an installed Edge in a fresh headless test context; `node tools/demo/hotels-browser.mjs` covers normal/reload/client-reply-loss/keyboard/mobile flows. Source provider/state/real-Host tests exercise changed terms, reserved/unknown, late first identity and external cancellation; they are fictional evidence, not supplier sandbox acceptance. Verification retains actual commands and any runner failures.

Optional explicit demo modes select a single fictional supplier outcome for the next purchase:

```text
dotnet run --project tools/demo/Travel.Hotels.Fixture -- reserved
dotnet run --project tools/demo/Travel.Hotels.Fixture -- unknown
dotnet run --project tools/demo/Travel.Hotels.Fixture -- cancelled
```

`reserved` returns documented200without details and `unknown` loses the supplier reply after its fictional effect. After5s the wrapper sends an authentic first-ID callback through the actual shared route; the production bounded reader obtains complete details. `cancelled` confirms first and sends a signed external cancellation with the exact bound bookingID. Each mode starts its own disposable fixture; stop it before selecting another. Refreshing the same rate produces a same-price material condition change, clears entered guests/consent and requires fresh review. Full direct confirmation does not prevent a separate second booking resource.

The360s L1 experiment is an explicit `l1 <frozen-manifest-path>` CLI mode, never an ordinary test. Its predeclared profile, final manifest and runner require main review before execution. The dependency-free `tools/demo/hotels-source-manifest.mjs` inventories all current non-document source/fixtures/harness plus the load profile; the runner rejects an incomplete/changed inventory and verifies hashes before/after. This does not authorize running the profile without its existing review gate.
