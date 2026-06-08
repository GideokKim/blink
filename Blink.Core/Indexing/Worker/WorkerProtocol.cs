using System.Text.Json;
using System.Text.Json.Serialization;
using Blink.Core.Model;

namespace Blink.Core.Indexing.Worker;

/// <summary>
/// JSON-line protocol shared by the indexer worker process and its in-process client.
///
/// Why a worker: in EDR/AV-locked environments the main executable's SMB reads of a NAS
/// can be blocked. We move ALL file I/O (enumerate, stat, parse) into a separate
/// (separately-signed) process that streams the resulting store operations as JSON lines
/// over stdout; the main process only ever touches the LOCAL SQLite file. The worker runs
/// the ordinary <see cref="Indexer"/> and <see cref="Pruner"/> against a store that emits
/// these ops instead of writing SQLite, so no indexing logic is duplicated.
/// </summary>
public static class WorkerProtocol
{
    public static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>A document the main process should upsert.</summary>
    public const string OpUpsert = "up";

    /// <summary>Doc ids the main process should delete.</summary>
    public const string OpDelete = "del";
}

/// <summary>One protocol line: either an upsert (<see cref="Doc"/>) or a delete (<see cref="Ids"/>).</summary>
public sealed record WireOp(
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("doc")] Document? Doc = null,
    [property: JsonPropertyName("ids")] string[]? Ids = null);

/// <summary>One entry of the known (doc_id → stored mtime) snapshot the client hands the worker.</summary>
public sealed record KnownEntry(
    [property: JsonPropertyName("id")] string DocId,
    [property: JsonPropertyName("m")] long Mtime);
