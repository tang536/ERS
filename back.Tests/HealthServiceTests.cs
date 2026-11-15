using ERental.Services;

namespace ERental.Tests;

/// <summary>
/// /health 的状态汇总口径：Unhealthy 优先于 Degraded，不能出现「数据库挂了但只报降级」。
/// </summary>
public class HealthServiceTests
{
    [Fact]
    public void AllHealthyIsHealthy()
    {
        var checks = new[]
        {
            new HealthItem("database", HealthService.Healthy, "ok"),
            new HealthItem("backup", HealthService.Healthy, "ok"),
        };

        Assert.Equal(HealthService.Healthy, HealthService.CombineStatus(checks));
    }

    [Fact]
    public void AnyDegradedMakesReportDegraded()
    {
        var checks = new[]
        {
            new HealthItem("database", HealthService.Healthy, "ok"),
            new HealthItem("backup", HealthService.Degraded, "备份过期"),
        };

        Assert.Equal(HealthService.Degraded, HealthService.CombineStatus(checks));
    }

    [Fact]
    public void UnhealthyDominatesDegraded()
    {
        var checks = new[]
        {
            new HealthItem("database", HealthService.Unhealthy, "连接失败"),
            new HealthItem("backup", HealthService.Degraded, "备份过期"),
        };

        Assert.Equal(HealthService.Unhealthy, HealthService.CombineStatus(checks));
    }
}
