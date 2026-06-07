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
/// Connection model: a single long-lived writer connection under WAL. SQLite serializes
/// writes; WAL permits concurrent readers. The vertical slice uses this one connection
/// for both reads and writes (simple and sufficient for the slice).
/// </summary>
public sealed class SqliteFtsStore : IIndexStore, IContentStore
{
    private readonly SqliteConnection _conn;

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

    private void InitSchema()
    {
        Exec(@"
            CREATE TABLE IF NOT EXISTS schema_meta(version INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS documents(
                rowid   INTEGER PRIMARY KEY,
                doc_id  TEXT UNIQUE NOT NULL,
                path    TEXT NOT NULL,
                mtime   REAL NOT NULL,
                size    INTEGER NOT NULL,
                content TEXT NOT NULL
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS documents_fts USING fts5(tokens, tokenize='unicode61');
        ");

        // Seed schema version once.
        using var check = _conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM schema_meta;";
        var count = Convert.ToInt64(check.ExecuteScalar());
        if (count == 0)
            Exec("INSERT INTO schema_meta(version) VALUES(1);");
    }

    public void Upsert(Document doc)
    {
        using var tx = _conn.BeginTransaction();
        UpsertCore(doc, tx);
        tx.Commit();
    }

    public void UpsertMany(IEnumerable<Document> docs)
    {
        using var tx = _conn.BeginTransaction();
        foreach (var doc in docs)
            UpsertCore(doc, tx);
        tx.Commit();
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

        ExecParam(@"INSERT INTO documents(doc_id, path, mtime, size, content)
                    VALUES($id, $path, $mtime, $size, $content);", tx,
            ("$id", doc.DocId), ("$path", doc.Path), ("$mtime", doc.Mtime),
            ("$size", doc.Size), ("$content", doc.Content));

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

    public IEnumerable<(string DocId, double Mtime)> IterDocsUnder(string root)
    {
        var prefix = Path.GetFullPath(root);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT doc_id, mtime FROM documents WHERE doc_id=$p OR doc_id LIKE $like ORDER BY doc_id;";
        cmd.Parameters.AddWithValue("$p", prefix);
        cmd.Parameters.AddWithValue("$like", prefix.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "%");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            yield return (reader.GetString(0), reader.GetDouble(1));
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

        using var cmd = _conn.CreateCommand();
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

    public int Count()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM documents;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Returns the stored content for a doc_id, or null if absent. Used by
    /// <c>InProcessProvider.GetMatchLines</c> for inline match-line extraction.
    /// (Not part of <see cref="IIndexStore"/> — an implementation-specific helper.)
    /// </summary>
    public string? GetContent(string docId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT content FROM documents WHERE doc_id=$id;";
        cmd.Parameters.AddWithValue("$id", docId);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? null : (string)result;
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
        _conn.Close();
        _conn.Dispose();
        // Connection pooling can keep the file handle alive; clear pools so temp DBs can be deleted in tests.
        SqliteConnection.ClearAllPools();
    }
}
