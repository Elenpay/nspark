namespace NSpark.Models;

/// <summary>
/// How fast the SSP has a cooperative exit confirm on-chain. The speed sets the fee rate of the exit transaction,
/// and with it the L1 broadcast fee the SSP charges: the slower, the cheaper. A slow exit can take hours to
/// confirm, and its leaves stay the wallet's (locked) until it does.
/// </summary>
public enum ExitSpeed
{
    Fast,
    Medium,
    Slow,
}
