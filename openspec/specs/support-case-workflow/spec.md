# support-case-workflow Specification

## Purpose

Дать клиенту приватный способ обратиться по проблеме с поездкой, а оператору — принять обращение, ответить и завершить его, сохранив права владельца, защищённую переписку и независимость финансовых операций от поддержки и AI.

## Requirements

### Requirement: Private authenticated customer access
Customer access SHALL require a validated authenticated identity and SHALL disclose only that customer's cases and messages. Missing and foreign resources SHALL have indistinguishable concealed responses. Operator access SHALL NOT grant financial order permissions.

#### Scenario: Foreign case or message
- **WHEN** a customer requests another owner's case, a nested message or a list with another owner identifier
- **THEN** no foreign content or existence is disclosed and the server derives the owner from the authenticated principal.

#### Scenario: Anonymous or invalid identity
- **WHEN** a request has no valid authenticated nonempty subject
- **THEN** access is rejected before content disclosure, decryption or mutation.

### Requirement: Dedicated operator queue and authority
An operator SHALL require the dedicated support permission and operator client identity for queue reads, replies, resolution and manual triage. Issuance SHALL restrict that permission to authorized operators without automatically granting booking scope. Every accepted action SHALL retain its authenticated actor and time; body identifiers SHALL NOT establish authority.

#### Scenario: Booking permission alone
- **WHEN** a user with booking permission but without support operator permission requests the operator queue or action
- **THEN** access is forbidden without revealing private content.

#### Scenario: Authorized reply
- **WHEN** an authorized operator replies to an accessible case
- **THEN** the persisted message records that operator and time, without granting cancellation or payment scope.

#### Scenario: Actual operator and non-operator token issuance
- **WHEN** the operator sign-in issues tokens for an authorized operator and an ordinary user who requests operator scope
- **THEN** only the authorized operator receives support permission; its token has no booking scope and the ordinary user's request does not grant operator authority.

### Requirement: Optional verified owned order link
A case SHALL be creatable without an order. An order link SHALL be accepted only after a successful owner-bound authoritative read; foreign, absent or unavailable context SHALL NOT become a verified link. Linking SHALL not mutate the order or import passenger or financial details into Support.

#### Scenario: No order supplied
- **WHEN** a customer submits a valid case without an order reference
- **THEN** the case is retained without any Flights lookup.

#### Scenario: Foreign order or unavailable lookup
- **WHEN** a supplied order is foreign or absent, or the ownership read is unavailable
- **THEN** before protection/storage/commit the reserved `Support.OrderLinkNotFound` or `Support.OrderLinkUnavailable` rejection creates no case and permits an explicit fresh CaseId/MessageId without a link; generic404/503 or transport loss keeps the unknown barrier.

#### Scenario: Held order before read model appears
- **WHEN** an owned booking is committed but its EF projection is not yet visible
- **THEN** the source ownership reader can validate its link without treating projection lag as proof of a missing order.

### Requirement: Bounded plain text input
S1 SHALL accept one in-app plain-text channel with a nonempty title of at most120Unicode scalars and messages of at most4000Unicode scalars/16KiBUTF-8. Oversize, malformed, duplicate or unknown fields SHALL be rejected with fixed errors before persistence. Text SHALL never execute markup or instructions.

#### Scenario: Invalid body or conflicting properties
- **WHEN** a body exceeds32KiB or contains duplicate, unknown or invalid fields
- **THEN** no case or message is persisted and diagnostics do not echo submitted text.

#### Scenario: HTML and model instructions
- **WHEN** valid-size text includes HTML, links or instructions to override application rules
- **THEN** it is displayed as plain text and invokes no tools, model or business operation.

### Requirement: Atomic retained creation
An accepted creation SHALL persist the case, first message, owner, revision, optional verified link and minimal action receipt atomically before reporting success. A failed commit SHALL not report a saved case. A snapshot or negative lookup SHALL NOT establish that a transport-uncertain write had no effect.

#### Scenario: Creation commit fails
- **WHEN** persistence fails before creation commits
- **THEN** neither an incomplete case nor a successful receipt is returned.

