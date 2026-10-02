# Flights PII keys: initialization, retention and recovery

This is an operational procedure, not evidence that it has been performed. The M2.2 implementation used only disposable fictional test keys. Normal non-Production Host/AppHost startup applies schema: do not start it merely to initialize keys. Provisioning, restore, deployment and replay each require the appropriate environment authorization.

## Configuration and initialization

The private provider reads `Flights:PiiProtection`:

| Setting | Meaning |
| --- | --- |
| `KeyRingPath` | Absolute, non-root directory outside source and web content; stable across restarts |
| `ActiveCertificatePath` | Absolute PKCS#12 path with a private key, used to protect new key XML |
| `ActiveCertificatePassword` | Secret from a target-local secret source; never command history, Git or chat |
| `ReadCertificates:0:Path`, `ReadCertificates:0:Password` | Retained private certificates for older key XML; additional numbered entries supported |

Set restrictive filesystem ACLs for the service identity and authorized recovery operators before provisioning. Back up the encrypted key ring and certificate/private-key material through separate access-controlled channels. A certificate file next to its password in an ordinary backup is not an adequate custody boundary. Never emit paths, passwords, certificates, plaintext or ciphertext into logs/support bundles.

After installing configuration in the authorized environment, the explicit command is:

```text
dotnet Travel.Host.dll flights-pii-keys initialize --execute
```

This command builds only configuration and the Flights crypto facade; it registers no EF/Marten/Wolverine stores and starts no HTTP service. Without the exact `initialize --execute` arguments it exits 2 without mutation. A valid active certificate is required before creating the directory/key. A pre-existing nonempty ring is refused (exit 3); success exits 0. The empty `.initialize.lock` file is retained for process exclusion. There is no reset/delete command. Never delete an existing ring to make initialization succeed.

Ordinary Protect/Unprotect refuse a missing, empty or unusable ring without bootstrap. Framework rotation may create subsequent keys within an initialized ring; retain all keys while any event, inbox, durable command or backup references them. The provider uses the framework's lifetime/rotation rules with a stable application name and purpose chains; changing those strings breaks decryption. Deploy consistent configuration to every participating instance. Restart after configuration/certificate changes to load the new certificate set.

## Availability and recovery

The dependency health check distinguishes NotConfigured/Unavailable/Ready. It is not a readiness dependency for metadata/search, nor an audit that all historical ciphertext can decrypt. No keys means safe 503 for new holds/inbox writes. Already-processed inbox rows need no key; duplicates with valid signatures are recognized before encryption.

1. Stop affected protected writes/consumers under the normal maintenance procedure; preserve durable failures, inbox rows and encrypted payloads. Record only safe IDs and error codes.
2. Restore the complete ring and matching retained private certificates from trusted backups into the approved paths with service ACLs. Never manufacture replacement keys for missing data. If required key/certificate material is lost, the corresponding ciphertext is unrecoverable.
3. With authorized fictional samples, validate both old and new ciphertext across a restarted provider using the original booking/owner or inbox context. A new-write health success alone is insufficient. Restore must include the original application/purpose identifiers.
4. Missing-key processing uses existing 1/5/30s retry then DLQ; malformed/unsupported envelopes are terminal. Neither acknowledges an unprocessed inbox row. After restoring keys, use the existing [booking consistency diagnostics/replay procedure](booking-read-model-recovery.md). Check source/projection prerequisites and the replay allowlist; do not bulk acknowledge rows or endlessly retry malformed data.
5. Verify exactly one event/notification and the processed flag after controlled replay, then restore normal traffic. Retain audit evidence without payloads or secrets.

Certificate rotation: retain the old private certificate as a read certificate, select the new active certificate, and restart. New ring keys use the active certificate; existing XML remains protected with the old one. Test both generations and their backup restore before retiring any key/certificate. Deleting a profile or rotating a certificate is not cryptographic erasure of booking history.

## Upgrade and rollback

Drain old writers and pending plaintext hold commands before deploying the V2 writer. The new command has no legacy plaintext adapter; old commands missing the protected envelope fail before provider effects. All readers, projection workers and diagnostics must understand both legacy and V2 representations before accepting protected traffic. Do not run old and new writers against the same stream/inbox during rollback.

Before the first V2 write, ordinary version rollback is possible after draining. After V2/new inbox writes, old binaries cannot consume their representation: stop traffic and deploy a compatible forward repair or a separately approved coordinated restore. Do not downgrade by decrypting events, rewriting history or automatically resetting keys.

No EF column/model migration is part of M2.2. Legacy V1 events, projection rows and raw inbox payloads retain their historical representation, including plaintext; source rebuild is not a privacy migration. Existing idempotency hashes remain unkeyed. Only fictional data is permitted throughout this demonstration.
