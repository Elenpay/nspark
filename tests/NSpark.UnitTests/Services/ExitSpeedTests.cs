using System.Text.Json;
using NSpark.Exceptions;
using NSpark.GraphQL;
using NSpark.Models;
using NSpark.Services;

namespace NSpark.UnitTests.Services;

/// <summary>
/// The exit speed of a cooperative exit: the fee quoted for it (the SSP's <c>coop_exit_fee_estimates</c> has one
/// estimate per speed) and the speed asked of <c>request_coop_exit</c>.
/// </summary>
[TestFixture]
public class ExitSpeedTests
{
    // The SSP's estimates, as open-ssp returns them for 2-, 6- and 12-block fee targets
    private const string Estimates = """
        {
          "coop_exit_fee_estimates": {
            "speed_fast": { "user_fee": { "original_value": 750, "original_unit": "SATOSHI" },
                            "l1_broadcast_fee": { "original_value": 2500, "original_unit": "SATOSHI" } },
            "speed_medium": { "user_fee": { "original_value": 750, "original_unit": "SATOSHI" },
                              "l1_broadcast_fee": { "original_value": 1000000, "original_unit": "MILLISATOSHI" } },
            "speed_slow": { "user_fee": { "original_value": 750, "original_unit": "SATOSHI" },
                            "l1_broadcast_fee": { "original_value": 250, "original_unit": "SATOSHI" } }
          }
        }
        """;

    private static CoopExitFeeEstimateData? Parse(string json) =>
        JsonSerializer.Deserialize<CoopExitFeeEstimateResponse>(json)!.CoopExitFeeEstimates;

    [TestCase(ExitSpeed.Fast, 3_250L)]
    [TestCase(ExitSpeed.Medium, 1_750L)]
    [TestCase(ExitSpeed.Slow, 1_000L)]
    public void The_quote_is_the_chosen_speeds_user_fee_plus_its_broadcast_fee(ExitSpeed speed, long feeSats)
    {
        WithdrawalService.FeeSatsAt(Parse(Estimates), speed).Should().Be(feeSats);
    }

    [Test]
    public void A_speed_the_SSP_has_no_estimate_for_is_refused()
    {
        var onlyFast = """
            { "coop_exit_fee_estimates": { "speed_fast": { "user_fee": { "original_value": 750, "original_unit": "SATOSHI" },
                                                           "l1_broadcast_fee": { "original_value": 2500, "original_unit": "SATOSHI" } } } }
            """;

        var act = () => WithdrawalService.FeeSatsAt(Parse(onlyFast), ExitSpeed.Slow);

        act.Should().Throw<SparkUntrustedResponseException>().WithMessage("*no slow exit estimate*");
        WithdrawalService.FeeSatsAt(Parse(onlyFast), ExitSpeed.Fast).Should().Be(3_250);
    }

    [TestCase(ExitSpeed.Fast, "FAST")]
    [TestCase(ExitSpeed.Medium, "MEDIUM")]
    [TestCase(ExitSpeed.Slow, "SLOW")]
    public void The_speed_is_asked_of_the_SSP_in_its_own_spelling(ExitSpeed speed, string graphQl)
    {
        WithdrawalService.GraphQLName(speed).Should().Be(graphQl);
    }
}
