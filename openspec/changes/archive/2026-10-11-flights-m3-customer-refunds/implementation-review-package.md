# Whole-change review package

Base and detached HEAD: 7aaf3d60e852670dd4a1f2f257d3e26995e5306f. Implementation is uncommitted; range diff alone is empty. Review tracked diff plus ALL untracked source files below. No staging/commit/push/merge or worktree mutation.

Plan: tasks.md. Spec: specs/flights-customer-refund-after-cancellation/spec.md. Design: design.md. Evidence/limits: verification.md, process-log.md, demo.md.

Review focus (verbatim):
Review focus: (1) Refunded label без capture proof, (2) signal между claim и отправкой,
(3) success после deadline/failed commit, (4) response прежнего owner/version,
(5) эквивалентный replay manual resolution со старым revision.

Local Host/AppHost/DB/schema/key bootstrap/paid/live APIs are prohibited. Review read-only; do not run ordinary test assemblies (PII AssemblyFixture before filters). Only audited temp offline subset is allowed. Full existing CI, generated Host OpenAPI baseline and architecture/startup remain pending by authority. Do not confuse planning review with code review or base CI with this source.

Final implementation policies: exact supplier cash amount/same captured currency/no Travel fee; agency-funded dispatch after proven cancellation; one saved claim; fixed180s deadline and120s cap; Sent means dispatch not bank credit. Operator cancellation/manual old unsupported basis never arbitrary-seeds money. New airline report has independent identity/revision/target and no amount. Historical event/read/hash bytes retained.

Node result-lost fixture presents final manual result directly; actual fixed timer proof is separate .NET/CI. Browser purchase seeded loopback for9/4 case, prior ancillary suite proves form journey.

Report Critical/Important/Minor with concrete repro/file lines, and every behavior declined to judge with reason. Assess source/local readiness separately from CI/real-money readiness. Single fresh reviewer, no subagents.

