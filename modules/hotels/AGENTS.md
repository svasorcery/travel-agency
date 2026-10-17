# Hotels module

Hotels M1 has source implementation in Core/Application/Infrastructure/Api, an EF reservation/inbox model, a default-off Test-only Duffel Stays adapter, Angular search/quote/private result flow, and bounded observation/retention handlers. Its canonical contract, completed source/fictional acceptance and owner-accepted memory-check limitation (16/17; workingSetRecovery remains failed) are in `openspec/changes/hotels-m1-duffel-booking`; source/fake-fixture checks do not establish supplier sandbox, target provisioning, CI or rollout acceptance.

M1 supports one room and1–2 adults. Provider protocol/DTOs/context mapping stay in Infrastructure; Core/Application use normalized search/preparation/purchase/optional-reader capabilities. The small alternate semantic adapter is a contract-test substitute, not a second real supplier. Multi-supplier aggregation/ranking, children/multiple rooms, customer payments and post-booking actions remain future scope.

Host imports Hotels only through `Api.Composition`. Hotels owns its EF schema, protection purposes and inbox; Host owns the single process builders and bounded `/webhooks/duffel` transport. Flights/Hotels receivers claim unique authenticated ownership before writes; historical Flights parsing applies only to saved evidence.

Keep creation capacity before the durable claim and never reset/retry a spent sender. Owner GET/list are local and need no guest decryption. Disabling `BookingEnabled` preserves receiving/drain. Normal startup never creates private keys; Production excludes the Test-only adapter and default-off Hotels startup does not require its schema or keys. Follow root authorization boundaries for source migrations and disposable fixtures.
