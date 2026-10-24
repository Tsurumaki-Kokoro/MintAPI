using MintAPI.Deployment;
using MySqlConnector;
using StackExchange.Redis;

namespace MintAPI.Tests.Unit;

public sealed class DeploymentCommandsTests
{
    [Fact]
    public void Connection_builders_preserve_special_characters_without_option_injection()
    {
        var password = "p;ass,'\\\"$ word";
        var mysql = new MySqlConnectionStringBuilder(DeploymentCommands.BuildMySqlConnection("db.test", 3307, "mintdb", "app", password, true));
        Assert.Equal(password, mysql.Password);
        Assert.Equal("app", mysql.UserID);
        Assert.Equal(MySqlSslMode.VerifyFull, mysql.SslMode);
        var cache = ConfigurationOptions.Parse(DeploymentCommands.BuildRedisConnection("cache.test", 6380, "mint-user", "password-123", true));
        Assert.True(cache.Ssl);
        Assert.Equal("mint-user", cache.User);
        Assert.Equal("password-123", cache.Password);
        Assert.Equal(1, cache.DefaultDatabase);
    }

    [Fact]
    public void Redis_connection_rejects_option_injection_in_credentials()
    {
        Assert.Throws<ArgumentException>(() => DeploymentCommands.BuildRedisConnection("cache", 6379, "", "bad,ssl=false", true));
    }

    [Fact]
    public async Task Probe_fails_with_nonzero_exit_when_server_is_unreachable()
    {
        Assert.Equal(1, await DeploymentCommands.RunAsync(["--probe", "--url", "http://127.0.0.1:1/health/ready"]));
    }
}
