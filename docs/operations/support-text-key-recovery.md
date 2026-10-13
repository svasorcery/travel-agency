# Support private-text keys

This procedure is source documentation. Keys, certificates and target configuration have not been provisioned by S1 source work. Ordinary Development/Testing Host startup applies schema, so key administration uses its separate early-exit command.

Configure `Support:TextProtection` through an approved local secret source:

| Setting | Meaning |
| --- | --- |
| `KeyRingPath` | Absolute non-root persistent directory outside source/web content |
| `ActiveCertificatePath` | Absolute PKCS#12 private certificate protecting new key XML |
| `ActiveCertificatePassword` | Secret; never shell arguments/history, source or logs |
| `ReadCertificates:N:Path`, `ReadCertificates:N:Password` | Retained private certificates for reading older key XML |

Restrict paths to the service/recovery identities. Back up the full encrypted ring and private certificate material through separate access-controlled channels. After target authorization and configuration, the explicit command is:

```text
dotnet Travel.Host.dll support-text-keys initialize --execute
```

The command exits before any web Host, EF/Marten/Wolverine registration or schema application. Exact arguments are mandatory; invalid arguments exit2 without configuration/key access and are not echoed. Success emits only `Support.KeysInitialized` (exit0); failure emits a fixed Support error (exit1). It requires a valid configured private certificate before creating the ring. A nonempty ring is refused; the empty `.initialize.lock` remains for process exclusion. There is no reset/delete operation. Never remove a ring to make initialization succeed.

Normal reads/writes reject missing/empty/unusable protection with fixed503; they never bootstrap keys or store plaintext. Framework rotation can add keys to an initialized ring. Keep application name `Travel.Support.Text.v1` and purpose bindings owner/case/title-or-body/message/written-at revision stable. Written revision is retained even when the case revision advances.

For recovery, preserve encrypted records and unknown action metadata, restore the complete original ring and matching certificates, then restart with consistent configuration. Validate both old and new authorized test ciphertext; Ready alone does not prove all historical records decrypt. Rotate by retaining the old certificate as a read certificate and restarting with the new active certificate. Missing original material makes corresponding ciphertext unreadable; new keys cannot reconstruct it.

Resolved cases become inaccessible at `resolvedAt+90days`; the hourly retention pass deletes only Support rows in batches≤50, at most1000 per pass after schema readiness. Health exposes protection/schema/retention status without text or paths. Backups have their own retention policy; deletion from the active Support schema does not prove erasure from backups. No Flights key, row or financial authority is part of this procedure.
