namespace NSpark.Models;

/// <summary>
/// A Spark-to-Spark transfer record returned by the Signing Operators.
/// </summary>
/// <param name="Id">Unique transfer identifier assigned by the SO coordinator.</param>
/// <param name="SenderIdentityPublicKey">Sender wallet's identity public key (hex).</param>
/// <param name="ReceiverIdentityPublicKey">Receiver wallet's identity public key (hex).</param>
/// <param name="TotalValueSats">Total transfer value in satoshis.</param>
/// <param name="Status">Server-reported status string (e.g. <c>"SENDER_KEY_TWEAKED"</c>, <c>"CLAIMED"</c>).</param>
/// <param name="CreatedAt">Timestamp when the transfer was created.</param>
/// <param name="Type">Optional transfer type tag (e.g. <c>"LIGHTNING"</c>, <c>"PREIMAGE_SWAP"</c>).</param>
public sealed record SparkTransfer(
    string Id,
    string SenderIdentityPublicKey,
    string ReceiverIdentityPublicKey,
    long TotalValueSats,
    string Status,
    DateTimeOffset CreatedAt,
    string? Type = null)
{
    /// <summary>The Spark invoice this transfer pays, if any.</summary>
    public string? SparkInvoice { get; init; }

    /// <summary>
    /// The leaves the transfer moves, as the operators reported them. A claimed inbound transfer's
    /// leaves become this wallet's: exiting exactly them (<c>WithdrawLeavesAsync</c>) moves exactly
    /// what the transfer brought in, without a leaf swap.
    /// </summary>
    public IReadOnlyList<SparkLeaf> Leaves { get; init; } = [];
}
