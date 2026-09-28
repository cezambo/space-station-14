using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Cognition.Core.Minds;

public sealed class MindVersionConflictException : Exception
{
    public Guid StableGuid { get; }
    public long Expected { get; }
    public long? Actual { get; }

    public MindVersionConflictException(Guid stableGuid, long expected, long? actual)
        : base($"mind {stableGuid}: expected version {expected}, store has {(actual is null ? "no mind" : actual)}")
    {
        StableGuid = stableGuid;
        Expected = expected;
        Actual = actual;
    }
}

public sealed class MindStoreException : Exception
{
    public MindStoreException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// One atomic change (RS-09). <see cref="Mind"/>.Version must equal the stored version. New memories are
/// written before old ones are deleted, in the same transaction (RMe-04). <see cref="RetagRecent"/> moves recent
/// memories added after a given id to another day (events during sleep belong to the next day);
/// <see cref="ClearCheckpoint"/> removes the consolidation checkpoint in the same transaction.
/// </summary>
public sealed record MindCommit(Mind Mind, IReadOnlyList<MemoryEntry> AddMemories, IReadOnlyList<long> DeleteMemoryIds)
{
    public MemoryRetag? RetagRecent { get; init; }
    public bool ClearCheckpoint { get; init; }

    public static MindCommit Of(Mind mind) => new(mind, [], []);
}

/// <summary>Recent memories with an id above <see cref="AfterId"/> get <see cref="ToDay"/>.</summary>
public sealed record MemoryRetag(long AfterId, int ToDay);

/// <summary>A consolidation checkpoint (RS-09): opaque to the store, valid only while the mind is at <see cref="BaseVersion"/>.</summary>
public sealed record StoredCheckpoint(long BaseVersion, string Body);

public sealed record CommitResult(Mind Mind, IReadOnlyList<MemoryEntry> Added);

public interface IMindStore
{
    Mind Create(Mind mind);
    Mind? Load(Guid stableGuid);
    IReadOnlyList<Guid> List();
    IReadOnlyList<MemoryEntry> Memories(Guid stableGuid, MemoryLevel? level = null);

    /// <summary>Adds a memory without touching the mind version, so events during sleep survive the consolidation commit (RS-09).</summary>
    MemoryEntry AppendMemory(Guid stableGuid, MemoryEntry memory);

    CommitResult Commit(MindCommit commit);

    /// <summary>Replaces the mind's consolidation checkpoint; fails if the mind no longer has <paramref name="baseVersion"/>.</summary>
    void SaveCheckpoint(Guid stableGuid, long baseVersion, string body);

    StoredCheckpoint? LoadCheckpoint(Guid stableGuid);

    void DeleteCheckpoint(Guid stableGuid);
}

/// <summary>
/// RD-04: minds keyed by <c>stableGuid</c>, independent of SS14 entity ids. The <c>version</c> column is the
/// source of truth for <see cref="Mind.Version"/>; memory ids come from the <c>memories</c> table.
/// </summary>
public sealed class SqliteMindStore : IMindStore
{
    public const int SchemaVersion = 2;

    private readonly string _connectionString;

    /// <summary>Test hook: called with <c>"inserted"</c> and <c>"deleted"</c> and a row counter inside the commit transaction.</summary>
    internal Action<string, Func<string, long>>? OnCommitStep { get; set; }

    static SqliteMindStore()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public SqliteMindStore(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
        Migrate();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        Exec(c, null, "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;");
        return c;
    }

