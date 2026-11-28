# trip-saved-options Specification

## Purpose

Позволяет владельцу в один шаг сохранить показанный вариант в личном плане и после нового входа увидеть выбранную поездку по дням вместе с историческими ценами.

## Requirements

### Requirement: Explicit creation from one displayed option

An explicit save SHALL create one owner-private Trips plan at revision 1 and its complete historical option atomically. The new plan SHALL be reachable from the success response and the ordinary owner's list. The operation SHALL accept only a valid owner-bound selection. It SHALL perform zero model, supplier, order, payment or reservation actions.

#### Scenario: Save with an empty title
- **WHEN** the traveler clicks «Сохранить поездку» on the evening option with a blank title
- **THEN** Trips opens the created plan with a destination-and-date title; that same plan appears once in the owner's list. A valid explicit title is retained if supplied.

### Requirement: Atomic admission to the last owner slot

Ordinary creation and saving a selection SHALL serialize admission for the same issuer and subject. The limit is 100 active plans. A replay of a committed selection SHALL be resolved before first-use expiry and capacity checks. A failed transaction SHALL leave no partial plan, historical snapshot or receipt.

#### Scenario: Two requests redeem one selection at 99 plans
- **WHEN** two independent connections submit the same selection for an owner with 99 manual plans
- **THEN** one returns 201 and the other 200 with the same trip ID. A third connection sees exactly 100 plans, one new historical snapshot, one receipt and revision 1 for the new plan.

#### Scenario: Ordinary create wins the last slot
- **WHEN** the ordinary create holds the owner lock first, the selection save waits, and the ordinary create commits
- **THEN** the ordinary create returns 201 and the selection save returns 409 Trips.CapacityExceeded. The owner has 100 plans and no snapshot, plan or receipt for the losing selection.

#### Scenario: Selection save wins the last slot
- **WHEN** the selection save holds the owner lock first, the ordinary create waits, and the selection save commits
- **THEN** the selection save returns 201, the ordinary create returns 409 Trips.CapacityExceeded, and a repeat of the winning selection returns 200 for the same plan at full capacity. Exactly one new snapshot and receipt exist.

#### Scenario: First writer rolls back while the other waits
- **WHEN** a selection save has written uncommitted rows for the last slot, another create waits for that owner's lock, and a failure rolls the first transaction back
- **THEN** the waiter can create the 100th plan. Independent observations show only the winner's records. A retry of a rolled-back selection that now competes with 100 committed plans receives the capacity conflict.

### Requirement: Historical calendar and price meaning

Reopening SHALL display the selected flight legs, local times, hotel and nights, walking dates, windows and ordered stops exactly as saved. Flight and hotel SHALL be labelled «Запланировано». The saved view SHALL retain the fictional source, reference instant, versions, attribution, price components in their original currencies and unknown expenses. It SHALL distinguish these amounts from editable user estimates.

#### Scenario: Cold reopen of the evening plan with generation disabled
- **WHEN** the owner saves B, closes the browser, stops Host, Keycloak and PostgreSQL, resumes with the same database and keys, keeps Trips and historical reads enabled, but disables walking generation, option preview and selection issuance/creation
- **THEN** a fresh authenticated browser opens the same ID from the owner's list without a selection token. The revision and digest of the canonical historical snapshot match the values before restart.
- **AND** it shows planned RT1/H1 for two adults and two nights, arrival at 20:00 and check-in at 21:30 on 28 October 2026, no walk that evening, Green Bridge then Stroganov Palace on the 29th for 38 minutes, check-out at 08:00 and return departure at 11:00 on the 30th. Known costs remain 18,000 RUB plus 12,000 RUB; the trip total remains unknown.
- **AND** reading the saved calendar requires no preview, discovery response, current data release, repricing, model, supplier, order, payment or new mutation.

#### Scenario: Changed catalogue and expired source
- **WHEN** B is reopened with generation enabled and A selected, or after the previously used source has expired
- **THEN** the saved B facts are returned without regenerating an option. These checks are additional to cold reopening with generation disabled.

