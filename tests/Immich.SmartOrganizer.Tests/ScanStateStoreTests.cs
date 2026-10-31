using Microsoft.Data.Sqlite;
using Immich.SmartOrganizer.State;

namespace Immich.SmartOrganizer.Tests;

[TestClass]
public sealed class ScanStateStoreTests
{
    [TestMethod]
    public void Persists_completed_asset_and_individual_action_states()
    {
        using TemporaryDirectory directory = new();
        var path = Path.Combine(directory.Path, "scan.db");

        using (ScanStateStore state = new(path))
        {
            state.BindScope("https://photos.example|user-1");
            state.BeginAsset("asset-1", "rules-a", "2026-10-31T12:00:00Z", "receipts", full: false);
            state.SetAction("asset-1", "album:Receipts", completed: true);
            state.SetAction("asset-1", "tag:Documents/Receipts", completed: false);
            state.FinishAsset("asset-1", "2026-10-31T12:00:00Z", completed: true);

            Assert.IsTrue(state.IsCompleted("asset-1", "rules-a", "2026-10-31T12:00:00Z"));
            Assert.IsFalse(state.IsCompleted("asset-1", "rules-b", "2026-10-31T12:00:00Z"));
            Assert.IsFalse(state.IsCompleted("asset-1", "rules-a", "2026-10-31T12:00:01Z"));
            Assert.IsTrue(state.IsActionCompleted("asset-1", "album:Receipts"));
            Assert.IsFalse(state.IsActionCompleted("asset-1", "tag:Documents/Receipts"));
        }

        using ScanStateStore reopened = new(path);
        Assert.IsTrue(reopened.IsCompleted("asset-1", "rules-a", "2026-10-31T12:00:00Z"));
        Assert.IsTrue(reopened.IsActionCompleted("asset-1", "album:Receipts"));
        Assert.IsFalse(reopened.IsActionCompleted("asset-1", "tag:Documents/Receipts"));
    }

    [TestMethod]
    public void Keeps_confirmed_actions_when_an_asset_finishes_with_a_failure()
    {
        using TemporaryDirectory directory = new();
        using ScanStateStore state = new(Path.Combine(directory.Path, "scan.db"));
        state.BeginAsset("asset-2", "rules-a", "marker-a", "receipts", full: false);
        state.SetAction("asset-2", "album:Receipts", completed: true);
        state.SetAction("asset-2", "tag:Documents/Receipts", completed: false);
        state.FinishAsset("asset-2", "marker-a", completed: false, "Immich did not confirm tag membership.");

        Assert.IsFalse(state.IsCompleted("asset-2", "rules-a", "marker-a"));
        Assert.IsTrue(state.IsActionCompleted("asset-2", "album:Receipts"));
        Assert.IsFalse(state.IsActionCompleted("asset-2", "tag:Documents/Receipts"));
        using SqliteConnection database = OpenDatabase(Path.Combine(directory.Path, "scan.db"));
        using SqliteCommand command = database.CreateCommand();
        command.CommandText = "SELECT completed,last_failure FROM assets WHERE id='asset-2'";
        using SqliteDataReader reader = command.ExecuteReader();
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(0L, reader.GetInt64(0));
        Assert.AreEqual("Immich did not confirm tag membership.", reader.GetString(1));
    }

    [TestMethod]
    public void Rejects_state_database_with_a_newer_schema_version()
    {
        using TemporaryDirectory directory = new();
        var path = Path.Combine(directory.Path, "newer.db");
        using (SqliteConnection database = OpenDatabase(path))
        using (SqliteCommand command = database.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version=9";
            command.ExecuteNonQuery();
        }

        var exception = Assert.ThrowsExactly<IOException>(() => new ScanStateStore(path));

        Assert.Contains("schema version 9 is unsupported", exception.Message);
        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public void Rejects_a_different_server_or_user_scope()
    {
        using TemporaryDirectory directory = new();
        var path = Path.Combine(directory.Path, "scope.db");
        using (ScanStateStore first = new(path))
        {
            first.BindScope("https://photos.example|user-1");
        }

        using ScanStateStore second = new(path);

        var exception = Assert.ThrowsExactly<IOException>(
            () => second.BindScope("https://other.example|user-2"));

        Assert.Contains("different Immich server or user", exception.Message);
        Assert.IsFalse(second.IsCompleted("asset-1", "rules-a", "marker-a"));
    }

    [TestMethod]
    public void Exclusive_lock_rejects_an_overlapping_scan_and_releases_on_dispose()
    {
        using TemporaryDirectory directory = new();
        var path = Path.Combine(directory.Path, "locked.db");
        using ScanStateStore first = new(path);

        var exception = Assert.ThrowsExactly<IOException>(() => new ScanStateStore(path));

        first.BindScope("https://photos.example|user-1");
        first.BeginAsset("asset-3", "rules-a", "marker-a", "receipts", full: false);
        first.FinishAsset("asset-3", "marker-a", completed: true);
        first.Dispose();

        using ScanStateStore reopened = new(path);
        Assert.IsTrue(reopened.IsCompleted("asset-3", "rules-a", "marker-a"));
    }

    [TestMethod]
    public void Full_reevaluation_clears_previous_action_confirmation()
    {
        using TemporaryDirectory directory = new();
        using ScanStateStore state = new(Path.Combine(directory.Path, "full.db"));
        state.BeginAsset("asset-4", "rules-a", "marker-a", "receipts", full: false);
        state.SetAction("asset-4", "album:Receipts", completed: true);
        state.FinishAsset("asset-4", "marker-a", completed: true);

        state.BeginAsset("asset-4", "rules-a", "marker-a", "receipts", full: true);

        Assert.IsFalse(state.IsCompleted("asset-4", "rules-a", "marker-a"));
        Assert.IsFalse(state.IsActionCompleted("asset-4", "album:Receipts"));
    }

    private static SqliteConnection OpenDatabase(string path)
    {
        SqliteConnection database = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        database.Open();
        return database;
    }
}
