using FleetCare.Domain.Entities;
using FleetCare.Domain.Enums;

namespace FleetCare.Domain.Tests;

public sealed class RiskAlertTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static RiskAlert Create(double score = 0.5) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "EngineTemperatureTrend", score, "Устойчивый рост температуры", Now);

    [Fact]
    public void Review_ChangesStatusAndTimestamp()
    {
        var alert = Create();
        Assert.Equal(RiskAlertStatus.New, alert.Status);
        alert.Review(Now.AddMinutes(5));
        Assert.Equal(RiskAlertStatus.Reviewed, alert.Status);
        Assert.Equal(Now.AddMinutes(5), alert.ReviewedAt);
    }

    [Fact]
    public void ReviewTwice_Throws()
    {
        var alert = Create();
        alert.Review(Now.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => alert.Review(Now.AddMinutes(2)));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void InvalidScore_Throws(double score) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(score));
}
