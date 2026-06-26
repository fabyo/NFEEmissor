using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Nfe.Api.Services;

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public RedisHealthCheck(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_redis.IsConnected)
        {
            return HealthCheckResult.Unhealthy("Redis desconectado.");
        }

        try
        {
            var database = _redis.GetDatabase();
            await database.PingAsync();
            return HealthCheckResult.Healthy("Redis conectado.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Falha ao consultar Redis.", ex);
        }
    }
}
