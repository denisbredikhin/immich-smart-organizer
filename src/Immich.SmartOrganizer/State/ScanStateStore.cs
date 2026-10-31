using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Immich.SmartOrganizer.State;

internal sealed class ScanStateStore : IDisposable
{
    private readonly FileStream _lock;
    private readonly SqliteConnection _connection;

    public ScanStateStore(string path)
    {
        _lock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        try
        {
            _connection.Open();
            using var version = _connection.CreateCommand();
            version.CommandText = "PRAGMA user_version";
            var schema = Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (schema is not (0 or 1))
            {
                throw new IOException($"State schema version {schema} is unsupported.");
            }

            using var transaction = _connection.BeginTransaction();
            Execute("""
                CREATE TABLE IF NOT EXISTS scope (id INTEGER PRIMARY KEY CHECK(id = 1), value TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS assets (
                    id TEXT PRIMARY KEY, fingerprint TEXT NOT NULL, marker TEXT NOT NULL,
                    classification TEXT NOT NULL, processed_at TEXT NOT NULL,
                    completed INTEGER NOT NULL, last_failure TEXT);
                CREATE TABLE IF NOT EXISTS actions (
                    asset_id TEXT NOT NULL, action TEXT NOT NULL, completed INTEGER NOT NULL,
                    PRIMARY KEY(asset_id, action));
                PRAGMA user_version = 1;
                """, transaction);
            transaction.Commit();
        }
        catch
        {
            _connection.Dispose();
            _lock.Dispose();
            throw;
        }
    }

    public void BindScope(string scope)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT value FROM scope WHERE id = 1";
        var existing = command.ExecuteScalar() as string;
        if (existing is not null && existing != scope)
        {
            throw new IOException("This state database belongs to a different Immich server or user. Use a separate state.path.");
        }

        Execute("INSERT OR IGNORE INTO scope(id,value) VALUES(1,$scope)", null, ("$scope", scope));
    }

    public bool IsCompleted(string assetId, string fingerprint, string marker)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT completed FROM assets WHERE id=$id AND fingerprint=$fingerprint AND marker=$marker";
        command.Parameters.AddWithValue("$id", assetId);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$marker", marker);
        return command.ExecuteScalar() is long value && value == 1;
    }

    public void BeginAsset(string assetId, string fingerprint, string marker, string classification, bool full)
    {
        using var transaction = _connection.BeginTransaction();
        Execute("""
            DELETE FROM actions WHERE asset_id=$id AND (
                $full=1 OR NOT EXISTS (
                    SELECT 1 FROM assets WHERE id=$id AND fingerprint=$fingerprint AND marker=$marker));
            INSERT INTO assets(id,fingerprint,marker,classification,processed_at,completed,last_failure)
            VALUES($id,$fingerprint,$marker,$classification,$time,0,NULL)
            ON CONFLICT(id) DO UPDATE SET fingerprint=$fingerprint, marker=$marker,
                classification=$classification, processed_at=$time, completed=0, last_failure=NULL;
            """, transaction, ("$id", assetId), ("$fingerprint", fingerprint), ("$marker", marker),
            ("$classification", classification), ("$full", full ? 1 : 0), ("$time", DateTimeOffset.UtcNow.ToString("O")));
        transaction.Commit();
    }

    public bool IsActionCompleted(string assetId, string action)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT completed FROM actions WHERE asset_id=$id AND action=$action";
        command.Parameters.AddWithValue("$id", assetId);
        command.Parameters.AddWithValue("$action", action);
        return command.ExecuteScalar() is long value && value == 1;
    }

    public void SetAction(string assetId, string action, bool completed) =>
        Execute("""
            INSERT INTO actions(asset_id,action,completed) VALUES($id,$action,$completed)
            ON CONFLICT(asset_id,action) DO UPDATE SET completed=$completed
            """, null, ("$id", assetId), ("$action", action), ("$completed", completed ? 1 : 0));

    public void FinishAsset(string assetId, string marker, bool completed, string? failure = null) =>
        Execute("UPDATE assets SET marker=$marker,completed=$completed,last_failure=$failure WHERE id=$id",
            null, ("$id", assetId), ("$marker", marker), ("$completed", completed ? 1 : 0),
            ("$failure", (object?)failure ?? DBNull.Value));

    private void Execute(string sql, SqliteTransaction? transaction, params (string Name, object Value)[] parameters)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Dispose();
        _lock.Dispose();
    }
}
