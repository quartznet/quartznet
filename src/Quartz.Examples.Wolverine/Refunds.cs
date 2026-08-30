using Npgsql;

namespace Quartz.Examples.Wolverine;

/// <summary>
/// The application's own table, so part 6 has something of its own to write beside the trigger and the
/// outgoing envelope.
/// </summary>
/// <remarks>
/// Created here rather than shipped as a migration because this table exists only to make one
/// transaction contain three writes. Nothing in the example reads it back.
/// </remarks>
public static class Refunds
{
    public static async Task EnsureTable(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = new(
            "create table if not exists refunds (order_id text not null, amount numeric not null)",
            connection);

        await command.ExecuteNonQueryAsync();
    }
}
