using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SwedishCrossword.Api;

internal sealed class LeaderboardDatabaseHealthCheck(IConfiguration configuration, IHostEnvironment environment) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var sqlConnectionString = configuration.GetConnectionString("Leaderboard");

        if (string.IsNullOrWhiteSpace(sqlConnectionString))
        {
            if (!environment.IsDevelopment())
                return HealthCheckResult.Unhealthy("ConnectionStrings:Leaderboard is required outside Development.");

            var path = configuration["Storage:LeaderboardPath"];
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(AppContext.BaseDirectory, "leaderboard");

            Directory.CreateDirectory(path);
            var dbPath = Path.Combine(path, "leaderboard.db");
            var sqliteConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString();

            return await ProbeAsync(new SqliteConnection(sqliteConnectionString), cancellationToken);
        }

        return await ProbeAsync(new SqlConnection(sqlConnectionString), cancellationToken);
    }

    private static async Task<HealthCheckResult> ProbeAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using (connection)
        {
            try
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync(cancellationToken);
                return HealthCheckResult.Healthy();
            }
            catch (SqlException ex)
            {
                return HealthCheckResult.Unhealthy("Azure SQL check failed.", ex);
            }
            catch (SqliteException ex)
            {
                return HealthCheckResult.Unhealthy("SQLite check failed.", ex);
            }
            catch (InvalidOperationException ex)
            {
                return HealthCheckResult.Unhealthy("Database connection is invalid.", ex);
            }
            catch (TimeoutException ex)
            {
                return HealthCheckResult.Unhealthy("Database check timed out.", ex);
            }
        }
    }
}
