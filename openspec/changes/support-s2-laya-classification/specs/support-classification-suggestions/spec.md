# Spec Delta

## Purpose

Дать оператору приватную проверяемую подсказку категории и очереди обращения по явному запросу, сохраняя ручное решение, достоверные правила срочности и независимость поддержки от финансовых действий.

## ADDED Requirements

### Requirement: Explicit operator request only
Classification SHALL run only after an explicit authorized operator request for a selected current case. Creation, reads, queue refresh, replies, navigation and background work SHALL NOT trigger inference. Disabled classification SHALL make no model request.

#### Scenario: Ordinary case operations
- **WHEN** customers or operators create, read, reply to or refresh a case
- **THEN** no classifier call occurs and the manual S1 workflow remains available.

#### Scenario: Operator requests a suggestion
- **WHEN** an authorized operator explicitly requests a suggestion for the displayed revision
- **THEN** at most one admitted inference is attempted without changing persisted case data.

#### Scenario: Disabled feature
- **WHEN** classification is disabled or its required serving profile is absent
- **THEN** a fixed Disabled outcome is shown without decrypting input or invoking Laya.

### Requirement: Authority and retention before private processing
Suggestion access SHALL require both dedicated operator permission and operator client identity. Access, current revision, eligible non-resolved state and expiry SHALL be checked before decrypting text and again before disclosure. Request identifiers SHALL NOT establish caller authority.

#### Scenario: Customer or wrong client requests classification
- **WHEN** a customer, booking-only principal or wrong-client principal requests a suggestion
- **THEN** it is denied before decryption, order lookup or inference.

#### Scenario: Expired or concurrently resolved case
- **WHEN** the case is expired, absent or becomes resolved during processing
- **THEN** no private suggestion is disclosed and no triage mutation occurs.

### Requirement: Complete bounded private input
The model input SHALL contain only a bounded complete title/conversation from the authorized retained case. Unsupported size, missing messages or unreadable text SHALL yield manual handling without silently dropping content. Text SHALL remain untrusted data inside an explicitly approved self-hosted Support perimeter.

#### Scenario: Complete short conversation
- **WHEN** the title and all messages fit the declared input policy
- **THEN** only plain text and customer/operator role markers are sent, without identity, order, passenger, token or credential fields.

#### Scenario: Oversize or unreadable conversation
- **WHEN** there are more than4messages, more than8000input Unicode scalars, more than24KiB serialized state, malformed input or failed decryption
- **THEN** no partial input is sent and the outcome explicitly requires manual handling.

#### Scenario: Personal details or embedded instructions
- **WHEN** free text contains personal details, HTML or instructions to change routing or execute actions
- **THEN** it remains private untrusted text, cannot alter the fixed question/options/configuration, and is never logged, cached or sent beyond the approved perimeter.

### Requirement: Verified serving identity
An eligible model result SHALL be tied to an approved immutable server/checkpoint/question profile. Generic liveness and a constant response model name SHALL NOT prove that identity. Missing or mismatched authenticated identity evidence SHALL yield manual handling.

#### Scenario: Healthy but wrong checkpoint
- **WHEN** health returns200with no required resident revision or a different checkpoint/profile is observed
- **THEN** no category is offered as a usable recommendation.

#### Scenario: Versioned candidate
- **WHEN** serving identity and the response are accepted
- **THEN** the suggestion carries the serving profile and question version, with checkpoint provenance distinguished from model quality.

### Requirement: Strict bounded typed output
A category candidate SHALL use exactly the configured category labels and matching queue mapping. Missing, malformed, oversized, truncated or collapsed-option responses SHALL yield explicit manual handling. Provider diagnostics or arbitrary strings SHALL NOT become product text.

#### Scenario: Allowed category
- **WHEN** a complete valid answer selects an allowed category
- **THEN** its queue is derived by the application from that category and no arbitrary destination is accepted.

#### Scenario: Unknown label or incomplete model evidence
- **WHEN** a response contains an unknown label, inconsistent probabilities, wrong discriminator, missing required fields or lost input/options
- **THEN** the result is rejected without replacing the saved category or displaying raw provider errors.

### Requirement: Confidence and abstention are explicit
Distinct confidence measurements SHALL retain their meanings. A usable category SHALL satisfy the configured provisional guards and an evaluated matching abstention gate; missing confidence, abstained or unevaluated results SHALL require manual classification. Confidence SHALL NOT be presented as measured accuracy.

#### Scenario: Unevaluated or abstained answer
- **WHEN** usable confidence is absent, the gate is absent, unevaluated or abstained, or its threshold differs
- **THEN** no usable category recommendation is offered.

#### Scenario: High numerical confidence
- **WHEN** an answer clears both separately defined guards
- **THEN** it is still labelled an operator-reviewed suggestion, without a correctness guarantee or automatic assignment.

### Requirement: Case revision and suggestion lifetime
Each suggestion SHALL identify the case, captured revision, request and generation/expiry times. A changed case revision, newer request or elapsed lifetime SHALL invalidate that suggestion. A stale result SHALL NOT overwrite a newer operator decision.

