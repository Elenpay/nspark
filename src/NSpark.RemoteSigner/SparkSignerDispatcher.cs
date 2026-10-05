using System.Text.Json;
using NSpark.Signer;

namespace NSpark.RemoteSigner;

/// <summary>
/// The only checks a blind Spark signer can make. Key tweaks and preimage shares may only be
/// built for the pinned operator set at the pinned threshold; outbound key tweaks may only go to
/// an allowed receiver (the SSP) or back to the signer's own identity.
/// </summary>
/// <param name="OperatorKeys">Compressed identity keys (hex) of the signing operators.</param>
/// <param name="Threshold">Operator signing threshold.</param>
/// <param name="AllowedReceivers">Compressed identity keys (hex) allowed to receive leaves.</param>
public sealed record SparkSignerPolicy(
    IReadOnlySet<string> OperatorKeys,
    uint Threshold,
    IReadOnlySet<string> AllowedReceivers)
{
    /// <summary>Builds a policy, normalizing keys to lowercase hex.</summary>
    public static SparkSignerPolicy Create(IEnumerable<string> operatorKeys, uint threshold, IEnumerable<string> allowedReceivers) =>
        new(Normalize(operatorKeys), threshold, Normalize(allowedReceivers));

    private static HashSet<string> Normalize(IEnumerable<string> keys) =>
        keys.Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
}

/// <summary>The request was refused by policy (HTTP 403).</summary>
public sealed class SparkSignerPolicyException(string message) : Exception(message);

/// <summary>The operation does not exist (HTTP 404).</summary>
public sealed class SparkSignerUnknownOperationException(string op) : Exception($"unknown Spark signer operation '{op}'");

/// <summary>The request body is malformed (HTTP 400).</summary>
public sealed class SparkSignerBadRequestException(string message) : Exception(message);

/// <summary>
/// Server side of <see cref="SparkSignerContract"/>: maps an operation and its JSON body onto an
/// <see cref="ISparkSigner"/> call, enforcing <see cref="SparkSignerPolicy"/>. Seed selection and
/// the HTTP envelope are the host's job.
/// </summary>
public static class SparkSignerDispatcher
{
    /// <summary>Reads the wallet id of a request body before the host selects the seed.</summary>
    public static string ReadWallet(string body) =>
        Parse<WalletRequest>(body).Wallet is { Length: > 0 } wallet
            ? wallet
            : throw new SparkSignerBadRequestException("missing wallet");

