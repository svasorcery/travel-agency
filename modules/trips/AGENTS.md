# Trips module

Trips M1 implements a private structured brief and editable calendar workspace. Its canonical scope, acceptance scenarios, task progress and local receipts are in `openspec/changes/trips-m1-structured-planning`. M1 is the foundation; automatic proposed itineraries (M2) and free-text brief extraction (M3) remain separate changes.

Core owns exact money, day/block invariants and non-destructive brief changes; Application owns owner guards, revision decisions and use cases. Infrastructure owns the Trips EF schema, atomic owner-capacity/CAS storage, protected canonical snapshots and read-only readiness. Api owns strict HTTP contracts and Composition. Host retains global builders and one endpoint map; Api code outside Composition must not import Infrastructure.

Trips is composed through its real Api facade and stays default-off. Disabled routes return404 before auth and resolve no Trips DB/protection services. Enabled private routes require a trusted owner principal; absent schema/protection fails closed. Ordinary startup never migrates schema or creates an empty key ring. Source migration and owned disposable PostgreSQL tests were explicitly authorized; existing/target database, keys, publication and rollout remain separate.

M1 makes no model, supplier, quote, booking, payment or cancellation call. User blocks and their estimates are intentions, not verified supplier facts. Unknown estimates stay separate from the known subtotal. Clients do not retry uncertain writes automatically and never retain private plans in browser storage or SSR output.

Local module behavior and operation configuration: [README](README.md). Test-only fictional HTTP/browser fixtures and disposable PostgreSQL evidence do not establish target readiness or itinerary/model quality. Preserve the exact requested fresh-dev ancestry when integrating alongside Rail.
