using Cognition.Core.Providers;

namespace Cognition.Core.Tests.Providers;

[TestFixture]
public sealed class LiveCostGuardTests
{
    private sealed class FixedCostClient(decimal cost) : IJevClient
    {
        public int Calls { get; private set; }

        public Task<JevResponse> EvaluateAsync(JevRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new JevResponse("r", TimeSpan.Zero, new Dictionary<string, JevAnswer>(),
                new UsageInfo(0, 0, cost)));
        }
    }

    [Test]
    public async Task AccumulatesActualCost()
    {
        // RDev-04
        var guard = new LiveCostGuard(1.00m);
        var client = new CostGuardedJevClient(new FixedCostClient(0.25m), guard, JevTestData.Config, 4.0);

        await client.EvaluateAsync(JevTestData.OneOfEach(), CancellationToken.None);
        await client.EvaluateAsync(JevTestData.OneOfEach(), CancellationToken.None);

        Assert.That(guard.SpentUsd, Is.EqualTo(0.50m));
    }

    [Test]
    public async Task RefusesTheCallThatWouldCrossTheCap()
    {
        var guard = new LiveCostGuard(0.30m);
        var inner = new FixedCostClient(0.25m);
        var client = new CostGuardedJevClient(inner, guard, JevTestData.Config, 4.0);
        await client.EvaluateAsync(JevTestData.OneOfEach(), CancellationToken.None);

        // A 100k-char state estimates at ~$0.0016 with the safety factor, which no longer fits.
        var big = JevTestData.OneOfEach(new string('x', 100_000));
        guard.Add(0.049m);

        Assert.ThrowsAsync<CostCapExceededException>(() => client.EvaluateAsync(big, CancellationToken.None));
        Assert.That(inner.Calls, Is.EqualTo(1));
    }

    [Test]
    public void EstimateScalesWithStateSize()
    {
        var small = JevCost.EstimateUsd(JevTestData.OneOfEach("x"), JevTestData.Config, 4.0, 1.0);
        var large = JevCost.EstimateUsd(JevTestData.OneOfEach(new string('x', 40_000)), JevTestData.Config, 4.0, 1.0);

        Assert.That(large, Is.GreaterThan(small * 50));
    }

    [Test]
    public void CapMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new LiveCostGuard(0m));
    }
}
