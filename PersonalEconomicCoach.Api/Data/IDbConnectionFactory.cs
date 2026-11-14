using MySqlConnector;

namespace PersonalEconomicCoach.Api.Data;

public interface IDbConnectionFactory
{
    Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Opens connections to the existing spankki database. The API reads the existing
/// transactions table and reads/writes only its own application tables.
/// </summary>
public sealed class MySqlConnectionFactory(IConfiguration configuration) : IDbConnectionFactory
{
    private readonly string _connectionString = configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

    public async Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new MySqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