#### Scenario: Reply or manual triage races with inference
- **WHEN** the case revision changes before the inference result is disclosed or accepted in the UI
- **THEN** the old suggestion is discarded and the operator is asked to review the current case.

#### Scenario: Expired or older request result
- **WHEN** the suggestion is older than2minutes or a newer request supersedes it
- **THEN** its apply control is unavailable and a late completion cannot restore it.

### Requirement: Bounded computation without automatic replay
Inference SHALL have finite admission, input/output and request-time budgets with no queued retry or automatic replay. Timeout and lost transport SHALL NOT prove that remote computation never ran. Manual S1 actions SHALL remain usable when inference admission or transport fails.

#### Scenario: Busy or unavailable classifier
- **WHEN** admission is exhausted, Laya is busy, an HTTP error occurs or the time budget expires
- **THEN** a fixed manual-handling reason is shown with no automatic retry or queued work.

#### Scenario: Lost inference response
- **WHEN** the remote result may have completed but its response is lost
- **THEN** the UI states uncertainty, does not resend, and any later explicit new attempt consumes admission again.

### Requirement: Application rules for urgency
Urgency SHALL be an application recommendation from fresh owner-bound authoritative order facts and server time. An active journey with a future scheduled departure at most24hours away SHALL recommend High; unknown context SHALL NOT establish Normal. Model categories and customer claims SHALL NOT establish order or financial facts.

#### Scenario: Verified imminent active journey
- **WHEN** authoritative owned order facts show an active journey and the time to its next planned segment departure satisfies 0 < time <=24hours
- **THEN** High is proposed independently of category confidence, without saving priority.

#### Scenario: Claimed or unknown urgency
- **WHEN** text claims imminent departure, no order is linked, times are absent or authoritative context is unavailable
- **THEN** urgency is explicitly unknown and the current manual priority is preserved.

#### Scenario: Closed journey, past departure or existing High
- **WHEN** an order is closed, has no known future departure, departs beyond24hours or the case already has High
- **THEN** the rule does not automatically lower priority or infer payment/refund completion.


#### Scenario: Unresolved order lifecycle
- **WHEN** a cancellation/supplier report, creation or confirmation outcome is unresolved, or a Held order has expired or unknown hold validity
- **THEN** the linked journey context is Unknown, no confirmed Active claim is made and urgency requires manual review.

### Requirement: Explicit manual acceptance and correction
A suggestion SHALL leave persisted category, priority, revision, lifecycle and audit unchanged until a separate operator decision. Accepting or correcting it SHALL use the existing current-revision manual action and retain operator before/after provenance. A queue recommendation SHALL NOT assign an employee.

#### Scenario: Operator accepts or edits
- **WHEN** the operator reviews a valid candidate and explicitly saves chosen category/priority
- **THEN** the normal conditional manual action records the operator decision and metadata receipt.

#### Scenario: Rejected suggestion or uncertain manual save
- **WHEN** the operator ignores the suggestion or the save response is uncertain
- **THEN** ignoring performs no mutation and uncertain saves preserve S1 receipt/revision barriers without resubmission.

### Requirement: Private browser and diagnostic lifetime
Suggestions and input SHALL remain private and no-store, without URLs, durable queues, browser storage or shared caches. Logout, permission/client/identity changes and navigation SHALL erase pending private state and invalidate late completions. Diagnostics SHALL be bounded metadata only.

#### Scenario: Permission lost during inference
- **WHEN** authorization is lost or a current request returns401/403while inference is pending
- **THEN** private state is erased, completion cannot restore it, and no retry authority is created.

#### Scenario: Refresh, reload or telemetry
- **WHEN** a page reloads or an operation is observed in logs/traces/metrics
- **THEN** inference is not replayed and no conversation, model prompt/response, token or identifying metric label is retained.

### Requirement: Classification has no booking authority
Requesting, displaying or accepting triage suggestions SHALL NOT execute order, supplier, cancellation, refund, payment or customer-response actions. Read-only order context SHALL NOT release unresolved financial barriers or establish bank credit.

#### Scenario: Customer asks for money or cancellation
- **WHEN** classification sees such a request or recommends Cancellation/Refund
- **THEN** only a Support category suggestion is produced and financial commands/consent/barriers remain unchanged.

### Requirement: Separate source, runtime and quality acceptance
Source acceptance SHALL require strict specification validation, privacy/HTTP/contract/browser tests and ordinary exact-head CI including PR E2E. Real-model readiness and RU/EN quality versus a simple baseline SHALL require separately authorized, versioned evidence. Automatic routing SHALL remain outside S2.

#### Scenario: Fixture and ordinary CI pass
- **WHEN** tests using model-response fixtures and disposable ordinary CI pass
- **THEN** source integration behavior is accepted without claiming installed weights, target provisioning, model quality or operator-time savings.

#### Scenario: Real-model experiment or later promotion
- **WHEN** model installation, private-text use, evaluation or automatic routing is proposed
- **THEN** the separate runtime/experiment or S3 gate must be approved and measured before enablement.