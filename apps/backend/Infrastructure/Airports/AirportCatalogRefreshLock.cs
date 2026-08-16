using Npgsql;

namespace backend.Infrastructure.Airports;

public interface IAirportCatalogRefreshLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken);
}

public sealed class PostgresAirportCatalogRefreshLock(IConfiguration configuration) : IAirportCatalogRefreshLock
{
    private const long AdvisoryLockId = 4_287_113_091;
    private readonly string _connectionString = configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

    public async Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", connection);
            command.Parameters.AddWithValue(AdvisoryLockId);
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false))
                return new Lease(connection);
            await connection.DisposeAsync();
            return null;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (connection.State == System.Data.ConnectionState.Open)
            {
                try
                {
                    await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock($1)", connection);
                    command.Parameters.AddWithValue(AdvisoryLockId);
                    await command.ExecuteScalarAsync(CancellationToken.None);
                }
                catch
                {
                    // Closing the PostgreSQL session releases the advisory lock.
                }
            }
            await connection.DisposeAsync();
        }
    }
}