#### Scenario: Successful commit and lost reply
- **WHEN** creation commits but the browser loses the response
- **THEN** the known client resource ID supports an owner read; no automatic duplicate creation or inferred failure occurs.

### Requirement: Explicit conversation lifecycle
A new case SHALL be Open. An operator reply SHALL set WaitingForCustomer, a customer reply to a non-resolved case SHALL set Open, and an operator SHALL explicitly resolve a case. Replying to Resolved SHALL require the customer's explicit reopen action; historical messages SHALL remain unchanged.

#### Scenario: Reply and resolve
- **WHEN** the operator replies and later explicitly resolves at the current revision
- **THEN** the states progress WaitingForCustomer then Resolved, retaining both actions.

#### Scenario: Customer resumes a waiting conversation
- **WHEN** the owner replies to WaitingForCustomer
- **THEN** the case becomes Open with exactly one new message.

#### Scenario: Resolved conversation
- **WHEN** the owner sends an ordinary reply to Resolved or explicitly submits ReplyAndReopen
- **THEN** the ordinary reply is rejected; the explicit action atomically adds the message and returns Open.

### Requirement: Concurrency and repeated mutation identity
Every mutation SHALL use a client-chosen resource/action identity and expected revision where applicable. While the case and receipt remain retained, a repeated identity with matching authorized content SHALL return that receipt without another mutation. Different content or stale revisions SHALL fail explicitly; uncertainty SHALL not trigger automatic resubmission.

#### Scenario: Duplicate create or reply
- **WHEN** the same owner/action identity and exact content are submitted twice
- **THEN** only one case/message is retained and the same committed receipt is returned.

#### Scenario: Identity reused for different content
- **WHEN** an authorized caller reuses a committed identity with different text, order link or action
- **THEN** the request conflicts without overwriting the original or disclosing another actor's data.

#### Scenario: Reply races with resolution
- **WHEN** a reply and resolution use the same expected case revision
- **THEN** at most one commits and the loser receives a conflict requiring a fresh read and explicit decision.

#### Scenario: Lost reply beyond the displayed message page
- **WHEN** the response to a committed action is lost and its message is outside the displayed page
- **THEN** the original authorized actor can read its minimal retained receipt by the known action identity without another mutation or private-text decryption; current case state is read separately.

#### Scenario: Negative or unavailable observation while a write may run
- **WHEN** receipt lookup returns not-found, expired, forbidden or unavailable, or a page contains no matching message
- **THEN** the write is not declared failed or safe to repeat; uncertainty stays explicit and no automatic resubmission occurs.

#### Scenario: Another actor or action kind
- **WHEN** another actor requests a retained receipt or a mutation ID is reused for another action kind
- **THEN** receipt access is concealed unless the original actor and current access match, and conflicting reuse cannot create another action.

### Requirement: Manual triage with explicit provenance
S1 SHALL retain manual category and Normal/High priority only from an authorized operator. New cases SHALL be Unclassified/Normal with source Default; operator corrections SHALL record before/after, actor and time. Customer text SHALL not establish payment, refund, order state or an automatically confirmed urgency.

#### Scenario: Customer alleges a refund or imminent departure
- **WHEN** text claims money arrived or a flight is imminent
- **THEN** it remains customer text; the case gets no invented financial status or automatic trusted urgency.

#### Scenario: Operator corrects category
- **WHEN** an operator changes a category or priority at the current revision
- **THEN** the new manual choice and its audit provenance are retained.

### Requirement: Protected private text
All stored free text SHALL be protected with an owner/case/message/revision-bound purpose before persistence. Authorization SHALL precede decryption. Missing keys or unreadable text SHALL fail closed rather than write plaintext, return empty content or report a successful readable conversation.

#### Scenario: New write without available protection
- **WHEN** required protection is unavailable
- **THEN** a fixed unavailable response is returned before a new write, without key bootstrap or plaintext fallback.

#### Scenario: Wrong binding or lost read key
- **WHEN** stored text cannot be read under its retained binding and authorized context
- **THEN** content access fails explicitly without revealing ciphertext or pretending the conversation is empty.

