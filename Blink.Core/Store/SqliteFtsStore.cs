using System.Text;
using Blink.Core.Model;
using Blink.Core.Tokenization;
using Microsoft.Data.Sqlite;

namespace Blink.Core.Store;

/// <summary>
/// SQLite FTS5-backed <see cref="IIndexStore"/>.
///
/// Storage model: a PLAIN FTS5 table <c>documents_fts(tokens)</c> (NOT contentless,
/// NOT external-content) holds the space-joined n-gram string; the original rows live
/// in <c>documents</c>, linked by a shared integer <c>rowid</c>. Both rows are written
/// in one transaction at the same rowid. <c>bm25(documents_fts)</c> ranks on the tokens
/// column (lower = better); match-line extraction re-reads <c>documents.content</c>.
///
/// Connection model: TWO long-lived connections under WAL. <c>_conn</c> (read-write)
/// carries all writes plus <c>IterDocsUnder</c> (the indexer's own read), serialized by
/// <c>_gate</c>; <c>_readConn</c> (read-only) carries the query surface — Search,
/// GetContent, Count, FolderStats, GetBundleSizes — serialized by <c>_readGate</c>.
/// A SqliteConnection is not safe for concurrent use, hence one gate per connection;
/// but WAL readers never block on writers, so a search proceeds even while a long
/// UpsertMany transaction holds <c>_gate</c> open (no UI freeze during indexing).
/// </summary>
public sealed class SqliteFtsStore : IIndexStore, IContentStore
{
    private readonly SqliteConnection _conn;     // read-write: all mutations + indexer reads
    private readonly SqliteConnection _readConn; // read-only: query surface (WAL multi-reader)

    // One gate per connection. SqliteConnection is not thread-safe, and the app drives
    // writes (indexer) and reads (search) from different threads.
    private readonly object _gate = new();
    private readonly object _readGate = new();

    static SqliteFtsStore()
    {
        // Register the bundled e_sqlite3 native provider (FTS5 compiled in).
        SQLitePCL.Batteries_V2.Init();
    }

    public SqliteFtsStore(string dbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        _conn.Open();

        SelfTestFts5();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA synchronous=NORMAL;");
        InitSchema();

        // Opened AFTER InitSchema: tables and the WAL files must exist before a
        // ReadOnly open is safe.
        _readConn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        _readConn.Open();
        using (var cmd = _readConn.CreateCommand())
        {
            // Brief retry instead of an instant SQLITE_BUSY if a read lands on the
            // exact moment of a WAL checkpoint.
            cmd.CommandText = "PRAGMA busy_timeout=250;";
            cmd.ExecuteNonQuery();
        }
    }

