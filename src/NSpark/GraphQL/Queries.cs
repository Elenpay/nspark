namespace NSpark.GraphQL;

internal static class Queries
{
    public const string GetLightningInvoice = """
        query GetLightningInvoice($paymentHash: String!) {
            spark_lightning_invoice(payment_hash: $paymentHash) {
                payment_request
                payment_hash
                amount_sats
                expires_at
                status
            }
        }
        """;

    public const string GetUserRequest = """
        query UserRequest($request_id: ID!) {
            user_request(request_id: $request_id) {
                __typename
                ... on LightningReceiveRequest {
                    lightning_receive_request_id: id
                    lightning_receive_request_status: status
                    lightning_receive_request_receiver_identity_public_key: receiver_identity_public_key
                }
                ... on LightningSendRequest {
                    lightning_send_request_id: id
                    lightning_send_request_status: status
                    lightning_send_request_encoded_invoice: encoded_invoice
                    lightning_send_request_fee: fee {
                        original_value
                        original_unit
                    }
                    lightning_send_request_idempotency_key: idempotency_key
                    lightning_send_request_payment_preimage: payment_preimage
                }
            }
        }
        """;

    /// <summary>A Lightning receive request with the transfer that paid it, once the SSP has sent it.</summary>
    public const string GetLightningReceiveRequest = """
        query UserRequest($request_id: ID!) {
            user_request(request_id: $request_id) {
                __typename
                ... on LightningReceiveRequest {
                    lightning_receive_request_id: id
                    lightning_receive_request_status: status
                    lightning_receive_request_transfer: transfer {
                        transfer_spark_id: spark_id
                        transfer_total_amount: total_amount {
                            original_value
                            original_unit
                        }
                    }
                }
            }
        }
        """;

    public const string LightningSendFeeEstimate = """
        query LightningSendFeeEstimate($encoded_invoice: String!, $amount_sats: Long) {
            lightning_send_fee_estimate(input: {
                encoded_invoice: $encoded_invoice,
                amount_sats: $amount_sats
            }) {
                fee_estimate { original_value original_unit }
            }
        }
        """;
}
