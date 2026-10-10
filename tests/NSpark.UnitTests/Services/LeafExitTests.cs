using System.Text.Json;
using Google.Protobuf;
using NSpark.Exceptions;
using NSpark.GraphQL;
using NSpark.Proto;
using NSpark.Services;
using NSpark.UnitTests.TestSupport;

namespace NSpark.UnitTests.Services;

/// <summary>
/// Exiting what one inbound transfer brought in: the transfer's leaves, the receive request that
/// names the transfer, and the leaf exit's refusals before anything moves.
/// </summary>
[TestFixture]
public sealed class LeafExitTests
{
    private const string Address = "bcrt1qcg6gkhg76snuvxu6l795yw3fx8dg8088r7zq82u60qauh7tr2kjquxfd2m";

    [Test]
    public void A_transfer_reports_the_leaves_it_moves()
    {
        var transfer = new Transfer { Id = "transfer-1", TotalValue = 300, Status = TransferStatus.Completed };
        transfer.Leaves.Add(new TransferLeaf { Leaf = Node("leaf-a", 256, 2000) });
        transfer.Leaves.Add(new TransferLeaf { Leaf = Node("leaf-b", 44, 2000) });
        transfer.Leaves.Add(new TransferLeaf());

        var model = TransferMapping.ToModel(transfer);

        model.Leaves.Select(l => (l.Id, l.ValueSats)).Should().Equal(("leaf-a", 256L), ("leaf-b", 44L));
        model.Leaves.Sum(l => l.ValueSats).Should().Be(model.TotalValueSats);
    }

    [Test]
    public void A_receive_request_names_the_transfer_that_paid_it()
    {
        var paid = Parse("""
            {"user_request": {"__typename": "LightningReceiveRequest", "lightning_receive_request_id": "req-1",
              "lightning_receive_request_status": "TRANSFER_COMPLETED",
              "lightning_receive_request_transfer": {"transfer_spark_id": "transfer-1",
                "transfer_total_amount": {"original_value": 300000, "original_unit": "MILLISATOSHI"}}}}
            """);
        var unpaid = Parse("""
            {"user_request": {"__typename": "LightningReceiveRequest", "lightning_receive_request_id": "req-2",
              "lightning_receive_request_status": "INVOICE_CREATED", "lightning_receive_request_transfer": null}}
            """);
        var send = Parse("""{"user_request": {"__typename": "LightningSendRequest"}}""");

        LightningService.ToLightningReceiveRequest(paid.UserRequest, "req-1").Should()
            .Be(new NSpark.Models.LightningReceiveRequest("req-1", "TRANSFER_COMPLETED", "transfer-1", 300));
        LightningService.ToLightningReceiveRequest(unpaid.UserRequest, "req-2").Should()
            .Be(new NSpark.Models.LightningReceiveRequest("req-2", "INVOICE_CREATED", null, null));
        LightningService.ToLightningReceiveRequest(send.UserRequest, "req-3").Should().BeNull();
        LightningService.ToLightningReceiveRequest(null, "req-4").Should().BeNull();
    }

    [Test]
    [CancelAfter(60_000)]
    public async Task A_leaf_exit_refuses_leaves_that_are_not_spendable_before_anything_moves(CancellationToken ct)
    {
        var state = FakeOperatorState.Accepting();
        state.SetNodes([Node("spendable", 256, 2000), Node("frozen", 44, 50)]);

        await FakeOperator.RunAsync(state, async wallet =>
        {
            var exit = () => wallet.WithdrawLeavesAsync(["spendable", "frozen", "gone"], Address, ct: ct);

            var refusal = (await exit.Should().ThrowAsync<SparkLeavesNotSpendableException>()).Which;
            refusal.LeafIds.Should().Equal("frozen", "gone");
        });
    }

    [Test]
    [CancelAfter(60_000)]
    public async Task A_leaf_exit_needs_distinct_leaves_and_an_address_on_the_wallets_network(CancellationToken ct)
    {
        var state = FakeOperatorState.Accepting();

        await FakeOperator.RunAsync(state, async wallet =>
        {
            await wallet.Invoking(w => w.WithdrawLeavesAsync([], Address, ct: ct)).Should().ThrowAsync<ArgumentException>();
            await wallet.Invoking(w => w.WithdrawLeavesAsync(["a", "a"], Address, ct: ct)).Should().ThrowAsync<ArgumentException>();
            await wallet.Invoking(w => w.WithdrawLeavesAsync(["a"], "bc1qar0srrr7xfkvy5l643lydnw9re59gtzzwf5mdq", ct: ct))
                .Should().ThrowAsync<Exception>();
        });

        state.Methods.Should().NotContain("query_nodes");
    }

    private static GetLightningReceiveRequestResponse Parse(string json) =>
        JsonSerializer.Deserialize<GetLightningReceiveRequestResponse>(json)!;

    private static TreeNode Node(string id, ulong value, uint refundSequence) => new()
    {
        Id = id,
        TreeId = "tree",
        Value = value,
        Status = "AVAILABLE",
        NodeTx = ByteString.CopyFrom(MakeRawTx(0)),
        RefundTx = ByteString.CopyFrom(MakeRawTx(refundSequence)),
    };

    /// <summary>Minimal single-input legacy tx: version, one input with an empty script and the given nSequence.</summary>
    private static byte[] MakeRawTx(uint sequence)
    {
        var tx = new byte[4 + 1 + 36 + 1 + 4 + 1 + 4];
        tx[0] = 0x02;
        tx[4] = 0x01;
        BitConverter.GetBytes(sequence).CopyTo(tx, 42);
        return tx;
    }
}
