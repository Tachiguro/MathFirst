namespace MathFirst.Core.Tests.CyberDefense;

using Microsoft.Data.Sqlite;

// Reuse the existing SQLite isolation collection: unrelated legacy ClearAllPools callers must not
// contaminate this deterministic ownership check. No assembly-wide parallelism setting changes.
[Collection("Combat modifier purchase SQLite isolation")]
public sealed class Issue123FixtureIsolationTests
{
    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FixtureCleanup_ReleasesItsOwnDatabase_WithoutClearingAnUnrelatedPool(bool suiteCleanup)
    {
        var suite = new Issue125GameMenuTests();
        var fixture = await Issue125GameMenuTests.MenuFixture.CreateAsync(suite, new());
        var disposed = false;
        var directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue123Pool_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var unrelated = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "unrelated-synthetic.db"), Pooling = true
        }.ToString());
        try
        {
            if (suiteCleanup)
            {
                await fixture.DisposeAsync();
                disposed = true;
            }
            await unrelated.OpenAsync();
            var pooledHandle = unrelated.Handle!;
            unrelated.Close();
            Assert.False(pooledHandle.IsClosed);

            if (suiteCleanup) suite.Dispose();
            else
            {
                await fixture.DisposeAsync();
                disposed = true;
            }

            Assert.False(pooledHandle.IsClosed, "This fixture must not dispose another database's idle connection.");
            if (suiteCleanup) Assert.False(Directory.Exists(fixture.Directory));
            else
            {
                // Windows exclusive file access also proves fixture seed/store handles were released.
                using var exclusive = new FileStream(fixture.Store.StoragePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Assert.True(exclusive.Length > 0);
            }
            await unrelated.OpenAsync();
            using var command = unrelated.CreateCommand();
            command.CommandText = "SELECT 123;";
            Assert.Equal(123L, await command.ExecuteScalarAsync());
        }
        finally
        {
            unrelated.Close();
            SqliteConnection.ClearPool(unrelated);
            if (!disposed) await fixture.DisposeAsync();
            suite.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }
}