#### Scenario: Different price basis and later manual edits
- **WHEN** a one-night option with a supplier net amount or a trip with a 1 EUR budget is saved and reopened
- **THEN** net remains distinct from traveler price and foreign charges retain their currency. No currency conversion or budget-fit claim is invented.
- **AND** ordinary title, notes or calendar edits preserve the historical option, keep the manual subtotal separate and never double-count the saved price.

### Requirement: Idempotent and recoverable save of one selection

One issued selection SHALL identify at most one plan across concurrent submissions and lost responses. The UI SHALL suppress a second in-flight click and SHALL NOT automatically resend a mutation. An explicit replay SHALL identify the original plan without changing its title, revision or contents. Replay SHALL NOT resurrect a deleted plan.

#### Scenario: Commit succeeded and the original response was lost
- **WHEN** the first POST commits but its response is dropped before the browser receives it
- **THEN** the UI keeps the issued trip ID and token in the owner's current session, shows an uncertain result and sends no automatic mutation. A check of that specific ID opens the committed option; an explicit retry with the same token returns 200 and the original ID, including at full capacity. The original title, snapshot and revision are preserved.

#### Scenario: A read returns 404 before the original transaction commits
- **WHEN** the first save is still uncommitted, the user checks the issued ID, receives 404 and refreshes the list
- **THEN** the operation stays uncertain. The UI preserves the original ID and token, keeps ordinary creation blocked for this operation and permits an explicit retry with that same token. Releasing the original transaction and the waiting retry produces exactly one saved plan.
- **AND** starting a distinct trip while the result is uncertain requires an explicit decision to abandon recovery. A late response cannot navigate the user away from the new trip.

#### Scenario: Expired token used for a committed or unused selection
- **WHEN** a genuine owner-bound token is submitted after its first-save expiry while the write route is enabled
- **THEN** the committed, still-existing selection returns 200 with its original ID even at full capacity. An unused expired token is refused. When writes are disabled the owner can still verify a committed result via GET by ID.

#### Scenario: Deleted selection and receipt cleanup
- **WHEN** a saved plan is deleted and its selection is replayed before or after the receipt is pruned
- **THEN** neither attempt recreates the plan. First-use admission and receipt pruning use the same authoritative expiry boundary, including for requests delayed by the owner lock.

### Requirement: Private bounded storage and compatibility

The server SHALL derive ownership from the authenticated principal, validate the issued selection and bound decrypted payloads and responses. The snapshot SHALL be protected at rest and removed with its plan. Reads SHALL conceal foreign plans. Old manual plans, versioned workspace APIs and confirmed-order links SHALL retain their existing behavior.

#### Scenario: Foreign owner or substituted data
- **WHEN** a second owner tries to redeem another owner's selection, read its plan, or submits altered selection bytes or client-supplied prices
- **THEN** no private option is disclosed and no write occurs. Private responses use no-store; tickets and snapshot details stay out of URLs, logs, browser persistence and SSR.

#### Scenario: Historical and ordinary plans coexist
- **WHEN** a traveler reopens an old manual plan and a new plan saved from a preview
- **THEN** the old plan opens normally; only the latter has a historical option. A storage or protection failure is shown distinctly from the absence of a saved option.

### Requirement: Schema-first compatibility for stored options

The additive saved-option schema SHALL be installed and verified before a new Host serves Trips traffic. The preceding Host SHALL support ordinary manual plan reads and creates on the expanded schema. With the migration missing, the new Host SHALL report Trips unready, including when option saving is disabled; normal startup SHALL NOT apply migrations.

#### Scenario: Upgrade with manual plans already present
- **WHEN** the old Host and schema contain a manual plan, the additive migration is applied, and the old Host remains active
- **THEN** that Host can read the existing plan and create another with no historical option. The new Host on the expanded schema can read both and create an ordinary plan with saving off. Enabling saving is a separate step.

#### Scenario: Missing schema or unreadable stored evidence
- **WHEN** the new Host is checked against an unmigrated schema, or the historical payload of an existing saved plan cannot be read with the configured keys
- **THEN** the former fails readiness without changing the schema. In the latter case the historical view reports unavailability; only an authorized manual plan with a genuine NULL snapshot reports no saved option.
