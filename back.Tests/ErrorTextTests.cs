using ERental.Services;

namespace ERental.Tests;

/// <summary>
/// 异常翻译的口径：用户填错（唯一键冲突、字段超长）= 400；
/// 连不上数据库/连接池耗尽/网络超时 = 500，不能伪装成「请检查填写内容」。
/// </summary>
public class ErrorTextTests
{
    /// <summary>只为命中按类型名识别的数据库异常分支，不需要真实驱动</summary>
    private sealed class MySqlException(string message) : Exception(message);

    [Theory]
    [InlineData("Unable to connect to any of the specified MySQL hosts.")]
    [InlineData("Connect Timeout expired.")]
    [InlineData("Reading from the stream has failed.")]
    [InlineData("Too many connections")]
    [InlineData("Access denied for user 'erental'@'localhost'")]
    public void ConnectionFailuresAreInfrastructureAndNotHandled(string message)
    {
        var ex = new InvalidOperationException("wrapper", new MySqlException(message));

        Assert.True(ErrorText.IsInfrastructure(ex));
        Assert.False(ErrorText.IsHandled(ex));
        Assert.Equal(ErrorText.Unavailable, ErrorText.Safe(ex));
    }

    [Fact]
    public void TimeoutAndSocketErrorsAreInfrastructure()
    {
        Assert.True(ErrorText.IsInfrastructure(new TimeoutException()));
        Assert.True(ErrorText.IsInfrastructure(new System.Net.Sockets.SocketException()));
    }

    [Fact]
    public void DataErrorsStayClientErrors()
    {
        var ex = new InvalidOperationException("wrapper", new MySqlException("Duplicate entry 'A001' for key 'uk_order_no'"));

        Assert.False(ErrorText.IsInfrastructure(ex));
        Assert.True(ErrorText.IsHandled(ex));
        Assert.Contains("已存在", ErrorText.Safe(ex), StringComparison.Ordinal);
    }

    [Fact]
    public void LockWaitTimeoutIsNotInfrastructure()
    {
        var ex = new MySqlException("Lock wait timeout exceeded; try restarting transaction");

        Assert.False(ErrorText.IsInfrastructure(ex));
        Assert.True(ErrorText.IsHandled(ex));
    }

    [Fact]
    public void UnexpectedErrorFallsBackToGeneric()
    {
        var ex = new InvalidOperationException("some internal detail");

        Assert.False(ErrorText.IsHandled(ex));
        Assert.Equal(ErrorText.Generic, ErrorText.Safe(ex));
    }
}