Files (112):
- apps/web/src/app/flights/flight-customer-refund.component.html
- apps/web/src/app/flights/flight-customer-refund.component.scss
- apps/web/src/app/flights/flight-customer-refund.component.spec.ts
- apps/web/src/app/flights/flight-customer-refund.component.ts
- apps/web/src/app/flights/flight-customer-refund.service.spec.ts
- apps/web/src/app/flights/flight-customer-refund.service.ts
- modules/flights/Travel.Modules.Flights.Api/Composition/CustomerRefundDeliveryPolicy.cs
- modules/flights/Travel.Modules.Flights.Api/Contracts/CustomerRefundRequestValidation.cs
- modules/flights/Travel.Modules.Flights.Api/Contracts/CustomerRefundReviewRequest.cs
- modules/flights/Travel.Modules.Flights.Api/Endpoints/CustomerRefundEndpointResults.cs
- modules/flights/Travel.Modules.Flights.Api/Endpoints/GetCustomerRefundReviewEndpoint.cs
- modules/flights/Travel.Modules.Flights.Api/Endpoints/GetCustomerRefundStatusEndpoint.cs
- modules/flights/Travel.Modules.Flights.Api/Endpoints/ResolveCustomerRefundReviewEndpoint.cs
- modules/flights/Travel.Modules.Flights.Api/Middleware/CustomerRefundRequestBodyGuard.cs
- modules/flights/Travel.Modules.Flights.Application/Handlers/Refunds/AssessCustomerRefundHandler.cs
- modules/flights/Travel.Modules.Flights.Application/Handlers/Refunds/CustomerRefundDeadlineHandler.cs
- modules/flights/Travel.Modules.Flights.Application/Handlers/Refunds/ExecuteCustomerRefundHandler.cs
- modules/flights/Travel.Modules.Flights.Application/Handlers/Refunds/GetCustomerRefundReviewHandler.cs
- modules/flights/Travel.Modules.Flights.Application/Handlers/Refunds/GetCustomerRefundStatusHandler.cs
- modules/flights/Travel.Modules.Flights.Application/Handlers/Refunds/ResolveCustomerRefundReviewHandler.cs
- modules/flights/Travel.Modules.Flights.Application/Refunds/CustomerRefundMessages.cs
- modules/flights/Travel.Modules.Flights.Application/Refunds/CustomerRefundStatusFactory.cs
- modules/flights/Travel.Modules.Flights.Application/Refunds/CustomerRefundStatusResult.cs
- modules/flights/Travel.Modules.Flights.Application/Refunds/CustomerRefundWriter.cs
- modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.CustomerRefund.cs
- modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.CustomerRefundReview.cs
- modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.SupplierReport.cs
- modules/flights/Travel.Modules.Flights.Core/Cancellation/SupplierCancellationReport.cs
- modules/flights/Travel.Modules.Flights.Core/DomainEvents/CustomerRefundEvents.cs
- modules/flights/Travel.Modules.Flights.Core/DomainEvents/SupplierCancellationReported.cs
- modules/flights/Travel.Modules.Flights.Core/Providers/ICustomerRefundGateway.cs
- modules/flights/Travel.Modules.Flights.Core/Refunds/CustomerRefundCase.cs
- modules/flights/Travel.Modules.Flights.Core/Refunds/CustomerRefundContracts.cs
- modules/flights/Travel.Modules.Flights.Core/Refunds/CustomerRefundPolicy.cs
- modules/flights/Travel.Modules.Flights.Core/Refunds/CustomerRefundReviewInput.cs
- modules/flights/Travel.Modules.Flights.Infrastructure/Payments/FictionalCustomerRefundGateway.cs
- openspec/changes/flights-m3-customer-refunds/.openspec.yaml
- openspec/changes/flights-m3-customer-refunds/checkpoint.md
- openspec/changes/flights-m3-customer-refunds/demo.md
- openspec/changes/flights-m3-customer-refunds/design.md
- openspec/changes/flights-m3-customer-refunds/offline-probe-path.txt
- openspec/changes/flights-m3-customer-refunds/process-log.md
- openspec/changes/flights-m3-customer-refunds/proposal.md
- openspec/changes/flights-m3-customer-refunds/research.md
- openspec/changes/flights-m3-customer-refunds/review.md
- openspec/changes/flights-m3-customer-refunds/specs/flights-customer-refund-after-cancellation/spec.md
- openspec/changes/flights-m3-customer-refunds/tasks.md
- openspec/changes/flights-m3-customer-refunds/verification.md
- shared/ts/api-client/src/flights-customer-refund-api.service.ts
- shared/ts/api-client/src/flights-customer-refund.decoder.spec.ts
- shared/ts/api-client/src/flights-customer-refund.decoder.ts
- shared/ts/api-client/src/flights-customer-refund.types.ts
- tests/Travel.Host.Tests.Integration/Flights/CustomerRefundHttpTests.cs
- tests/fixtures/flights-customer-refunds.json
- tests/flights/Travel.Modules.Flights.Tests.Integration/Refunds/CustomerRefundPersistenceTests.cs
- tests/flights/Travel.Modules.Flights.Tests.Integration/Refunds/CustomerRefundRestartTests.cs
- tests/flights/Travel.Modules.Flights.Tests.Unit/Refunds/CustomerRefundDecisionTests.cs
- tests/flights/Travel.Modules.Flights.Tests.Unit/Refunds/CustomerRefundDeliveryPolicyTests.cs
- tests/flights/Travel.Modules.Flights.Tests.Unit/Refunds/CustomerRefundExecutionTests.cs
- tests/flights/Travel.Modules.Flights.Tests.Unit/Refunds/CustomerRefundOrchestrationTests.cs
- tests/flights/Travel.Modules.Flights.Tests.Unit/Refunds/CustomerRefundPolicyTests.cs
- tests/flights/Travel.Modules.Flights.Tests.Unit/Refunds/CustomerRefundRegistrationTests.cs
- tests/flights/Travel.Modules.Flights.Tests.Unit/Refunds/CustomerRefundReplayTests.cs
- tests/flights/Travel.Modules.Flights.Tests.Unit/Refunds/CustomerRefundRequestValidationTests.cs
- tests/flights/Travel.Modules.Flights.Tests.Unit/Refunds/CustomerRefundReviewTests.cs
- tests/flights/Travel.Modules.Flights.Tests.Unit/Refunds/CustomerRefundStatusTests.cs
- tests/flights/Travel.Modules.Flights.Tests.Unit/Refunds/FictionalCustomerRefundGatewayTests.cs
- tests/flights/Travel.Modules.Flights.Tests.Unit/Refunds/RefundTestBookings.cs
- tests/flights/Travel.Modules.Flights.Tests.Unit/Refunds/SupplierCancellationReportTests.cs
- tests/travel-e2e/demo/flights-customer-refunds.spec.ts
- tools/demo/flights-customer-refunds.mjs
- tools/demo/flights-customer-refunds.test.mjs
- apps/web/src/app/flights/flight-cancellation.service.ts
- apps/web/src/app/flights/flight-order-page.component.html
- apps/web/src/app/flights/flight-order-page.component.spec.ts
- apps/web/src/app/flights/flight-order-page.component.ts
- apps/web/src/app/flights/flight-orders-page.component.ts
- docs/adr/0015-booking-aggregate-event-model.md
- docs/adr/0016-booking-saga-via-marten-es.md
- docs/adr/0018-duffel-webhook-inbox-outbox.md
- docs/adr/0019-payment-gateway-abstraction.md
- docs/architecture/current-state.md
- docs/examples/flights-requests.json
- modules/flights/Travel.Modules.Flights.Api/Composition/FlightsModule.cs
- modules/flights/Travel.Modules.Flights.Api/Contracts/CancellationRequestValidation.cs
- modules/flights/Travel.Modules.Flights.Api/Contracts/CancellationRequests.cs
- modules/flights/Travel.Modules.Flights.Api/Contracts/CancellationResponse.cs
- modules/flights/Travel.Modules.Flights.Api/Middleware/SavedTravelerResponsePolicyMiddleware.cs
- modules/flights/Travel.Modules.Flights.Application/Cancellation/CancellationDecisionWriter.cs
- modules/flights/Travel.Modules.Flights.Application/Cancellation/CancellationReviewFactory.cs
- modules/flights/Travel.Modules.Flights.Application/Cancellation/CancellationReviewResult.cs
- modules/flights/Travel.Modules.Flights.Application/Handlers/Webhooks/DuffelWebhookHandler.cs
- modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.Cancellation.cs
- modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.Confirmation.cs
- modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.ManualResolution.cs
- modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.cs
- modules/flights/Travel.Modules.Flights.Core/Cancellation/ManualResolutionInput.cs
- modules/flights/Travel.Modules.Flights.Infrastructure/Diagnostics/BookingConsistencyDiagnostics.cs
- modules/flights/Travel.Modules.Flights.Infrastructure/FlightsInfrastructureServiceCollectionExtensions.cs
- modules/flights/Travel.Modules.Flights.Infrastructure/Marten/BookingAggregateConfig.cs
- modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/OrderReadModelEventApplier.cs
- shared/ts/api-client/src/index.ts
- tests/Travel.Host.Tests.Integration/Documentation/SavedTravelersWolverineOpenApiFixture.cs
- tests/Travel.Host.Tests.Integration/Flights/CancellationApiFixture.cs
- tests/Travel.Host.Tests.Integration/Flights/CancellationEndpointMetadataTests.cs
- tests/Travel.Host.Tests.Integration/Web/HostWebContractTests.cs
- tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/BookingConsistencyRecoveryTests.cs
- tests/flights/Travel.Modules.Flights.Tests.Integration/Webhooks/DuffelWebhookHandlerTests.cs
- tests/travel-e2e/demo/fictional-route-guard.ts
- tests/travel-e2e/demo/flights-fictional-guard.spec.ts
- tests/travel-e2e/demo/flights-search.spec.ts
- tools/demo/flights-search-api.mjs