    private void SelfTestFts5()
    {
        try
        {
            Exec("CREATE VIRTUAL TABLE temp.__fts5_probe USING fts5(x);");
            Exec("DROP TABLE temp.__fts5_probe;");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "SQLite FTS5 is not available in this build. Ensure SQLitePCLRaw.bundle_e_sqlite3 is referenced.", ex);
        }
    }

    // Current on-disk schema version. Bump when the schema changes and add a migration step.
    //   v1: documents(doc_id, path, mtime, size, content)
    //   v2: + is_bundle, member_count  (virtual bundle entries for collapsed file groups)
    private const long SchemaVersion = 2;

    private void InitSchema()
    {
        // Full CURRENT schema for fresh databases. Existing databases are brought up to
        // date by Migrate() below (CREATE IF NOT EXISTS is a no-op on an old table, so new
        // columns are added there, not here).
        Exec(@"
            CREATE TABLE IF NOT EXISTS schema_meta(version INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS documents(
                rowid        INTEGER PRIMARY KEY,
                doc_id       TEXT UNIQUE NOT NULL,
                path         TEXT NOT NULL,
                mtime        REAL NOT NULL,
                size         INTEGER NOT NULL,
                content      TEXT NOT NULL,
                is_bundle    INTEGER NOT NULL DEFAULT 0,
                member_count INTEGER NOT NULL DEFAULT 0
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS documents_fts USING fts5(tokens, tokenize='unicode61');
        ");

        using var check = _conn.CreateCommand();
        check.CommandText = "SELECT version FROM schema_meta LIMIT 1;";
        var existing = check.ExecuteScalar();

        if (existing is null or DBNull)
            Exec($"INSERT INTO schema_meta(version) VALUES({SchemaVersion});"); // fresh db
        else
            Migrate(Convert.ToInt64(existing));
    }

    /// <summary>Bring an older database up to <see cref="SchemaVersion"/>.</summary>
    private void Migrate(long from)
    {
        if (from >= SchemaVersion)
            return;

        if (from < 2)
        {
            // v1 → v2: add the bundle columns to the pre-existing documents table.
            AddColumnIfMissing("documents", "is_bundle", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing("documents", "member_count", "INTEGER NOT NULL DEFAULT 0");
        }

        Exec($"UPDATE schema_meta SET version={SchemaVersion};");
    }

    private void AddColumnIfMissing(string table, string column, string definition)
    {
        bool present = false;
        using (var info = _conn.CreateCommand())
        {
            info.CommandText = $"PRAGMA table_info({table});";
            using var reader = info.ExecuteReader();
            while (reader.Read())
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    { present = true; break; }
        }
        if (!present)
            Exec($"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }

    public void Upsert(Document doc)
    {
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            UpsertCore(doc, tx);
            tx.Commit();
        }
    }

    public void UpsertMany(IEnumerable<Document> docs)
    {
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            foreach (var doc in docs)
                UpsertCore(doc, tx);
            tx.Commit();
        }
    }

    private void UpsertCore(Document doc, SqliteTransaction tx)
    {
        // Remove any existing rows for this doc_id (both tables, shared rowid).
        long? existing = FindRowId(doc.DocId, tx);
        if (existing is long old)
        {
            ExecParam("DELETE FROM documents_fts WHERE rowid=$r;", tx, ("$r", old));
            ExecParam("DELETE FROM documents WHERE rowid=$r;", tx, ("$r", old));
        }

        ExecParam(@"INSERT INTO documents(doc_id, path, mtime, size, content, is_bundle, member_count)
                    VALUES($id, $path, $mtime, $size, $content, $bundle, $members);", tx,
            ("$id", doc.DocId), ("$path", doc.Path), ("$mtime", doc.Mtime),
            ("$size", doc.Size), ("$content", doc.Content),
            ("$bundle", doc.IsBundle ? 1 : 0), ("$members", doc.MemberCount));

        long rowid;
        using (var rid = _conn.CreateCommand())
        {
            rid.Transaction = tx;
            rid.CommandText = "SELECT last_insert_rowid();";
            rowid = Convert.ToInt64(rid.ExecuteScalar());
        }

        // Indexing policy: the file NAME is always indexed alongside the content so
        // filename-only matches work, while documents.content stays pure (for clean
        // match-line display). doc.Content holds the original content; the FTS tokens
        // are derived from "<filename> <content>".
        var fileName = Path.GetFileName(doc.Path);
        var tokens = NgramTokenizer.Tokenize(fileName + " " + doc.Content);
        ExecParam("INSERT INTO documents_fts(rowid, tokens) VALUES($r, $t);", tx,
            ("$r", rowid), ("$t", tokens));
    }

    private long? FindRowId(string docId, SqliteTransaction? tx)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT rowid FROM documents WHERE doc_id=$id;";
        cmd.Parameters.AddWithValue("$id", docId);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    public void Delete(string docId) => DeleteMany(new[] { docId });

    public void DeleteMany(IEnumerable<string> docIds)
    {
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            foreach (var id in docIds)
            {
                if (FindRowId(id, tx) is long r)
                {
                    ExecParam("DELETE FROM documents_fts WHERE rowid=$r;", tx, ("$r", r));
                    ExecParam("DELETE FROM documents WHERE rowid=$r;", tx, ("$r", r));
                }
            }
            tx.Commit();
        }
    }

    public IEnumerable<(string DocId, double Mtime)> IterDocsUnder(string root)
    {
        var prefix = Path.GetFullPath(root);
        // Materialize under the gate rather than yielding lazily: a lazy iterator would
        // hold no lock between the consumer's MoveNext calls, racing with concurrent
        // writes (and risking re-entrant lock attempts). Callers use this to build an
        // in-memory (doc_id → mtime) map, so eager materialization is fine.
        var result = new List<(string, double)>();
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT doc_id, mtime FROM documents WHERE doc_id=$p OR doc_id LIKE $like ORDER BY doc_id;";
            cmd.Parameters.AddWithValue("$p", prefix);
            cmd.Parameters.AddWithValue("$like", prefix.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "%");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add((reader.GetString(0), reader.GetDouble(1)));
        }
        return result;
    }

    public IReadOnlyList<SearchHit> Search(string query, int limit = 50, int offset = 0)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SearchHit>();

        var tokens = NgramTokenizer.Tokens(query);
        if (tokens.Count == 0)
            return Array.Empty<SearchHit>();

        // AND-join FTS5 phrase-quoted tokens; escape embedded double-quotes by doubling.
        var match = string.Join(" AND ", tokens.Select(t => "\"" + t.Replace("\"", "\"\"") + "\""));

        lock (_readGate)
        {
            using var cmd = _readConn.CreateCommand();
            cmd.CommandText = @"
                SELECT d.doc_id, d.path, bm25(documents_fts) AS score
                FROM documents_fts f
                JOIN documents d ON d.rowid = f.rowid
                WHERE documents_fts MATCH $m
                ORDER BY bm25(documents_fts) ASC, d.doc_id
                LIMIT $limit OFFSET $offset;";
            cmd.Parameters.AddWithValue("$m", match);
            cmd.Parameters.AddWithValue("$limit", limit);
            cmd.Parameters.AddWithValue("$offset", offset);

            var hits = new List<SearchHit>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                hits.Add(new SearchHit(reader.GetString(0), reader.GetString(1), reader.GetDouble(2)));
            return hits;
        }
    }

    public int Count()
    {
        lock (_readGate)
        {
            using var cmd = _readConn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM documents;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    public (long FileCount, long TotalBytes) FolderStats(string root)
    {
        var prefix = Path.GetFullPath(root);
        lock (_readGate)
        {
            using var cmd = _readConn.CreateCommand();
            cmd.CommandText =
                "SELECT COALESCE(SUM(CASE WHEN is_bundle=1 THEN member_count ELSE 1 END),0), COALESCE(SUM(size),0) " +
                "FROM documents WHERE doc_id=$p OR doc_id LIKE $like;";
            cmd.Parameters.AddWithValue("$p", prefix);
            cmd.Parameters.AddWithValue("$like", prefix.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "%");
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return (reader.GetInt64(0), reader.GetInt64(1));
            return (0L, 0L);
        }
    }

    /// <summary>
    /// Returns the stored content for a doc_id, or null if absent. Used by
    /// <c>InProcessProvider.GetMatchLines</c> for inline match-line extraction.
    /// (Not part of <see cref="IIndexStore"/> — an implementation-specific helper.)
    /// </summary>
    public string? GetContent(string docId)
    {
        lock (_readGate)
        {
            using var cmd = _readConn.CreateCommand();
            cmd.CommandText = "SELECT content FROM documents WHERE doc_id=$id;";
            cmd.Parameters.AddWithValue("$id", docId);
            var result = cmd.ExecuteScalar();
            return result is null or DBNull ? null : (string)result;
        }
    }

    /// <summary>
    /// For each doc id that is a bundle entry, its member count. Non-bundle (or unknown)
    /// ids are omitted. Lets the UI annotate a hit like "folder of 12,430 images".
    /// </summary>
    public IReadOnlyDictionary<string, long> GetBundleSizes(IEnumerable<string> docIds)
    {
        var ids = docIds as ICollection<string> ?? docIds.ToList();
        var result = new Dictionary<string, long>();
        if (ids.Count == 0)
            return result;

        lock (_readGate)
        {
            using var cmd = _readConn.CreateCommand();
            // Parameterize each id ($p0,$p1,…) to keep it injection-safe.
            var names = new List<string>(ids.Count);
            int i = 0;
            foreach (var id in ids)
            {
                var name = "$p" + i++;
                names.Add(name);
                cmd.Parameters.AddWithValue(name, id);
            }
            cmd.CommandText =
                $"SELECT doc_id, member_count FROM documents WHERE is_bundle=1 AND doc_id IN ({string.Join(",", names)});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = reader.GetInt64(1);
        }
        return result;
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void ExecParam(string sql, SqliteTransaction? tx, params (string Name, object Value)[] ps)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _readConn.Close();
        _readConn.Dispose();
        _conn.Close();
        _conn.Dispose();
        // Connection pooling can keep the file handle alive; clear ALL pools (covers
        // both connections' pools) so temp DBs can be deleted in tests.
        SqliteConnection.ClearAllPools();
    }
}
