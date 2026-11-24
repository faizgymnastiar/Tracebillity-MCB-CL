using System.Text;
using Microsoft.Data.Sqlite;
using Traceability.Domain;

namespace Traceability.Infrastructure.Foundation;

internal static class Sql
{
    public static List<T> ReadList<T>(SqliteConnection connection, string commandText,
        Func<SqliteDataReader, T> map, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        Bind(command, parameters);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read()) rows.Add(map(reader));
        return rows;
    }

    public static T? ReadOne<T>(SqliteConnection connection, string commandText,
        Func<SqliteDataReader, T> map, params (string Name, object? Value)[] parameters)
        where T : class
    {
        return ReadList(connection, commandText, map, parameters).FirstOrDefault();
    }

    public static void Execute(SqliteConnection connection, string commandText,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        Bind(command, parameters);
        command.ExecuteNonQuery();
    }

    /// <summary>Jumlah baris terpengaruh — dipakai penjaga transisi "UPDATE ... WHERE status = ...".</summary>
    public static int ExecuteCount(SqliteConnection connection, string commandText,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        Bind(command, parameters);
        return command.ExecuteNonQuery();
    }

    public static long ScalarLong(SqliteConnection connection, string commandText,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        Bind(command, parameters);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? 0L : Convert.ToInt64(value);
    }

    public static string? ScalarText(SqliteConnection connection, string commandText,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        Bind(command, parameters);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private static void Bind(SqliteCommand command, (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    public static string Text(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    public static string? NullableText(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static int Int(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
    }

    public static bool Bool(SqliteDataReader reader, string column)
    {
        return Int(reader, column) != 0;
    }
}

public sealed class AccountStore
{
    private readonly TraceDb _db;

    public AccountStore(TraceDb db) => _db = db;

    public bool HasAnyAccount()
    {
        using var connection = _db.OpenConnection();
        return Sql.ScalarLong(connection, "SELECT COUNT(1) FROM accounts;") > 0;
    }

    public AccountRecord? FindByEmail(string email)
    {
        using var connection = _db.OpenConnection();
        var account = Sql.ReadOne(connection,
            "SELECT id, email, full_name, password_hash, status, must_change_password, created_at_utc, updated_at_utc " +
            "FROM accounts WHERE email = $email COLLATE NOCASE;", MapAccount,
            ("$email", email));
        return account is null ? null : WithPermissions(connection, account);
    }

    public AccountRecord? FindById(string id)
    {
        using var connection = _db.OpenConnection();
        var account = Sql.ReadOne(connection,
            "SELECT id, email, full_name, password_hash, status, must_change_password, created_at_utc, updated_at_utc " +
            "FROM accounts WHERE id = $id;", MapAccount,
            ("$id", id));
        return account is null ? null : WithPermissions(connection, account);
    }

    public List<AccountRecord> ListAccounts()
    {
        using var connection = _db.OpenConnection();
        var accounts = Sql.ReadList(connection,
            "SELECT id, email, full_name, password_hash, status, must_change_password, created_at_utc, updated_at_utc " +
            "FROM accounts ORDER BY full_name COLLATE NOCASE;", MapAccount);
        foreach (var account in accounts) WithPermissions(connection, account);
        return accounts;
    }

    public void Insert(AccountRecord account, IEnumerable<string> permissions)
    {
        using var connection = _db.OpenConnection();
        using var transaction = connection.BeginTransaction();
        Sql.Execute(connection,
            "INSERT INTO accounts (id, email, full_name, password_hash, status, must_change_password, created_at_utc, updated_at_utc) " +
            "VALUES ($id, $email, $fullName, $hash, $status, $mustChange, $createdAt, $updatedAt);",
            ("$id", account.Id), ("$email", account.Email), ("$fullName", account.FullName),
            ("$hash", account.PasswordHash), ("$status", account.Status),
            ("$mustChange", account.MustChangePassword ? 1 : 0),
            ("$createdAt", account.CreatedAtUtc), ("$updatedAt", account.UpdatedAtUtc));
        ReplacePermissions(connection, account.Id, permissions);
        transaction.Commit();
    }

    public void Update(AccountRecord account, IEnumerable<string>? permissions)
    {
        using var connection = _db.OpenConnection();
        using var transaction = connection.BeginTransaction();
        Sql.Execute(connection,
            "UPDATE accounts SET full_name = $fullName, password_hash = $hash, status = $status, " +
            "must_change_password = $mustChange, updated_at_utc = $updatedAt WHERE id = $id;",
            ("$fullName", account.FullName), ("$hash", account.PasswordHash), ("$status", account.Status),
            ("$mustChange", account.MustChangePassword ? 1 : 0),
            ("$updatedAt", account.UpdatedAtUtc), ("$id", account.Id));
        if (permissions is not null) ReplacePermissions(connection, account.Id, permissions);
        transaction.Commit();
    }

    public void InsertSession(AuthSessionRecord session)
    {
        using var connection = _db.OpenConnection();
        Sql.Execute(connection,
            "INSERT INTO auth_sessions (id, account_id, token_hash, created_at_utc, expires_at_utc, last_seen_utc, revoked_at_utc) " +
            "VALUES ($id, $accountId, $tokenHash, $createdAt, $expiresAt, $lastSeen, NULL);",
            ("$id", session.Id), ("$accountId", session.AccountId), ("$tokenHash", session.TokenHash),
            ("$createdAt", session.CreatedAtUtc), ("$expiresAt", session.ExpiresAtUtc),
            ("$lastSeen", session.LastSeenAtUtc));
    }

    public AuthSessionRecord? FindSessionByTokenHash(string tokenHash)
    {
        using var connection = _db.OpenConnection();
        return Sql.ReadOne(connection,
            "SELECT id, account_id, token_hash, created_at_utc, expires_at_utc, last_seen_utc, revoked_at_utc " +
            "FROM auth_sessions WHERE token_hash = $tokenHash;",
            (reader) => new AuthSessionRecord
            {
                Id = Sql.Text(reader, "id"),
                AccountId = Sql.Text(reader, "account_id"),
                TokenHash = Sql.Text(reader, "token_hash"),
                CreatedAtUtc = Sql.Text(reader, "created_at_utc"),
                ExpiresAtUtc = Sql.Text(reader, "expires_at_utc"),
                LastSeenAtUtc = Sql.Text(reader, "last_seen_utc"),
                RevokedAtUtc = Sql.NullableText(reader, "revoked_at_utc"),
            },
            ("$tokenHash", tokenHash));
    }

    public void TouchSession(string sessionId, string nowUtc)
    {
        using var connection = _db.OpenConnection();
        Sql.Execute(connection,
            "UPDATE auth_sessions SET last_seen_utc = $now WHERE id = $id;",
            ("$now", nowUtc), ("$id", sessionId));
    }

    public void RevokeSession(string sessionId, string nowUtc)
    {
        using var connection = _db.OpenConnection();
        Sql.Execute(connection,
            "UPDATE auth_sessions SET revoked_at_utc = $now WHERE id = $id AND revoked_at_utc IS NULL;",
            ("$now", nowUtc), ("$id", sessionId));
    }

    public void RevokeAllSessions(string accountId, string nowUtc)
    {
        using var connection = _db.OpenConnection();
        Sql.Execute(connection,
            "UPDATE auth_sessions SET revoked_at_utc = $now WHERE account_id = $id AND revoked_at_utc IS NULL;",
            ("$now", nowUtc), ("$id", accountId));
    }

    public List<AuthSessionRecord> ListSessions(string? accountId)
    {
        using var connection = _db.OpenConnection();
        var sql =
            "SELECT id, account_id, token_hash, created_at_utc, expires_at_utc, last_seen_utc, revoked_at_utc " +
            "FROM auth_sessions WHERE ($accountId IS NULL OR account_id = $accountId) " +
            "ORDER BY created_at_utc DESC LIMIT 200;";
        return Sql.ReadList(connection, sql,
            (reader) => new AuthSessionRecord
            {
                Id = Sql.Text(reader, "id"),
                AccountId = Sql.Text(reader, "account_id"),
                TokenHash = Sql.Text(reader, "token_hash"),
                CreatedAtUtc = Sql.Text(reader, "created_at_utc"),
                ExpiresAtUtc = Sql.Text(reader, "expires_at_utc"),
                LastSeenAtUtc = Sql.Text(reader, "last_seen_utc"),
                RevokedAtUtc = Sql.NullableText(reader, "revoked_at_utc"),
            },
            ("$accountId", accountId));
    }

    public string? EmailOfAccount(string accountId)
    {
        using var connection = _db.OpenConnection();
        return Sql.ScalarText(connection, "SELECT email FROM accounts WHERE id = $id;", ("$id", accountId));
    }

    private static AccountRecord MapAccount(SqliteDataReader reader) => new()
    {
        Id = Sql.Text(reader, "id"),
        Email = Sql.Text(reader, "email"),
        FullName = Sql.Text(reader, "full_name"),
        PasswordHash = Sql.Text(reader, "password_hash"),
        Status = Sql.Text(reader, "status"),
        MustChangePassword = Sql.Bool(reader, "must_change_password"),
        CreatedAtUtc = Sql.Text(reader, "created_at_utc"),
        UpdatedAtUtc = Sql.Text(reader, "updated_at_utc"),
    };

    private AccountRecord WithPermissions(SqliteConnection connection, AccountRecord account)
    {
        account.Permissions = Sql.ReadList(connection,
            "SELECT permission FROM account_permissions WHERE account_id = $id ORDER BY permission;",
            (reader) => Sql.Text(reader, "permission"),
            ("$id", account.Id));
        return account;
    }

    private static void ReplacePermissions(SqliteConnection connection, string accountId, IEnumerable<string> permissions)
    {
        Sql.Execute(connection, "DELETE FROM account_permissions WHERE account_id = $id;", ("$id", accountId));
        foreach (var permission in permissions)
        {
            Sql.Execute(connection,
                "INSERT OR IGNORE INTO account_permissions (account_id, permission) VALUES ($id, $permission);",
                ("$id", accountId), ("$permission", permission));
        }
    }
}

public sealed class MasterCatalogStore
{
    private readonly TraceDb _db;

    public MasterCatalogStore(TraceDb db) => _db = db;

    public List<FamilyRecord> Families(bool includeInactive)
    {
        using var connection = _db.OpenConnection();
        return Sql.ReadList(connection,
            "SELECT id, code, name, status FROM product_families " +
            (includeInactive ? "" : "WHERE status = 'ACTIVE' ") + "ORDER BY code;",
            (reader) => new FamilyRecord
            {
                Id = Sql.Text(reader, "id"),
                Code = Sql.Text(reader, "code"),
                Name = Sql.Text(reader, "name"),
                Status = Sql.Text(reader, "status"),
            });
    }

    public List<SkuRecord> Skus(bool includeInactive)
    {
        using var connection = _db.OpenConnection();
        return Sql.ReadList(connection,
            "SELECT id, family_id, code, name, status FROM product_skus " +
            (includeInactive ? "" : "WHERE status = 'ACTIVE' ") + "ORDER BY code;",
            (reader) => new SkuRecord
            {
                Id = Sql.Text(reader, "id"),
                FamilyId = Sql.Text(reader, "family_id"),
                Code = Sql.Text(reader, "code"),
                Name = Sql.Text(reader, "name"),
                Status = Sql.Text(reader, "status"),
            });
    }

    public List<ShiftRecord> Shifts(bool includeInactive)
    {
        using var connection = _db.OpenConnection();
        return Sql.ReadList(connection,
            "SELECT id, code, name, status FROM shifts " +
            (includeInactive ? "" : "WHERE status = 'ACTIVE' ") + "ORDER BY code;",
            (reader) => new ShiftRecord
            {
                Id = Sql.Text(reader, "id"),
                Code = Sql.Text(reader, "code"),
                Name = Sql.Text(reader, "name"),
                Status = Sql.Text(reader, "status"),
            });
    }

    public List<LineRecord> Lines(bool includeInactive)
    {
        using var connection = _db.OpenConnection();
        return Sql.ReadList(connection,
            "SELECT id, code, name, status FROM lines " +
            (includeInactive ? "" : "WHERE status = 'ACTIVE' ") + "ORDER BY code;",
            (reader) => new LineRecord
            {
                Id = Sql.Text(reader, "id"),
                Code = Sql.Text(reader, "code"),
                Name = Sql.Text(reader, "name"),
                Status = Sql.Text(reader, "status"),
            });
    }

    public List<StageRecord> Stages(string? familyId, bool includeInactive)
    {
        using var connection = _db.OpenConnection();
        var where = new StringBuilder("WHERE 1=1 ");
        if (familyId is not null) where.Append("AND s.family_id = $familyId ");
        if (!includeInactive) where.Append("AND s.status = 'ACTIVE' ");

        var stages = Sql.ReadList(connection,
            $"SELECT s.id, s.family_id, s.code, s.name, s.seq, s.status FROM stages s {where} ORDER BY s.seq, s.code;",
            (reader) => new StageRecord
            {
                Id = Sql.Text(reader, "id"),
                FamilyId = Sql.Text(reader, "family_id"),
                Code = Sql.Text(reader, "code"),
                Name = Sql.Text(reader, "name"),
                Seq = Sql.Int(reader, "seq"),
                Status = Sql.Text(reader, "status"),
            },
            ("$familyId", familyId));

        foreach (var stage in stages)
        {
            stage.Defects = Sql.ReadList(connection,
                "SELECT id, stage_id, group_code, name, status FROM defects WHERE stage_id = $stageId " +
                (!includeInactive ? "AND status = 'ACTIVE' " : "") + "ORDER BY group_code, name;",
                (reader) => new DefectRecord
                {
                    Id = Sql.Text(reader, "id"),
                    StageId = Sql.Text(reader, "stage_id"),
                    GroupCode = Sql.Text(reader, "group_code"),
                    Name = Sql.Text(reader, "name"),
                    Status = Sql.Text(reader, "status"),
                },
                ("$stageId", stage.Id));
        }

        return stages;
    }

    public SkuRecord? FindSku(string id) => Skus(true).FirstOrDefault(item => item.Id == id);

    public ShiftRecord? FindShift(string id) => Shifts(true).FirstOrDefault(item => item.Id == id);

    public LineRecord? FindLine(string id) => Lines(true).FirstOrDefault(item => item.Id == id);

    public FamilyRecord? FindFamily(string id) => Families(true).FirstOrDefault(item => item.Id == id);

    public StageRecord? FindStage(string id) => Stages(null, true).FirstOrDefault(item => item.Id == id);

    public DefectRecord? FindDefect(string id) =>
        Stages(null, true).SelectMany(stage => stage.Defects).FirstOrDefault(item => item.Id == id);

    public void Upsert(string table, string id, IDictionary<string, object?> columns)
    {
        // `table`/`columns` selalu berasal dari daftar internal service, bukan input pengguna.
        using var connection = _db.OpenConnection();
        var names = columns.Keys.ToArray();
        var setClause = string.Join(", ", names.Select(name => $"{name} = ${name}"));
        if (id.Length > 0)
        {
            var updated = Sql.ScalarLong(connection, $"SELECT COUNT(1) FROM {table} WHERE id = $id;", ("$id", id));
            if (updated > 0)
            {
                Sql.Execute(connection, $"UPDATE {table} SET {setClause} WHERE id = $id;",
                    columns.Select(pair => ($"${pair.Key}", pair.Value)).Append(("$id", id)).ToArray());
                return;
            }
        }

        var insertNames = names.Append("id").ToArray();
        var placeholders = string.Join(", ", insertNames.Select(name => $"${name}"));
        var parameters = columns
            .Select(pair => ($"${pair.Key}", pair.Value))
            .Append(("$id", id.Length > 0 ? id : NewId()))
            .ToArray();
        Sql.Execute(connection,
            $"INSERT INTO {table} ({string.Join(", ", insertNames)}) VALUES ({placeholders});",
            parameters);
    }

    public void SetStatus(string table, string id, string status)
    {
        using var connection = _db.OpenConnection();
        Sql.Execute(connection, $"UPDATE {table} SET status = $status WHERE id = $id;",
            ("$status", status), ("$id", id));
    }

    public void DeleteIfUnreferenced(string id)
    {
        using var connection = _db.OpenConnection();
        Sql.Execute(connection, "DELETE FROM defects WHERE id = $id;", ("$id", id));
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}

public sealed class ProductionStore
{
    private readonly TraceDb _db;

    public ProductionStore(TraceDb db) => _db = db;

    public ProductionSessionRecord? FindActiveForAccount(string accountId)
    {
        using var connection = _db.OpenConnection();
        var session = Sql.ReadOne(connection,
            "SELECT * FROM production_sessions WHERE account_id = $accountId AND ended_at_utc IS NULL " +
            "ORDER BY started_at_utc DESC LIMIT 1;",
            MapSession,
            ("$accountId", accountId));
        if (session is not null) session.WorkOrders = WorkOrdersFor(connection, session.Id);
        return session;
    }

    public ProductionSessionRecord? FindById(string sessionId)
    {
        using var connection = _db.OpenConnection();
        var session = Sql.ReadOne(connection,
            "SELECT * FROM production_sessions WHERE id = $id;", MapSession, ("$id", sessionId));
        if (session is not null) session.WorkOrders = WorkOrdersFor(connection, session.Id);
        return session;
    }

    public void InsertSession(ProductionSessionRecord session)
    {
        using var connection = _db.OpenConnection();
        using var transaction = connection.BeginTransaction();
        session.ContextVersion = checked((int)Sql.ScalarLong(connection,
            "SELECT COALESCE(MAX(context_version), 0) + 1 FROM production_sessions WHERE account_id = $accountId;",
            ("$accountId", session.AccountId)));
        Sql.Execute(connection,
            "UPDATE production_sessions SET ended_at_utc = $started, active_work_order_id = NULL " +
            "WHERE account_id = $accountId AND ended_at_utc IS NULL;",
            ("$started", session.StartedAtUtc), ("$accountId", session.AccountId));
        Sql.Execute(connection,
            "INSERT INTO production_sessions (id, account_id, operator_name, shift_id, line_id, sku_id, " +
            "shift_code, shift_name, line_code, line_name, sku_code, sku_name, family_code, " +
            "active_work_order_id, context_version, started_at_utc, ended_at_utc) " +
            "VALUES ($id, $accountId, $operator, $shiftId, $lineId, $skuId, $shiftCode, $shiftName, " +
            "$lineCode, $lineName, $skuCode, $skuName, $familyCode, NULL, $version, $started, NULL);",
            ("$id", session.Id), ("$accountId", session.AccountId), ("$operator", session.OperatorName),
            ("$shiftId", session.ShiftId), ("$lineId", session.LineId), ("$skuId", session.SkuId),
            ("$shiftCode", session.ShiftCode), ("$shiftName", session.ShiftName),
            ("$lineCode", session.LineCode), ("$lineName", session.LineName),
            ("$skuCode", session.SkuCode), ("$skuName", session.SkuName),
            ("$familyCode", session.FamilyCode), ("$started", session.StartedAtUtc),
            ("$version", session.ContextVersion));

        foreach (var workOrder in session.WorkOrders)
        {
            InsertWorkOrderCore(connection, session.Id, workOrder);
        }

        if (session.ActiveWorkOrderId is not null)
        {
            Sql.Execute(connection,
                "UPDATE production_sessions SET active_work_order_id = $woId WHERE id = $id;",
                ("$woId", session.ActiveWorkOrderId), ("$id", session.Id));
        }

        transaction.Commit();
    }

    public void InsertWorkOrder(string sessionId, WorkOrderRecord workOrder)
    {
        using var connection = _db.OpenConnection();
        InsertWorkOrderCore(connection, sessionId, workOrder);
    }

    public void SetActiveWorkOrder(string sessionId, string? workOrderId, int contextVersion)
    {
        using var connection = _db.OpenConnection();
        Sql.Execute(connection,
            "UPDATE production_sessions SET active_work_order_id = $woId, context_version = $version WHERE id = $id;",
            ("$woId", workOrderId), ("$version", contextVersion), ("$id", sessionId));
    }

    public void EndSession(string sessionId, string nowUtc, int contextVersion)
    {
        using var connection = _db.OpenConnection();
        Sql.Execute(connection,
            "UPDATE production_sessions SET ended_at_utc = $now, active_work_order_id = NULL, " +
            "context_version = $version WHERE id = $id;",
            ("$now", nowUtc), ("$version", contextVersion), ("$id", sessionId));
    }

    public bool WorkOrderNumberTaken(string sessionId, string number)
    {
        using var connection = _db.OpenConnection();
        return Sql.ScalarLong(connection,
            "SELECT COUNT(1) FROM work_orders WHERE session_id = $sessionId AND number = $number COLLATE NOCASE;",
            ("$sessionId", sessionId), ("$number", number)) > 0;
    }

    private static void InsertWorkOrderCore(SqliteConnection connection, string sessionId, WorkOrderRecord workOrder)
    {
        Sql.Execute(connection,
            "INSERT INTO work_orders (id, session_id, number, client, shipping_reference, target_qty, created_at_utc) " +
            "VALUES ($id, $sessionId, $number, $client, $shipping, $target, $created);",
            ("$id", workOrder.Id), ("$sessionId", sessionId), ("$number", workOrder.Number),
            ("$client", workOrder.Client), ("$shipping", workOrder.ShippingReference),
            ("$target", workOrder.TargetQty), ("$created", workOrder.CreatedAtUtc));
    }

    private static List<WorkOrderRecord> WorkOrdersFor(SqliteConnection connection, string sessionId)
    {
        return Sql.ReadList(connection,
            "SELECT id, session_id, number, client, shipping_reference, target_qty, created_at_utc " +
            "FROM work_orders WHERE session_id = $sessionId ORDER BY created_at_utc, number;",
            (reader) => new WorkOrderRecord
            {
                Id = Sql.Text(reader, "id"),
                SessionId = Sql.Text(reader, "session_id"),
                Number = Sql.Text(reader, "number"),
                Client = Sql.Text(reader, "client"),
                ShippingReference = Sql.Text(reader, "shipping_reference"),
                TargetQty = Sql.Int(reader, "target_qty"),
                CreatedAtUtc = Sql.Text(reader, "created_at_utc"),
            },
            ("$sessionId", sessionId));
    }

    private static ProductionSessionRecord MapSession(SqliteDataReader reader) => new()
    {
        Id = Sql.Text(reader, "id"),
        AccountId = Sql.Text(reader, "account_id"),
        OperatorName = Sql.Text(reader, "operator_name"),
        ShiftId = Sql.Text(reader, "shift_id"),
        LineId = Sql.Text(reader, "line_id"),
        SkuId = Sql.Text(reader, "sku_id"),
        ShiftCode = Sql.Text(reader, "shift_code"),
        ShiftName = Sql.Text(reader, "shift_name"),
        LineCode = Sql.Text(reader, "line_code"),
        LineName = Sql.Text(reader, "line_name"),
        SkuCode = Sql.Text(reader, "sku_code"),
        SkuName = Sql.Text(reader, "sku_name"),
        FamilyCode = Sql.Text(reader, "family_code"),
        ActiveWorkOrderId = Sql.NullableText(reader, "active_work_order_id"),
        ContextVersion = Sql.Int(reader, "context_version"),
        StartedAtUtc = Sql.Text(reader, "started_at_utc"),
        EndedAtUtc = Sql.NullableText(reader, "ended_at_utc"),
    };
}
