# Spec Delta

## ADDED Requirements

### Requirement: Experimental operator labelling and fixed categories
The operator classification control and any displayed result SHALL visibly identify the feature as experimental and warn that it can be wrong and requires review before application. Laya SHALL retain exactly eight candidate categories: Payment, Ticketing, Cancellation, Refund, FareConditions, Ancillaries, Technical and Other. Unclassified SHALL remain a manual fallback rather than a model candidate.

#### Scenario: Before request and during result review
- **WHEN** an operator opens an eligible case or views a returned suggestion
- **THEN** «Экспериментальная» and the warning to check before applying remain visible near the classification control, including after a manual-required or failed outcome.

#### Scenario: Customer view
- **WHEN** a customer views an appeal
- **THEN** neither the classification control nor its experimental label is shown.

#### Scenario: Separate manual confirmation
- **WHEN** an operator requests or prefills an experimental candidate
- **THEN** the saved category, priority, revision and triage source remain unchanged until the operator separately confirms the existing manual save action.

#### Scenario: Current model and taxonomy
- **WHEN** the experimental suggestion is requested
- **THEN** the existing Laya profile and eight category choices remain in use; no model replacement, new category or automatic assignment is introduced.

## MODIFIED Requirements

### Requirement: Confidence and abstention are explicit
Distinct confidence measurements SHALL retain their meanings. An experimental operator candidate SHALL satisfy the configured nonzero provisional guards. Missing confidence or an abstained answer SHALL require manual classification. A failed or unevaluated quality gate SHALL remain disclosed as such and SHALL NOT be represented as quality acceptance. Confidence SHALL NOT be presented as measured accuracy.

#### Scenario: Unevaluated or abstained answer
- **WHEN** usable confidence is absent, the configured guards reject the answer, or the quality gate is absent/unevaluated without explicitly approved experimental use
- **THEN** no category recommendation is offered and manual classification remains available.

#### Scenario: Explicitly accepted experimental use
- **WHEN** limited experimental operator use is explicitly approved despite a failed or unevaluated quality gate and a valid answer clears the unchanged provisional guards
- **THEN** the candidate may be shown only with the experimental warning and separate manual acceptance, without claiming the quality gate passed.

#### Scenario: High numerical confidence
- **WHEN** an answer clears both separately defined guards
- **THEN** it is still labelled an operator-reviewed experimental suggestion, without a correctness guarantee or automatic assignment.

### Requirement: Bounded computation without automatic replay
Inference SHALL have finite admission and input/output budgets and a total request deadline of at most 10 seconds, with no queued retry or automatic replay. Timeout and lost transport SHALL NOT prove that remote computation never ran. Manual S1 actions SHALL remain usable when inference admission or transport fails.

#### Scenario: Busy or unavailable classifier
- **WHEN** admission is exhausted, Laya is busy, an HTTP error occurs or the time budget expires
- **THEN** a fixed manual-handling reason is shown with no automatic retry or queued work.

#### Scenario: Lost inference response
- **WHEN** the remote result may have completed but its response is lost
- **THEN** the UI states uncertainty, does not resend, and any later explicit new attempt consumes admission again.

#### Scenario: Experimental request reaches the deadline
- **WHEN** the 10-second inference request budget expires
- **THEN** no automatic retry occurs, the outcome remains uncertain if computation may have run, and manual handling remains available.

### Requirement: Separate source, runtime and quality acceptance
Source acceptance SHALL require strict specification validation, privacy/HTTP/contract/browser tests and ordinary exact-head CI including PR E2E. Real-model readiness and RU/EN quality versus a simple baseline SHALL require separately authorized, versioned evidence. Explicit approval of limited experimental operator use SHALL NOT turn a failed quality result into acceptance. Automatic routing SHALL remain outside S2.

#### Scenario: Fixture and ordinary CI pass
- **WHEN** tests using model-response fixtures and disposable ordinary CI pass
- **THEN** source integration behavior is accepted without claiming installed weights, target provisioning, model quality or operator-time savings.

#### Scenario: Real-model experiment or later promotion
- **WHEN** model installation, private-text use, evaluation or automatic routing is proposed
- **THEN** the separate runtime/experiment or S3 gate must be approved and measured before enablement.

#### Scenario: Local experimental application check
- **WHEN** the explicitly approved experimental UI is checked with fictional cases and fixture model replies
- **THEN** the evidence confirms labelling, eight-category selection and separate manual persistence only, without claiming a live-model run, real-case enablement or improved model quality.
