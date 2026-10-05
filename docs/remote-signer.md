# Remote signer contract (Elenpay fork)

`src/NSpark.RemoteSigner` lets a Spark wallet run with its keys in a separate service, for
example an AWS Lambda that KMS-decrypts the seed, while the wallet (NSpark's `SparkWallet`)
runs elsewhere. It is not part of the NSpark package.

| Piece | Side | Role |
|---|---|---|
| `SparkSignerContract` | both | `POST /spark/{op}` routes, request records, `{result,error}` envelope, JSON settings, `Version` |
| `RemoteSparkSigner` | client | `ISparkSigner` that forwards every operation over an `ISparkSignerTransport` |
| `SparkSignerHttpHandler` | service | path → operation → `ISparkSigner`, with status codes 200/400/403/404/500 |
| `SparkSignerDispatcher` + `SparkSignerPolicy` | service | the checks a blind signer can make |

## What a remote signer does and doesn't serve

Served: identity/deposit/leaf/static-deposit public keys, identity ECDSA signatures, one-shot
leaf FROST shares, send/claim key-tweak batches and Lightning preimage shares, plus `info`
(identity, policy and contract version, for the caller to verify before use).

Not served, by design: two-phase FROST nonces, static-deposit private-key export, adaptor keys
(SSP leaf swaps) and ECIES decryption. `RemoteSparkSigner` throws `NotSupportedException` for
them without a round trip. Static deposits and amount-exact exits that need a leaf swap are
therefore unavailable; withdraw-all cooperative exits, Lightning receives and claims work.

## Policy

- Tweak and preimage shares go only to the pinned operator set, at the pinned threshold.
- Outbound key tweaks go only to an allowed receiver (the SSP) or back to the signer's own identity.
- Adaptor signatures are refused.

The signer still signs FROST shares and identity hashes blindly: policy bounds where leaves can
go, not what the SSP does with them. Keep balances transit-only.

## Contract changes

`tests/NSpark.RemoteSigner.Tests/Fixtures/*.json` pin the request body of every operation; the
signer service's tests read the same files. Regenerate with `NSPARK_UPDATE_FIXTURES=1 dotnet test`,
review the diff, and bump `SparkSignerContract.Version` on an incompatible change.