    private void Migrate()
    {
        using var c = Open();
        var version = Convert.ToInt32(Scalar(c, null, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
        if (version > SchemaVersion)
            throw new MindStoreException($"database schema {version} is newer than this build ({SchemaVersion})");
        Exec(c, null, "PRAGMA journal_mode = WAL;");
        if (version == SchemaVersion)
            return;
        using var tx = c.BeginTransaction();
        if (version < 1)
            CreateV1(c, tx);
        if (version < 2)
        {
            Exec(c, tx, """
                CREATE TABLE checkpoints (
                    stable_guid TEXT PRIMARY KEY REFERENCES minds(stable_guid) ON DELETE CASCADE,
                    base_version INTEGER NOT NULL,
                    body TEXT NOT NULL);
                """);
        }

        Exec(c, tx, $"PRAGMA user_version = {SchemaVersion};");
        tx.Commit();
    }

    private static void CreateV1(SqliteConnection c, SqliteTransaction tx)
    {
        Exec(c, tx, """
            CREATE TABLE minds (
                stable_guid TEXT PRIMARY KEY,
                id TEXT NOT NULL UNIQUE,
                version INTEGER NOT NULL,
                body TEXT NOT NULL);
            CREATE TABLE memories (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                stable_guid TEXT NOT NULL REFERENCES minds(stable_guid) ON DELETE CASCADE,
                level TEXT NOT NULL,
                day INTEGER NOT NULL,
                body TEXT NOT NULL);
            CREATE INDEX memories_by_mind ON memories(stable_guid, level, id);
            """);
    }

    public Mind Create(Mind mind)
    {
        Check(mind);
        var stored = mind with { Version = 0 };
        using var c = Open();
        using var tx = c.BeginTransaction(deferred: false);
        try
        {
            Exec(c, tx, "INSERT INTO minds (stable_guid, id, version, body) VALUES ($g, $id, 0, $body);",
                ("$g", Key(mind.StableGuid)), ("$id", mind.Id), ("$body", MindJson.Serialize(stored)));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new MindStoreException($"mind {mind.StableGuid} / '{mind.Id}' already exists");
        }

        tx.Commit();
        return stored;
    }

    public Mind? Load(Guid stableGuid)
    {
        using var c = Open();
        using var cmd = Command(c, null, "SELECT version, body FROM minds WHERE stable_guid = $g;", ("$g", Key(stableGuid)));
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        var mind = MindJson.Deserialize<Mind>(r.GetString(1), $"mind {stableGuid}");
        return mind with { Version = r.GetInt64(0) };
    }

    public IReadOnlyList<Guid> List()
    {
        using var c = Open();
        using var cmd = Command(c, null, "SELECT stable_guid FROM minds ORDER BY id;");
        using var r = cmd.ExecuteReader();
        var list = new List<Guid>();
        while (r.Read())
        {
            list.Add(Guid.Parse(r.GetString(0), CultureInfo.InvariantCulture));
        }

        return list;
    }

    public IReadOnlyList<MemoryEntry> Memories(Guid stableGuid, MemoryLevel? level = null)
    {
        using var c = Open();
        using var cmd = level is null
            ? Command(c, null, "SELECT id, body FROM memories WHERE stable_guid = $g ORDER BY id;", ("$g", Key(stableGuid)))
            : Command(c, null, "SELECT id, body FROM memories WHERE stable_guid = $g AND level = $l ORDER BY id;",
                ("$g", Key(stableGuid)), ("$l", LevelKey(level.Value)));
        using var r = cmd.ExecuteReader();
        var list = new List<MemoryEntry>();
        while (r.Read())
        {
            var id = r.GetInt64(0);
            list.Add(MindJson.Deserialize<MemoryEntry>(r.GetString(1), $"memory {id}") with { Id = id });
        }

        return list;
    }

    public MemoryEntry AppendMemory(Guid stableGuid, MemoryEntry memory)
    {
        Check(memory);
        using var c = Open();
        using var tx = c.BeginTransaction(deferred: false);
        if (Scalar(c, tx, "SELECT 1 FROM minds WHERE stable_guid = $g;", ("$g", Key(stableGuid))) is null)
            throw new MindStoreException($"mind {stableGuid} does not exist");
        var added = Insert(c, tx, stableGuid, memory);
        tx.Commit();
        return added;
    }

    public CommitResult Commit(MindCommit commit)
    {
        var mind = commit.Mind;
        Check(mind);
        foreach (var m in commit.AddMemories)
        {
            Check(m);
        }

        using var c = Open();
        using var tx = c.BeginTransaction(deferred: false);
        var stored = Scalar(c, tx, "SELECT version FROM minds WHERE stable_guid = $g;", ("$g", Key(mind.StableGuid)));
        var actual = stored is null ? (long?)null : Convert.ToInt64(stored, CultureInfo.InvariantCulture);
        if (actual != mind.Version)
            throw new MindVersionConflictException(mind.StableGuid, mind.Version, actual);

        var added = commit.AddMemories.Select(m => Insert(c, tx, mind.StableGuid, m)).ToList();
        OnCommitStep?.Invoke("inserted", Counter(c, tx));

        foreach (var id in commit.DeleteMemoryIds.Distinct())
        {
            var n = Exec(c, tx, "DELETE FROM memories WHERE id = $id AND stable_guid = $g;", ("$id", id),
                ("$g", Key(mind.StableGuid)));
            if (n != 1)
                throw new MindStoreException($"mind {mind.StableGuid}: memory {id} does not exist");
        }

        OnCommitStep?.Invoke("deleted", Counter(c, tx));

        if (commit.RetagRecent is { } retag)
        {
            Exec(c, tx, """
                UPDATE memories SET day = $d, body = json_set(body, '$.day', $d)
                WHERE stable_guid = $g AND level = 'recent' AND id > $after;
                """, ("$d", retag.ToDay), ("$g", Key(mind.StableGuid)), ("$after", retag.AfterId));
        }

        if (commit.ClearCheckpoint)
            Exec(c, tx, "DELETE FROM checkpoints WHERE stable_guid = $g;", ("$g", Key(mind.StableGuid)));

        var next = mind with { Version = mind.Version + 1 };
        Exec(c, tx, "UPDATE minds SET version = $v, id = $id, body = $body WHERE stable_guid = $g;",
            ("$v", next.Version), ("$id", next.Id), ("$body", MindJson.Serialize(next)), ("$g", Key(mind.StableGuid)));
        tx.Commit();
        return new CommitResult(next, added);
    }

    public void SaveCheckpoint(Guid stableGuid, long baseVersion, string body)
    {
        using var c = Open();
        using var tx = c.BeginTransaction(deferred: false);
        var stored = Scalar(c, tx, "SELECT version FROM minds WHERE stable_guid = $g;", ("$g", Key(stableGuid)));
        var actual = stored is null ? (long?)null : Convert.ToInt64(stored, CultureInfo.InvariantCulture);
        if (actual != baseVersion)
            throw new MindVersionConflictException(stableGuid, baseVersion, actual);
        Exec(c, tx, """
            INSERT INTO checkpoints (stable_guid, base_version, body) VALUES ($g, $v, $body)
            ON CONFLICT(stable_guid) DO UPDATE SET base_version = excluded.base_version, body = excluded.body;
            """, ("$g", Key(stableGuid)), ("$v", baseVersion), ("$body", body));
        tx.Commit();
    }

    public StoredCheckpoint? LoadCheckpoint(Guid stableGuid)
    {
        using var c = Open();
        using var cmd = Command(c, null, "SELECT base_version, body FROM checkpoints WHERE stable_guid = $g;", ("$g", Key(stableGuid)));
        using var r = cmd.ExecuteReader();
        return r.Read() ? new StoredCheckpoint(r.GetInt64(0), r.GetString(1)) : null;
    }

    public void DeleteCheckpoint(Guid stableGuid)
    {
        using var c = Open();
        Exec(c, null, "DELETE FROM checkpoints WHERE stable_guid = $g;", ("$g", Key(stableGuid)));
    }

    private static MemoryEntry Insert(SqliteConnection c, SqliteTransaction tx, Guid stableGuid, MemoryEntry m)
    {
        var body = MindJson.Serialize(m with { Id = 0 });
        var id = Convert.ToInt64(Scalar(c, tx,
            "INSERT INTO memories (stable_guid, level, day, body) VALUES ($g, $l, $d, $body) RETURNING id;",
            ("$g", Key(stableGuid)), ("$l", LevelKey(m.Level)), ("$d", m.Day), ("$body", body)), CultureInfo.InvariantCulture);
        return m with { Id = id };
    }

    private static Func<string, long> Counter(SqliteConnection c, SqliteTransaction tx) =>
        table => Convert.ToInt64(Scalar(c, tx, $"SELECT COUNT(*) FROM {table};"), CultureInfo.InvariantCulture);

    private static void Check(Mind mind)
    {
        var errors = MindValidator.Validate(mind);
        if (errors.Count > 0)
            throw new MindFormatException($"mind '{mind.Id}' is invalid:\n  " + string.Join("\n  ", errors));
    }

    private static void Check(MemoryEntry memory)
    {
        var errors = MindValidator.Validate(memory);
        if (errors.Count > 0)
            throw new MindFormatException("memory is invalid:\n  " + string.Join("\n  ", errors));
    }

    private static string Key(Guid g) => g.ToString("D", CultureInfo.InvariantCulture);

    private static string LevelKey(MemoryLevel l) => l.ToString().ToLowerInvariant();

    private static SqliteCommand Command(SqliteConnection c, SqliteTransaction? tx, string sql,
        params (string Name, object Value)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        return cmd;
    }

    private static int Exec(SqliteConnection c, SqliteTransaction? tx, string sql, params (string, object)[] args)
    {
        using var cmd = Command(c, tx, sql, args);
        return cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection c, SqliteTransaction? tx, string sql, params (string, object)[] args)
    {
        using var cmd = Command(c, tx, sql, args);
        return cmd.ExecuteScalar();
    }
}
