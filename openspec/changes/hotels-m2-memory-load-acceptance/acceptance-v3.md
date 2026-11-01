# Future M2 acceptance revision3 — required versus redundant due work

2026-11-01. The owner delegated autonomous completion after the L source/data audit. The following classification is the implementer's explicit design decision within that scope; it is not a retroactive waiver of v4.

Inheritance: acceptanceVersion1 keeps24 original mandatory checks; version2 keeps23 and the final5s RSS diagnostic as specified in acceptance-v2.md. Version3 retains the same numeric memory, cache, capacity, response, scheduling, phase, safety, duration and evidence limits. Both S/L profiles must use the same supported version.

For version3 only, replace raw dueWorkAndInboxSettled in the mandatory map with m2ActionableDueWorkAndInboxSettled. Required conditions: terminal pendingInbox=0, requiredDue=0, unknownDue=0; all361 queue observations structurally complete and bound to the recorded raw due count. Original raw due count/oldest lag, raw gate boolean and each bounded envelope's identity/type/dueTime/body-size/owned reservation/disposition remain visible; raw=false is never rewritten to true.

Only an exact Wolverine HotelWork message with a complete three-field body, owned reservation, nonempty attempted booking identity, Confirmed/Cancelled outcome and Escalation=false is redundant. All nonterminal work and escalations remain required; webhook observations remain required. Unknown type/body/identity/owner/state and missing/overflowing evidence fail closed. No body or supplier/private content is logged. Maximum64due items and2048byte work body are measurement bounds, not product capacity limits.

The product recovery handler remains unchanged: it persists a bounded successor before provider I/O and safely no-ops for terminal reservations. No scheduled message is cancelled/deleted or processed out of band for acceptance. No additional settling window, threshold epsilon or implicit retry is introduced.

Version3 has23mandatory checks:15 unchanged original families plus8M2 checks (the prior7 plus the actionable queue gate). Both supplemental legacy results remain diagnostic. The unchanged peakRSS1GiB and final<=baseline+128MiB AND mean[330,last]<=mean[300,330) still block on any failure.

Load fixture storage is independently corrected: loadDuffel keeps one immutable serializedUTF8payload shared by two aliases; loadHotelbeds stores SHA256 lookup identities while keeping original opaque wire keys. Logical histories, maximum entries, output data, fault recipes, starts/effects and private guards stay unchanged. Stored key UTF16 payload and unique rate payload bytes are reported separately; logical ValueBytes retains its previous meaning. No fixture history is cleared or reset.

Historical M1failed16/17, S-v2failed22/24, S-v3failed23/24 and v4S23/23/L21/23 remain immutable. Version3 must be assessed only through a fresh frozen S/L campaign with full6304jobs/722samples, complete evidence and owned cleanup. No supplier/target/rollout or leak-free claim follows.
