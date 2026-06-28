using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OrderAlert.Core.Models;
using OrderAlert.Core.Services;

namespace OrderAlert.Core.Persistence;

public sealed class SqliteStore : IScanStore
{
    private readonly string _connectionString;

    public SqliteStore(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS accounts (
                id TEXT PRIMARY KEY,
                platform INTEGER NOT NULL,
                display_name TEXT NOT NULL,
                account_identifier TEXT NOT NULL,
                profile_path TEXT NOT NULL,
                is_enabled INTEGER NOT NULL,
                last_successful_scan_at TEXT NULL,
                last_error TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS orders (
                platform INTEGER NOT NULL,
                account_id TEXT NOT NULL,
                order_id TEXT NOT NULL,
                source_url TEXT NOT NULL,
                raw_status TEXT NOT NULL,
                assessment_at TEXT NULL,
                shipping_seconds INTEGER NULL,
                last_seen_at TEXT NOT NULL,
                is_active INTEGER NOT NULL,
                PRIMARY KEY (platform, account_id, order_id),
                FOREIGN KEY (account_id) REFERENCES accounts(id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS scan_batches (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_id TEXT NOT NULL,
                started_at TEXT NOT NULL,
                finished_at TEXT NOT NULL,
                complete INTEGER NOT NULL,
                page_count INTEGER NOT NULL,
                order_count INTEGER NOT NULL,
                error TEXT NULL,
                FOREIGN KEY (account_id) REFERENCES accounts(id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS alert_states (
                order_key TEXT PRIMARY KEY,
                risk INTEGER NOT NULL,
                first_alerted_at TEXT NULL,
                last_alerted_at TEXT NULL,
                next_eligible_at TEXT NULL,
                snoozed_until TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAccountAsync(
        StoreAccount account,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO accounts (
                id, platform, display_name, account_identifier, profile_path,
                is_enabled, last_successful_scan_at, last_error
            ) VALUES (
                $id, $platform, $displayName, $accountIdentifier, $profilePath,
                $isEnabled, $lastSuccessfulScanAt, $lastError
            )
            ON CONFLICT(id) DO UPDATE SET
                platform = excluded.platform,
                display_name = excluded.display_name,
                account_identifier = excluded.account_identifier,
                profile_path = excluded.profile_path,
                is_enabled = excluded.is_enabled,
                last_successful_scan_at = excluded.last_successful_scan_at,
                last_error = excluded.last_error;
            """;
        AddAccountParameters(command, account);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoreAccount>> ListAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        var accounts = new List<StoreAccount>();
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, platform, display_name, account_identifier, profile_path,
                   is_enabled, last_successful_scan_at, last_error
            FROM accounts
            ORDER BY display_name;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add(new StoreAccount(
                Guid.Parse(reader.GetString(0)),
                (PlatformKind)reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5) != 0,
                ParseNullableDate(reader, 6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return accounts;
    }

    public async Task DeleteAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM accounts WHERE id = $id;";
        command.Parameters.AddWithValue("$id", accountId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CommitBatchAsync(
        ScanBatch batch,
        CancellationToken cancellationToken = default)
    {
        if (!batch.Complete)
            throw new InvalidOperationException("Only complete scan batches may replace a snapshot.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var scan = connection.CreateCommand();
            scan.Transaction = transaction;
            scan.CommandText =
                """
                INSERT INTO scan_batches (
                    account_id, started_at, finished_at, complete,
                    page_count, order_count, error
                ) VALUES (
                    $accountId, $startedAt, $finishedAt, 1,
                    $pageCount, $orderCount, NULL
                );
                """;
            scan.Parameters.AddWithValue("$accountId", batch.AccountId.ToString());
            scan.Parameters.AddWithValue("$startedAt", Format(batch.StartedAt));
            scan.Parameters.AddWithValue("$finishedAt", Format(batch.FinishedAt));
            scan.Parameters.AddWithValue("$pageCount", batch.PageCount);
            scan.Parameters.AddWithValue("$orderCount", batch.Orders.Count);
            await scan.ExecuteNonQueryAsync(cancellationToken);

            var deactivate = connection.CreateCommand();
            deactivate.Transaction = transaction;
            deactivate.CommandText =
                "UPDATE orders SET is_active = 0 WHERE account_id = $accountId;";
            deactivate.Parameters.AddWithValue("$accountId", batch.AccountId.ToString());
            await deactivate.ExecuteNonQueryAsync(cancellationToken);

            foreach (var order in batch.Orders)
            {
                if (order.AccountId != batch.AccountId)
                    throw new InvalidOperationException("Batch contains an order from another account.");
                await UpsertOrderAsync(connection, transaction, order, cancellationToken);
            }

            var account = connection.CreateCommand();
            account.Transaction = transaction;
            account.CommandText =
                """
                UPDATE accounts
                SET last_successful_scan_at = $finishedAt, last_error = NULL
                WHERE id = $accountId;
                """;
            account.Parameters.AddWithValue("$finishedAt", Format(batch.FinishedAt));
            account.Parameters.AddWithValue("$accountId", batch.AccountId.ToString());
            await account.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task RecordFailedBatchAsync(
        Guid accountId,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        string error,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);
        var scan = connection.CreateCommand();
        scan.Transaction = transaction;
        scan.CommandText =
            """
            INSERT INTO scan_batches (
                account_id, started_at, finished_at, complete,
                page_count, order_count, error
            ) VALUES ($accountId, $startedAt, $finishedAt, 0, 0, 0, $error);
            UPDATE accounts SET last_error = $error WHERE id = $accountId;
            """;
        scan.Parameters.AddWithValue("$accountId", accountId.ToString());
        scan.Parameters.AddWithValue("$startedAt", Format(startedAt));
        scan.Parameters.AddWithValue("$finishedAt", Format(finishedAt));
        scan.Parameters.AddWithValue("$error", error);
        await scan.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrderSnapshot>> GetActiveOrdersAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var orders = new List<OrderSnapshot>();
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT platform, account_id, order_id, source_url, raw_status,
                   assessment_at, shipping_seconds, last_seen_at, is_active
            FROM orders
            WHERE account_id = $accountId AND is_active = 1
            ORDER BY order_id;
            """;
        command.Parameters.AddWithValue("$accountId", accountId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            orders.Add(new OrderSnapshot(
                (PlatformKind)reader.GetInt32(0),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                ParseNullableDate(reader, 5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                ParseDate(reader.GetString(7)),
                reader.GetInt32(8) != 0));
        }
        return orders;
    }

    public async Task SaveSettingsAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO settings (key, value) VALUES ('app', $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(settings));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AppSettings> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = 'app';";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string json
            ? JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.Default
            : AppSettings.Default;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task UpsertOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OrderSnapshot order,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO orders (
                platform, account_id, order_id, source_url, raw_status,
                assessment_at, shipping_seconds, last_seen_at, is_active
            ) VALUES (
                $platform, $accountId, $orderId, $sourceUrl, $rawStatus,
                $assessmentAt, $shippingSeconds, $lastSeenAt, 1
            )
            ON CONFLICT(platform, account_id, order_id) DO UPDATE SET
                source_url = excluded.source_url,
                raw_status = excluded.raw_status,
                assessment_at = excluded.assessment_at,
                shipping_seconds = excluded.shipping_seconds,
                last_seen_at = excluded.last_seen_at,
                is_active = 1;
            """;
        command.Parameters.AddWithValue("$platform", (int)order.Platform);
        command.Parameters.AddWithValue("$accountId", order.AccountId.ToString());
        command.Parameters.AddWithValue("$orderId", order.OrderId);
        command.Parameters.AddWithValue("$sourceUrl", order.SourceUrl);
        command.Parameters.AddWithValue("$rawStatus", order.RawStatus);
        command.Parameters.AddWithValue(
            "$assessmentAt",
            order.AssessmentAt.HasValue ? Format(order.AssessmentAt.Value) : DBNull.Value);
        command.Parameters.AddWithValue(
            "$shippingSeconds",
            order.ShippingSeconds.HasValue ? order.ShippingSeconds.Value : DBNull.Value);
        command.Parameters.AddWithValue("$lastSeenAt", Format(order.LastSeenAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddAccountParameters(
        SqliteCommand command,
        StoreAccount account)
    {
        command.Parameters.AddWithValue("$id", account.Id.ToString());
        command.Parameters.AddWithValue("$platform", (int)account.Platform);
        command.Parameters.AddWithValue("$displayName", account.DisplayName);
        command.Parameters.AddWithValue("$accountIdentifier", account.AccountIdentifier);
        command.Parameters.AddWithValue("$profilePath", account.ProfilePath);
        command.Parameters.AddWithValue("$isEnabled", account.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue(
            "$lastSuccessfulScanAt",
            account.LastSuccessfulScanAt.HasValue
                ? Format(account.LastSuccessfulScanAt.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$lastError",
            account.LastError is null ? DBNull.Value : account.LastError);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static DateTimeOffset? ParseNullableDate(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : ParseDate(reader.GetString(index));
}
