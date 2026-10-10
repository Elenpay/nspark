namespace NSpark.Exceptions;

/// <summary>
/// A Spark-to-Spark transfer failed (FROST round, leaf selection, finalization).
/// Some sub-conditions may be retryable; consult <see cref="SparkException.IsRetryable"/>
/// and the inner exception.
/// </summary>
public sealed class SparkTransferException : SparkException
{
    /// <summary>Identifier of the transfer that failed, when known.</summary>
    public string? TransferId { get; init; }

    /// <inheritdoc />
    public SparkTransferException(string operation, string message)
        : base(operation, message)
    {
    }

    /// <inheritdoc />
    public SparkTransferException(string operation, string message, Exception innerException)
        : base(operation, message, innerException)
    {
    }
}

/// <summary>An on-chain deposit operation (address generation, claim) failed.</summary>
public sealed class SparkDepositException : SparkException
{
    /// <summary>Deposit address or transaction id, when known.</summary>
    public string? Reference { get; init; }

    /// <inheritdoc />
    public SparkDepositException(string operation, string message)
        : base(operation, message)
    {
    }

    /// <inheritdoc />
    public SparkDepositException(string operation, string message, Exception innerException)
        : base(operation, message, innerException)
    {
    }
}

/// <summary>An on-chain withdrawal failed before broadcast or settlement.</summary>
public sealed class SparkWithdrawalException : SparkException
{
    /// <inheritdoc />
    public SparkWithdrawalException(string operation, string message)
        : base(operation, message)
    {
    }

    /// <inheritdoc />
    public SparkWithdrawalException(string operation, string message, Exception innerException)
        : base(operation, message, innerException)
    {
    }
}

/// <summary>
/// Leaves asked for by id are not owned or not spendable (spent, locked, frozen, or due a renewal
/// that failed; a renewal itself keeps the leaf's id). Nothing was moved.
/// </summary>
public sealed class SparkLeavesNotSpendableException : SparkException
{
    /// <summary>The requested leaves that are not owned or not spendable.</summary>
    public IReadOnlyList<string> LeafIds { get; }

    /// <summary>Creates the exception for <paramref name="leafIds"/>.</summary>
    /// <param name="operation">The operation that asked for the leaves.</param>
    /// <param name="leafIds">The requested leaves that are not owned or not spendable.</param>
    public SparkLeavesNotSpendableException(string operation, IReadOnlyList<string> leafIds)
        : base(operation, $"{leafIds.Count} requested leaves are not owned or not spendable: {string.Join(", ", leafIds)}.")
    {
        LeafIds = leafIds;
    }
}
