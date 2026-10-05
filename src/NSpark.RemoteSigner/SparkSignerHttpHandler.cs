using System.Text.Json;
using NSpark.Signer;

namespace NSpark.RemoteSigner;

/// <summary>
/// Host glue for a signer service: turns a <c>POST /spark/{op}</c> request into an HTTP status and
/// a <see cref="SparkSignerEnvelope"/> body. The host only supplies seed selection and policy.
/// </summary>
public static class SparkSignerHttpHandler
{
    /// <summary>True when <paramref name="path"/> belongs to the Spark signer (and nothing else does).</summary>
    public static bool IsSparkPath(string? path) =>
        Normalize(path).StartsWith(SparkSignerContract.RoutePrefix, StringComparison.Ordinal);

    /// <summary>
    /// Handles one request. <paramref name="resolveSigner"/> maps the request's wallet id to a signer;
    /// throw <see cref="SparkSignerPolicyException"/> from it to refuse a wallet.
    /// </summary>
    public static async Task<(int Status, string Body)> HandleAsync(
        string? path,
        string? body,
        Func<string, CancellationToken, Task<ISparkSigner>> resolveSigner,
        SparkSignerPolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resolveSigner);
        ArgumentNullException.ThrowIfNull(policy);

        var normalized = Normalize(path);
        if (!normalized.StartsWith(SparkSignerContract.RoutePrefix, StringComparison.Ordinal))
        {
            return Error(404, $"not a Spark signer path: {path}");
        }

        var op = normalized[SparkSignerContract.RoutePrefix.Length..].TrimEnd('/');
        try
        {
            var wallet = SparkSignerDispatcher.ReadWallet(body ?? string.Empty);
            var signer = await resolveSigner(wallet, ct).ConfigureAwait(false);
            var result = await SparkSignerDispatcher.DispatchAsync(op, body ?? string.Empty, signer, policy, ct).ConfigureAwait(false);
            return (200, JsonSerializer.Serialize(new { result }, SparkSignerContract.Json));
        }
        catch (SparkSignerBadRequestException e)
        {
            return Error(400, e.Message);
        }
        catch (SparkSignerPolicyException e)
        {
            return Error(403, e.Message);
        }
        catch (SparkSignerUnknownOperationException e)
        {
            return Error(404, e.Message);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Error(500, $"{e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>Collapses repeated slashes so <c>//spark/info</c> and <c>/spark/info</c> match.</summary>
    internal static string Normalize(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        var collapsed = string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries));
        return "/" + collapsed + (path.EndsWith('/') && collapsed.Length > 0 ? "/" : string.Empty);
    }

    private static (int, string) Error(int status, string message) =>
        (status, JsonSerializer.Serialize(new { error = message }, SparkSignerContract.Json));
}
