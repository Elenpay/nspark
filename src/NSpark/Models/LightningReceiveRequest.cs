namespace NSpark.Models;

/// <summary>
/// The SSP's view of a Lightning receive request (<c>CreateLightningInvoiceAsync</c>), including the
/// Spark transfer that paid it once the SSP has sent it.
/// </summary>
/// <param name="Id">The SSP's request id (<see cref="LightningInvoice.RequestId"/>).</param>
/// <param name="Status">The request's status, e.g. <c>"TRANSFER_COMPLETED"</c>.</param>
/// <param name="TransferId">
/// The Spark transfer that paid the invoice, once the SSP has sent it. It is the id of a claimed
/// <see cref="SparkTransfer"/>, whose <see cref="SparkTransfer.Leaves"/> are what the payment brought in.
/// </param>
/// <param name="TransferAmountSats">The transfer's total, as the SSP reports it.</param>
public sealed record LightningReceiveRequest(
    string Id,
    string? Status,
    string? TransferId,
    long? TransferAmountSats);
