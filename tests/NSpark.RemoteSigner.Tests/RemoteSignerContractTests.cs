using System.Text;
using NBitcoin;
using NSpark.RemoteSigner;
using NSpark.Signer;

namespace NSpark.RemoteSigner.Tests;

/// <summary>
/// End-to-end contract tests: <see cref="RemoteSparkSigner"/> (client) over an in-memory transport
/// into <see cref="SparkSignerHttpHandler"/> (server) backed by a real in-process
/// <see cref="SparkSigner"/>. Uses the public BIP-39 test mnemonic; never fund it.
/// </summary>
public class RemoteSignerContractTests
{
    private const string Mnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string Wallet = "testwallet";

    private static readonly byte[][] OperatorKeys = [Pub(1), Pub(2), Pub(3)];
    private static readonly byte[] Ssp = Pub(9);

    private static readonly IReadOnlyList<SoTarget> SoTargets =
    [
        new("0000000000000000000000000000000000000000000000000000000000000001", 1, OperatorKeys[0]),
        new("0000000000000000000000000000000000000000000000000000000000000002", 2, OperatorKeys[1]),
        new("0000000000000000000000000000000000000000000000000000000000000003", 3, OperatorKeys[2]),
    ];

    private SparkSigner _local = null!;
    private InMemoryTransport _transport = null!;
    private RemoteSparkSigner _remote = null!;

    [SetUp]
    public void SetUp()
    {
        _local = SparkSigner.FromMnemonic(Mnemonic, 0);
        var policy = SparkSignerPolicy.Create(OperatorKeys.Select(Hex), 2, [Hex(Ssp)]);
        _transport = new InMemoryTransport(policy, wallet =>
            wallet == Wallet ? _local : throw new SparkSignerPolicyException($"unknown wallet {wallet}"));
        _remote = new RemoteSparkSigner(_transport, Wallet);
    }

    [Test]
    public async Task Info_ReportsTheIdentityAndThePolicy()
    {
        var info = await _remote.GetInfoAsync();

        info.ContractVersion.Should().Be(SparkSignerContract.Version);
        info.IdentityPublicKey.Should().Be(Hex(await _local.GetIdentityPublicKeyAsync()));
        info.OperatorKeys.Should().BeEquivalentTo(OperatorKeys.Select(Hex));
        info.Threshold.Should().Be(2);
        info.AllowedReceivers.Should().Equal(Hex(Ssp));
    }

    [Test]
    public async Task PublicKeys_MatchTheInProcessSigner()
    {
        (await _remote.GetIdentityPublicKeyAsync()).Should().Equal(await _local.GetIdentityPublicKeyAsync());
        (await _remote.GetDepositPublicKeyAsync()).Should().Equal(await _local.GetDepositPublicKeyAsync());
        (await _remote.GetLeafPublicKeyAsync("leaf-1")).Should().Equal(await _local.GetLeafPublicKeyAsync("leaf-1"));
        (await _remote.GetStaticDepositPublicKeyAsync(2)).Should().Equal(await _local.GetStaticDepositPublicKeyAsync(2));
    }

    [Test]
    public async Task IdentitySignatures_MatchTheInProcessSigner()
    {
        var hash = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

        (await _remote.SignWithIdentityKeyAsync(hash)).Should().Equal(await _local.SignWithIdentityKeyAsync(hash));
        (await _remote.SignCompactWithIdentityKeyAsync(hash)).Should().Equal(await _local.SignCompactWithIdentityKeyAsync(hash));
    }

    [Test]
    public async Task SendTweaks_ToTheAllowedReceiver_AreBuilt()
    {
        var batch = await _remote.BuildEncryptedSendTweaksAsync([new SendTweakLeafDescriptor("leaf-1", Ssp)], SoTargets,
            "019a0000-0000-7000-8000-000000000001", 2);

        batch.EncryptedPackageBySoId.Keys.Should().BeEquivalentTo(SoTargets.Select(t => t.SoId));
    }

    [Test]
    public async Task SendTweaks_BackToTheOwnIdentity_AreBuilt()
    {
        var own = await _local.GetIdentityPublicKeyAsync();

        var batch = await _remote.BuildEncryptedSendTweaksAsync([new SendTweakLeafDescriptor("leaf-1", own)], SoTargets,
            "019a0000-0000-7000-8000-000000000002", 2);

        batch.EncryptedPackageBySoId.Should().HaveCount(3);
    }

    [Test]
    public async Task SendTweaks_ToAnyOtherReceiver_AreRefused()
    {
        var act = () => _remote.BuildEncryptedSendTweaksAsync([new SendTweakLeafDescriptor("leaf-1", Pub(42))], SoTargets,
            "019a0000-0000-7000-8000-000000000003", 2);

        (await act.Should().ThrowAsync<SparkRemoteSignerException>()).Which.Status.Should().Be(403);
    }

