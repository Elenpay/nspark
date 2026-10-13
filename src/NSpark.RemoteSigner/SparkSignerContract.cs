using System.Text.Json;
using System.Text.Json.Serialization;
using NSpark.Signer;

namespace NSpark.RemoteSigner;

/// <summary>
/// HTTP contract for an <see cref="ISparkSigner"/> served remotely: <c>POST /spark/{op}</c> with a
/// JSON body <c>{"wallet": "&lt;seed id&gt;", ...}</c>, answered with a <see cref="SparkSignerEnvelope"/>.
/// <c>byte[]</c> values travel as base64.
/// </summary>
/// <remarks>
/// Only single-round operations are part of the contract. Two-phase FROST nonces, static-deposit
/// key export, adaptor keys (SSP leaf swaps) and ECIES decryption are deliberately absent: a remote
/// signer never hands secret material, or a way to recover it, to its caller.
/// </remarks>
public static class SparkSignerContract
{
    /// <summary>Bumped on any incompatible change; reported by <see cref="Ops.Info"/>.</summary>
    public const int Version = 1;

    /// <summary>Every operation lives under this path prefix.</summary>
    public const string RoutePrefix = "/spark/";

    public static class Ops
    {
        public const string Info = "info";
        public const string IdentityPublicKey = "identity-public-key";
        public const string SignWithIdentity = "sign-with-identity";
        public const string SignCompactWithIdentity = "sign-compact-with-identity";
        public const string DepositPublicKey = "deposit-public-key";
        public const string LeafPublicKey = "leaf-public-key";
        public const string StaticDepositPublicKey = "static-deposit-public-key";
        public const string SignLeafFrost = "sign-leaf-frost";
        public const string BuildSendTweaks = "build-send-tweaks";
        public const string BuildClaimTweaks = "build-claim-tweaks";
        public const string BuildPreimageShares = "build-preimage-shares";
    }

    /// <summary>Serializer settings both sides must use.</summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The path of <paramref name="op"/>, without a leading base path.</summary>
    public static string PathOf(string op) => RoutePrefix + op;
}

public sealed record WalletRequest(string Wallet);

public sealed record HashRequest(string Wallet, byte[] MessageHash);

public sealed record LeafRequest(string Wallet, string LeafId);

public sealed record IndexRequest(string Wallet, int Index);

public sealed record SignLeafFrostRequest(
    string Wallet,
    string LeafId,
    byte[] Message,
    byte[] VerifyingKey,
    Dictionary<string, SigningCommitment> SoCommitments,
    byte[]? AdaptorPublicKey);

public sealed record BuildSendTweaksRequest(
    string Wallet,
    List<SendTweakLeafDescriptor> Leaves,
    List<SoTarget> SoTargets,
    string TransferId,
    uint Threshold);

public sealed record BuildClaimTweaksRequest(
    string Wallet,
    List<ClaimTweakLeafDescriptor> Leaves,
    List<SoTarget> SoTargets,
    uint Threshold);

public sealed record BuildPreimageSharesRequest(
    string Wallet,
    string TransferId,
    List<SoTarget> SoTargets,
    uint Threshold);

/// <summary>What a signer serves: lets the caller verify identity and policy before using it.</summary>
public sealed record SparkSignerInfo(
    int ContractVersion,
    string IdentityPublicKey,
    IReadOnlyList<string> OperatorKeys,
    uint Threshold,
    IReadOnlyList<string> AllowedReceivers);

/// <summary>Response body: exactly one of <see cref="Result"/> and <see cref="Error"/> is set.</summary>
public sealed record SparkSignerEnvelope(JsonElement? Result, string? Error);
