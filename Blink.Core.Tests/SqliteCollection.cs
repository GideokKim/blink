namespace Blink.Core.Tests;

/// <summary>
/// Test classes that open SQLite stores. Microsoft.Data.Sqlite's connection pool is
/// process-wide and these classes call <c>SqliteConnection.ClearAllPools()</c> in Dispose (so
/// Windows can delete the temp DB files); run in parallel, one class's clear can dispose a pooled
/// connection another class just checked out. Sharing one collection serializes them.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqliteCollection
{
    public const string Name = "sqlite";
}