    [Test]
    public async Task Tweaks_ForAnotherOperatorSet_AreRefused()
    {
        var foreign = SoTargets.Take(2).Append(new SoTarget(SoTargets[2].SoId, 3, Pub(7))).ToList();

        var claim = () => _remote.BuildEncryptedClaimTweaksAsync([], foreign, 2);
        var preimage = () => _remote.BuildEncryptedPreimageSharesAsync("019a0000-0000-7000-8000-000000000004", foreign, 2);

        (await claim.Should().ThrowAsync<SparkRemoteSignerException>()).Which.Status.Should().Be(403);
        (await preimage.Should().ThrowAsync<SparkRemoteSignerException>()).Which.Status.Should().Be(403);
    }

    [Test]
    public async Task Tweaks_AtAnotherThreshold_AreRefused()
    {
        var act = () => _remote.BuildEncryptedPreimageSharesAsync("019a0000-0000-7000-8000-000000000005", SoTargets, 3);

        (await act.Should().ThrowAsync<SparkRemoteSignerException>()).Which.Status.Should().Be(403);
    }

    [Test]
    public async Task PreimageShares_ForThePinnedOperators_AreBuilt()
    {
        var bundle = await _remote.BuildEncryptedPreimageSharesAsync("019a0000-0000-7000-8000-000000000006", SoTargets, 2);

        bundle.PaymentHash.Should().HaveCount(32);
    }

    [Test]
    public async Task AdaptorSignatures_AreRefused()
    {
        var act = () => _remote.SignLeafFrostAsync("leaf-1", new byte[32], Pub(5),
            new Dictionary<string, SigningCommitment>(), adaptorPublicKey: Pub(6));

        (await act.Should().ThrowAsync<SparkRemoteSignerException>()).Which.Status.Should().Be(403);
    }

    [Test]
    public async Task UnknownWallet_IsRefused()
    {
        var other = new RemoteSparkSigner(_transport, "otherwallet");

        var act = () => other.GetIdentityPublicKeyAsync();

        (await act.Should().ThrowAsync<SparkRemoteSignerException>()).Which.Status.Should().Be(403);
    }

    [Test]
    public async Task OperationsOutsideTheContract_NeverReachTheTransport()
    {
        var calls = _transport.Requests.Count;

        await FluentActions.Awaiting(() => _remote.ExportStaticDepositPrivateKeyAsync()).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => _remote.DecryptEciesWithIdentityKeyAsync([1])).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => _remote.GenerateLeafFrostNonceAsync("leaf-1")).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => _remote.GenerateAdaptorKeyAsync()).Should().ThrowAsync<NotSupportedException>();

        _transport.Requests.Should().HaveCount(calls);
    }

    [Test]
    public async Task UnknownOperation_Is404()
    {
        var (status, body) = await SparkSignerHttpHandler.HandleAsync("/spark/export-everything", $"{{\"wallet\":\"{Wallet}\"}}",
            (_, _) => Task.FromResult<ISparkSigner>(_local), SparkSignerPolicy.Create([], 2, []));

        status.Should().Be(404);
        body.Should().Contain("\"error\"");
    }

    [TestCase("/spark/info", true)]
    [TestCase("//spark/info", true)]
    [TestCase("/spark/", true)]
    [TestCase("/", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    [TestCase("/spark", false)]
    [TestCase("/sparkle/info", false)]
    [TestCase("/sign", false)]
    public void OnlyTheSparkPrefix_IsASparkPath(string? path, bool expected) =>
        SparkSignerHttpHandler.IsSparkPath(path).Should().Be(expected);

    [Test]
    public async Task ANonEnvelopeResponse_IsReportedAsNotASparkSigner()
    {
        // A signer that predates the Spark routes answers every path as a PSBT signer.
        var legacy = new RemoteSparkSigner(new FixedTransport(500, "Object reference not set to an instance of an object."), Wallet);

        var act = () => legacy.GetInfoAsync();

        (await act.Should().ThrowAsync<SparkRemoteSignerException>()).WithMessage("*not a Spark signer envelope*");
    }

    private static byte[] Pub(byte seed) => new Key(Enumerable.Repeat(seed, 32).ToArray()).PubKey.ToBytes();

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    internal sealed class InMemoryTransport(SparkSignerPolicy policy, Func<string, ISparkSigner> resolve) : ISparkSignerTransport
    {
        public List<(string Path, string Body)> Requests { get; } = [];

        public Task<(int Status, string Body)> InvokeAsync(string path, string jsonBody, CancellationToken ct)
        {
            Requests.Add((path, jsonBody));
            return SparkSignerHttpHandler.HandleAsync(path, jsonBody, (wallet, _) => Task.FromResult(resolve(wallet)), policy, ct);
        }
    }

    private sealed class FixedTransport(int status, string body) : ISparkSignerTransport
    {
        public Task<(int Status, string Body)> InvokeAsync(string path, string jsonBody, CancellationToken ct) =>
            Task.FromResult((status, body));
    }

    internal static string Utf8(byte[] bytes) => Encoding.UTF8.GetString(bytes);
}
