# S1 wire contract — implementation coordination

All dates are UTC ISO8601, all resource IDs nonempty UUIDs, revisions positive Int32 integers. Actor/owner authority comes only from server principal. DTO field names below are shared across API, TS and demo; no private text in receipts/cursors.

Case DTO: `caseId, ownerId, title, state, revision, category, priority, triageSource, orderId, orderVerifiedAt, orderStreamVersion, createdAt, updatedAt, resolvedAt, purgeAt`. Nullable link/resolution fields are explicit null. State=Open/WaitingForCustomer/Resolved; category=Unclassified/Payment/Ticketing/Cancellation/Refund/FareConditions/Ancillaries/Technical/Other; priority=Normal/High; triageSource=Default/Operator.

Case page: `{items: CaseDto[], nextCursor: string|null}`. Message DTO: `{caseId,messageId,actorKind,body,writtenAtRevision,createdAt}`; actorKind=Customer/Operator. Message page: `{caseId,items: MessageDto[],nextCursor:string|null}`.

Receipt DTO: `{caseId,actionId,kind,appliedRevision,appliedState,appliedAt,messageId}`; messageId nullable for control actions. Kind=Create/CustomerReply/ReplyAndReopen/OperatorReply/Resolve/SetTriage. Creation actionId=first messageId; reply actionId=messageId; controls use actionId. One namespace per case.

Create `PUT /api/support/cases/{caseId}` body: `{messageId,title,body,orderId?:string|null}`. Customer reply `PUT /api/support/cases/{caseId}/messages/{messageId}` body: `{body,expectedRevision,kind:"Reply"|"ReplyAndReopen"}`. Operator reply corresponding `/operator/cases/...` body: `{body,expectedRevision,kind:"Reply"}`. Control action `PUT /api/support/operator/cases/{caseId}/actions/{actionId}` body: `{kind:"Resolve",expectedRevision}` or `{kind:"SetTriage",expectedRevision,category,priority}`; Unclassified allowed for explicit manual correction.

Customer GET list/detail/messages/receipt routes and operator equivalents follow designD4. Receipt GET returns actor-bound metadata only. List accepts optional cursor and enum state/category/priority filters, max20. Messages use optional cursor, max20. Fixed ProblemDetails types start `Support.`;401/403clear active private context,404/503are not mutation NoEffect proof,409requires explicit current-state review.

Create-only exceptions: `Support.OrderLinkNotFound`404 / `Support.OrderLinkUnavailable`503 are reserved for failed new-link verification before any protection/store/commit. Only these observed fixed rejection responses permit explicit fresh unlinked creation with preserved draft. Generic404/503/transport and any receipt-negative remain unknown. Production timestamps are normalized to UTC microseconds before storage/initial receipt; UTC Z and +00:00 forms both represent the same allowed instant.