    /// <summary>Runs <paramref name="op"/> and returns the value to put in the envelope's result.</summary>
    public static async Task<object> DispatchAsync(
        string op, string body, ISparkSigner signer, SparkSignerPolicy policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(policy);

        switch (op)
        {
            case SparkSignerContract.Ops.Info:
            {
                var identity = await signer.GetIdentityPublicKeyAsync(ct).ConfigureAwait(false);
                return new SparkSignerInfo(
                    SparkSignerContract.Version,
                    Convert.ToHexString(identity).ToLowerInvariant(),
                    policy.OperatorKeys.Order(StringComparer.Ordinal).ToList(),
                    policy.Threshold,
                    policy.AllowedReceivers.Order(StringComparer.Ordinal).ToList());
            }

            case SparkSignerContract.Ops.IdentityPublicKey:
                return await signer.GetIdentityPublicKeyAsync(ct).ConfigureAwait(false);

            case SparkSignerContract.Ops.SignWithIdentity:
                return await signer.SignWithIdentityKeyAsync(Hash32(Parse<HashRequest>(body).MessageHash), ct).ConfigureAwait(false);

            case SparkSignerContract.Ops.SignCompactWithIdentity:
                return await signer.SignCompactWithIdentityKeyAsync(Hash32(Parse<HashRequest>(body).MessageHash), ct).ConfigureAwait(false);

            case SparkSignerContract.Ops.DepositPublicKey:
                return await signer.GetDepositPublicKeyAsync(ct).ConfigureAwait(false);

            case SparkSignerContract.Ops.LeafPublicKey:
                return await signer.GetLeafPublicKeyAsync(Parse<LeafRequest>(body).LeafId, ct).ConfigureAwait(false);

            case SparkSignerContract.Ops.StaticDepositPublicKey:
                return await signer.GetStaticDepositPublicKeyAsync(Parse<IndexRequest>(body).Index, ct).ConfigureAwait(false);

            case SparkSignerContract.Ops.SignLeafFrost:
            {
                var r = Parse<SignLeafFrostRequest>(body);
                if (r.AdaptorPublicKey is not null)
                {
                    throw new SparkSignerPolicyException("adaptor signatures (SSP leaf swaps) are not served");
                }

                return await signer.SignLeafFrostAsync(r.LeafId, r.Message, r.VerifyingKey, r.SoCommitments, null, ct).ConfigureAwait(false);
            }

            case SparkSignerContract.Ops.BuildSendTweaks:
            {
                var r = Parse<BuildSendTweaksRequest>(body);
                RequirePinnedOperators(r.SoTargets, r.Threshold, policy);
                var own = Convert.ToHexString(await signer.GetIdentityPublicKeyAsync(ct).ConfigureAwait(false)).ToLowerInvariant();
                foreach (var leaf in r.Leaves)
                {
                    var receiver = Convert.ToHexString(leaf.ReceiverPublicKey).ToLowerInvariant();
                    if (receiver != own && !policy.AllowedReceivers.Contains(receiver))
                    {
                        throw new SparkSignerPolicyException($"receiver {receiver} is not allowed");
                    }
                }

                return await signer.BuildEncryptedSendTweaksAsync(r.Leaves, r.SoTargets, r.TransferId, r.Threshold, ct).ConfigureAwait(false);
            }

            case SparkSignerContract.Ops.BuildClaimTweaks:
            {
                var r = Parse<BuildClaimTweaksRequest>(body);
                RequirePinnedOperators(r.SoTargets, r.Threshold, policy);
                return await signer.BuildEncryptedClaimTweaksAsync(r.Leaves, r.SoTargets, r.Threshold, ct).ConfigureAwait(false);
            }

            case SparkSignerContract.Ops.BuildPreimageShares:
            {
                var r = Parse<BuildPreimageSharesRequest>(body);
                RequirePinnedOperators(r.SoTargets, r.Threshold, policy);
                return await signer.BuildEncryptedPreimageSharesAsync(r.TransferId, r.SoTargets, r.Threshold, ct).ConfigureAwait(false);
            }

            default:
                throw new SparkSignerUnknownOperationException(op);
        }
    }

    private static void RequirePinnedOperators(List<SoTarget> targets, uint threshold, SparkSignerPolicy policy)
    {
        if (policy.OperatorKeys.Count == 0)
        {
            throw new SparkSignerPolicyException("no operator set is configured");
        }

        var given = targets.Select(t => Convert.ToHexString(t.IdentityPublicKey).ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        if (targets.Count != policy.OperatorKeys.Count || !given.SetEquals(policy.OperatorKeys))
        {
            throw new SparkSignerPolicyException("operator targets do not match the pinned operator set");
        }

        if (threshold != policy.Threshold)
        {
            throw new SparkSignerPolicyException($"threshold {threshold} does not match the pinned threshold {policy.Threshold}");
        }
    }

    private static byte[] Hash32(byte[]? hash) =>
        hash is { Length: 32 } ? hash : throw new SparkSignerBadRequestException("messageHash must be 32 bytes");

    private static T Parse<T>(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(string.IsNullOrEmpty(body) ? "{}" : body, SparkSignerContract.Json)
                   ?? throw new SparkSignerBadRequestException("empty request body");
        }
        catch (JsonException e)
        {
            throw new SparkSignerBadRequestException($"malformed request: {e.Message}");
        }
    }
}
