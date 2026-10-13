# Support fixture workflow

`npm run test:flights-demo` executes Flights and Support Node/browser checks against the loopback-only fixture API and Angular flights-demo build. Support uses only the three exact synthetic RAM bearer values defined in `support-cases.mjs`; public/real credentials, prototype keys and non-loopback peers are refused. Existing Flights credential refusal remains unchanged.

Support covers creation, manual operator triage/reply/resolve and customer reopening; pagination beyond20messages; retained receipts after lost replies, early404while in flight and generic503; permission loss/private memory erase; expiry; explicit fresh unlinked creation; reload without draft reconstruction; keyboard and narrow screens. Browser routing asserts only Support GET/PUT paths and exact fixture tokens, so provider/payment/model calls fail the scenario.

`TRAVEL_SUPPORT_PRESET` selects an isolated startup preset. Per-test servers combine seed history/faults through explicit options; production UI has no preset controls. RAM fixture text/auth does not prove encryption, EF transactions, retention deletion or real issuer issuance. Those have separate disposable ordinary CI source tests and [canonical evidence](../../openspec/changes/archive/2026-10-13-support-s1-case-workflow/verification.md).
