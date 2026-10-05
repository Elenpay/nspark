using NSpark.RemoteSigner;
using NSpark.Signer;

namespace NSpark.RemoteSigner.Tests;

/// <summary>
/// Pins the wire format: the exact request body <see cref="RemoteSparkSigner"/> sends for each
/// operation. The signer service's tests parse the same files, so a change here is a contract
/// change on both sides (bump <see cref="SparkSignerContract.Version"/> if incompatible).
/// Regenerate with <c>NSPARK_UPDATE_FIXTURES=1 dotnet test</c> and review the diff.
/// </summary>
public class GoldenFixtureTests
{
    private const string Wallet = "ed0210c8";

    private static readonly byte[] Hash = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] Key33 = [0x02, .. Enumerable.Range(1, 32).Select(i => (byte)i)];
    private static readonly SoTarget Target = new("0000000000000000000000000000000000000000000000000000000000000001", 1, Key33);

    private static IEnumerable<TestCaseData> Calls()
    {
        yield return Case(SparkSignerContract.Ops.Info, s => s.GetInfoAsync());
        yield return Case(SparkSignerContract.Ops.IdentityPublicKey, s => s.GetIdentityPublicKeyAsync());
        yield return Case(SparkSignerContract.Ops.SignWithIdentity, s => s.SignWithIdentityKeyAsync(Hash));
        yield return Case(SparkSignerContract.Ops.SignCompactWithIdentity, s => s.SignCompactWithIdentityKeyAsync(Hash));
        yield return Case(SparkSignerContract.Ops.DepositPublicKey, s => s.GetDepositPublicKeyAsync());
        yield return Case(SparkSignerContract.Ops.LeafPublicKey, s => s.GetLeafPublicKeyAsync("leaf-1"));
        yield return Case(SparkSignerContract.Ops.StaticDepositPublicKey, s => s.GetStaticDepositPublicKeyAsync(2));
        yield return Case(SparkSignerContract.Ops.SignLeafFrost, s => s.SignLeafFrostAsync("leaf-1", Hash, Key33,
            new Dictionary<string, SigningCommitment> { [Target.SoId] = new(Key33, Key33) }));
        yield return Case(SparkSignerContract.Ops.BuildSendTweaks, s => s.BuildEncryptedSendTweaksAsync(
            [new SendTweakLeafDescriptor("leaf-1", Key33)], [Target], "019a0000-0000-7000-8000-000000000001", 2));
        yield return Case(SparkSignerContract.Ops.BuildClaimTweaks, s => s.BuildEncryptedClaimTweaksAsync(
            [new ClaimTweakLeafDescriptor("leaf-1", Hash)], [Target], 2));
        yield return Case(SparkSignerContract.Ops.BuildPreimageShares, s => s.BuildEncryptedPreimageSharesAsync(
            "019a0000-0000-7000-8000-000000000002", [Target], 2));
    }

    [TestCaseSource(nameof(Calls))]
    public async Task RequestBody_MatchesTheFixture(string op, Func<RemoteSparkSigner, Task> call)
    {
        var transport = new CapturingTransport();
        await call(new RemoteSparkSigner(transport, Wallet));

        transport.Path.Should().Be("/spark/" + op);
        var fixture = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", op + ".json");
        if (Environment.GetEnvironmentVariable("NSPARK_UPDATE_FIXTURES") == "1")
        {
            var source = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "Fixtures", op + ".json");
            await File.WriteAllTextAsync(source, transport.Body + "\n");
            Assert.Inconclusive($"fixture {op}.json rewritten");
        }

        transport.Body.Should().Be((await File.ReadAllTextAsync(fixture)).TrimEnd('\n'));
    }

    private static TestCaseData Case(string op, Func<RemoteSparkSigner, Task> call) =>
        new TestCaseData(op, call).SetName($"RequestBody_MatchesTheFixture({op})");

    /// <summary>Captures the request and answers with an envelope whose result fits any operation.</summary>
    private sealed class CapturingTransport : ISparkSignerTransport
    {
        public string Path { get; private set; } = string.Empty;

        public string Body { get; private set; } = string.Empty;

        public Task<(int Status, string Body)> InvokeAsync(string path, string jsonBody, CancellationToken ct)
        {
            Path = path;
            Body = jsonBody;
            var result = path.EndsWith("/" + SparkSignerContract.Ops.Info, StringComparison.Ordinal)
                ? """{"contractVersion":1,"identityPublicKey":"00","operatorKeys":[],"threshold":2,"allowedReceivers":[]}"""
                : path.Contains("tweaks", StringComparison.Ordinal) || path.Contains("preimage", StringComparison.Ordinal) || path.Contains("frost", StringComparison.Ordinal)
                    ? "{}"
                    : "\"AA==\"";
            return Task.FromResult((200, $"{{\"result\":{result}}}"));
        }
    }
}