#### Scenario: Persisted ciphertext after restart or key rotation
- **WHEN** a compatible service restarts or rotates its certificate while retaining the required read key
- **THEN** recorded title/body can still be read under their original bindings, while another binding or modified ciphertext is rejected.

### Requirement: Private reads and bounded pagination
Case and message reads SHALL return no-store responses, bounded pages and stable continuation. Protected text, tokens and submitted content SHALL not enter URLs, browser storage, logs, durable messaging or shared response caches. A read failure SHALL not look like a successful empty queue.

#### Scenario: Unauthorized response and diagnostics
- **WHEN** access is rejected or a private operation fails
- **THEN** no-store remains, and diagnostics contain fixed codes and permitted correlation metadata only.

#### Scenario: More messages or a failed queue read
- **WHEN** more than20messages exist or the queue request fails
- **THEN** pagination preserves access boundaries; a failure is displayed as an error and cannot become an empty successful result.

### Requirement: Retention and physical removal
Resolved case content SHALL become unavailable90days after resolution and SHALL be physically deleted by a bounded idempotent retention pass. Reopening before expiry SHALL cancel that deadline. Support removal SHALL leave Flights history unchanged and SHALL not depend on text decryption.

#### Scenario: Deadline and interrupted retention pass
- **WHEN** the resolution retention deadline is reached and a cleanup pass stops partway through
- **THEN** expired content is no longer disclosed; the next pass removes remaining case-owned content without duplicate side effects.

#### Scenario: Reopen versus deletion
- **WHEN** reopen and deletion race near the deadline
- **THEN** only a conditionally valid operation commits; expired or deleted content cannot be revived, and unrelated order evidence remains.

### Requirement: Support does not execute booking or AI operations
S1 case operations and reads SHALL not invoke a classifier, supplier, payment or financial booking command, grant consent or release unresolved booking barriers. Laya absence SHALL not impair the manual workflow; customer/operator messages SHALL not become business outcome evidence.

#### Scenario: Unresolved booking and repeated support reads
- **WHEN** an owner with an unresolved booking opens, creates or reads a support case
- **THEN** existing booking uncertainty remains and no booking, cancellation, refund or model dispatch occurs.

#### Scenario: Classifier unavailable
- **WHEN** no Laya runtime, model or configuration exists
- **THEN** all supported S1 creation, queue, conversation and status operations remain available.

### Requirement: Browser identity and write lifetime
Identity, client-context or required-permission changes, logout, navigation and current-request authorization denial SHALL invalidate pending private completions and erase private text. Unknown mutation metadata SHALL not become permission to repeat after loss of access; current-state reads SHALL not prove the outcome of another action.

#### Scenario: Late old identity response
- **WHEN** an earlier identity's response completes after logout or owner replacement
- **THEN** it cannot expose private text or overwrite the current session.

#### Scenario: Unknown reply and reload
- **WHEN** a reply response is lost and the page is reloaded
- **THEN** the page reads the authorized retained conversation without an automatic reply resend or release of Flights barriers.

#### Scenario: Private server rendering and unsafe auth return
- **WHEN** a Support route is requested before client initialization or an auth callback supplies an external or foreign return path
- **THEN** private content is not prerendered or transferred through server state, and the unsafe return is rejected.

#### Scenario: Permission lost without identity change
- **WHEN** required Support permission disappears on token refresh or a current request receives401/403while GUID/client stay the same
- **THEN** private content is erased, old callbacks cannot restore it, and pending Support or Flights mutation uncertainty remains unresolved rather than becoming retry permission.

### Requirement: Source acceptance and separate runtime authority
Acceptance SHALL require domain, privacy, HTTP, concurrency, persistence and browser evidence in the appropriate layers, ordinary exact-head CI and PR E2E. Planning/source tests SHALL not establish key provisioning, schema application, deployed operator permissions, Laya quality or external delivery.

#### Scenario: Fixture and CI success
- **WHEN** isolated and disposable CI checks pass
- **THEN** the result certifies those application paths only, with provisioning/deployment/model gates reported separately.

#### Scenario: Plan ready
- **WHEN** this planning corpus validates successfully
- **THEN** no code, migrations, keys, operator grants or model invocation begin until separately authorized.
