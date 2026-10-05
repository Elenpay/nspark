using System.Text.Json;
using NSpark.Signer;

namespace NSpark.RemoteSigner;

/// <summary>How a <see cref="RemoteSparkSigner"/> reaches the signer service.</summary>
public interface ISparkSignerTransport
{
    /// <summary>POSTs <paramref name="jsonBody"/> to <paramref name="path"/>; returns the HTTP status and body.</summary>
    public Task<(int Status, string Body)> InvokeAsync(string path, string jsonBody, CancellationToken ct);
}

/// <summary>A remote signer refused or failed a request.</summary>
public sealed class SparkRemoteSignerException(string op, int status, string? error)
    : Exception($"remote Spark signer {op} -> {status}: {error}")
{
    public string Operation { get; } = op;

    public int Status { get; } = status;
}

/// <summary>
/// Client side of <see cref="SparkSignerContract"/>: an <see cref="ISparkSigner"/> that holds no key
/// material and forwards every operation to the signer service. Operations outside the contract
/// throw <see cref="NotSupportedException"/> without a round trip.
/// </summary>
public sealed class RemoteSparkSigner : ISparkSigner
{
    private readonly ISparkSignerTransport _transport;
    private readonly string _wallet;
    private byte[]? _identity;

    /// <param name="transport">Transport to the signer service.</param>
    /// <param name="wallet">Id of the seed the signer should use (e.g. its master fingerprint).</param>
    public RemoteSparkSigner(ISparkSignerTransport transport, string wallet)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentException.ThrowIfNullOrEmpty(wallet);
        _transport = transport;
        _wallet = wallet;
    }

    /// <summary>The signer's identity and policy, to verify before using it.</summary>
    public Task<SparkSignerInfo> GetInfoAsync(CancellationToken ct = default) =>
        CallAsync<SparkSignerInfo>(SparkSignerContract.Ops.Info, new WalletRequest(_wallet), ct);

    private async Task<T> CallAsync<T>(string op, object request, CancellationToken ct)
    {
        var (status, body) = await _transport
            .InvokeAsync(SparkSignerContract.PathOf(op), JsonSerializer.Serialize(request, SparkSignerContract.Json), ct)
            .ConfigureAwait(false);

        SparkSignerEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<SparkSignerEnvelope>(string.IsNullOrEmpty(body) ? "{}" : body, SparkSignerContract.Json);
        }
        catch (JsonException)
        {
            // Not a Spark signer (e.g. a signer that predates the Spark routes answering as a PSBT signer).
            throw new SparkRemoteSignerException(op, status, "response is not a Spark signer envelope");
        }

        if (status != 200 || envelope?.Error is not null || envelope?.Result is null)
        {
            throw new SparkRemoteSignerException(op, status, envelope?.Error ?? "empty result");
        }

        return envelope.Result.Value.Deserialize<T>(SparkSignerContract.Json)
               ?? throw new SparkRemoteSignerException(op, status, "empty result");
    }

    public async Task<byte[]> GetIdentityPublicKeyAsync(CancellationToken ct = default) =>
        _identity ??= await CallAsync<byte[]>(SparkSignerContract.Ops.IdentityPublicKey, new WalletRequest(_wallet), ct).ConfigureAwait(false);

    public Task<byte[]> SignWithIdentityKeyAsync(byte[] messageHash, CancellationToken ct = default) =>
        CallAsync<byte[]>(SparkSignerContract.Ops.SignWithIdentity, new HashRequest(_wallet, messageHash), ct);

    public Task<byte[]> SignCompactWithIdentityKeyAsync(byte[] messageHash, CancellationToken ct = default) =>
        CallAsync<byte[]>(SparkSignerContract.Ops.SignCompactWithIdentity, new HashRequest(_wallet, messageHash), ct);

    public Task<byte[]> DecryptEciesWithIdentityKeyAsync(byte[] ciphertext, CancellationToken ct = default) =>
        throw new NotSupportedException("A remote Spark signer does not release ECIES plaintexts.");

    public Task<byte[]> GetDepositPublicKeyAsync(CancellationToken ct = default) =>
        CallAsync<byte[]>(SparkSignerContract.Ops.DepositPublicKey, new WalletRequest(_wallet), ct);

    public Task<byte[]> GetLeafPublicKeyAsync(string leafId, CancellationToken ct = default) =>
        CallAsync<byte[]>(SparkSignerContract.Ops.LeafPublicKey, new LeafRequest(_wallet, leafId), ct);

    public Task<LeafFrostSignature> SignLeafFrostAsync(string leafId, byte[] message, byte[] verifyingKey,
        IReadOnlyDictionary<string, SigningCommitment> soCommitments, byte[]? adaptorPublicKey = null,
        CancellationToken ct = default) =>
        CallAsync<LeafFrostSignature>(SparkSignerContract.Ops.SignLeafFrost,
            new SignLeafFrostRequest(_wallet, leafId, message, verifyingKey,
                soCommitments.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal), adaptorPublicKey), ct);

    public Task<LeafFrostNonceCommitment> GenerateLeafFrostNonceAsync(string leafId, CancellationToken ct = default) =>
        throw new NotSupportedException("A remote Spark signer does not serve two-phase FROST nonces.");

    public Task<byte[]> SignLeafFrostWithNonceAsync(string leafId, byte[] nonceHandle, byte[] message,
        byte[] verifyingKey, IReadOnlyDictionary<string, SigningCommitment> soCommitments,
        byte[]? adaptorPublicKey = null, CancellationToken ct = default) =>
        throw new NotSupportedException("A remote Spark signer does not serve two-phase FROST nonces.");

    public Task<EncryptedSendTweakBatch> BuildEncryptedSendTweaksAsync(IReadOnlyList<SendTweakLeafDescriptor> leaves,
        IReadOnlyList<SoTarget> soTargets, string transferId, uint threshold, CancellationToken ct = default) =>
        CallAsync<EncryptedSendTweakBatch>(SparkSignerContract.Ops.BuildSendTweaks,
            new BuildSendTweaksRequest(_wallet, leaves.ToList(), soTargets.ToList(), transferId, threshold), ct);

    public Task<EncryptedClaimTweakBatch> BuildEncryptedClaimTweaksAsync(IReadOnlyList<ClaimTweakLeafDescriptor> leaves,
        IReadOnlyList<SoTarget> soTargets, uint threshold, CancellationToken ct = default) =>
        CallAsync<EncryptedClaimTweakBatch>(SparkSignerContract.Ops.BuildClaimTweaks,
            new BuildClaimTweaksRequest(_wallet, leaves.ToList(), soTargets.ToList(), threshold), ct);

    public Task<byte[]> GetStaticDepositPublicKeyAsync(int index = 0, CancellationToken ct = default) =>
        CallAsync<byte[]>(SparkSignerContract.Ops.StaticDepositPublicKey, new IndexRequest(_wallet, index), ct);

    public Task<byte[]> ExportStaticDepositPrivateKeyAsync(int index = 0, CancellationToken ct = default) =>
        throw new NotSupportedException("A remote Spark signer never exports private keys.");

    public Task<LeafFrostNonceCommitment> GenerateStaticDepositFrostNonceAsync(int index, CancellationToken ct = default) =>
        throw new NotSupportedException("A remote Spark signer does not serve static-deposit nonces.");

    public Task<byte[]> SignStaticDepositFrostWithNonceAsync(int index, byte[] nonceHandle, byte[] message,
        byte[] verifyingKey, IReadOnlyDictionary<string, SigningCommitment> soCommitments,
        CancellationToken ct = default) =>
        throw new NotSupportedException("A remote Spark signer does not serve static-deposit nonces.");

    public Task<EncryptedPreimageShareBundle> BuildEncryptedPreimageSharesAsync(string transferId,
        IReadOnlyList<SoTarget> soTargets, uint threshold, CancellationToken ct = default) =>
        CallAsync<EncryptedPreimageShareBundle>(SparkSignerContract.Ops.BuildPreimageShares,
            new BuildPreimageSharesRequest(_wallet, transferId, soTargets.ToList(), threshold), ct);

    public Task<AdaptorKeyHandle> GenerateAdaptorKeyAsync(CancellationToken ct = default) =>
        throw new NotSupportedException("A remote Spark signer does not serve adaptor keys (SSP leaf swaps).");
}
