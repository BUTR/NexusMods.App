﻿using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Data.SQLite;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using BitFaster.Caching.Lru;
using NexusMods.DataModel.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.DataModel.Interprocess;
using NexusMods.Hashing.xxHash64;
using NexusMods.Paths;


namespace NexusMods.DataModel;

public class SqliteDataStore : IDataStore, IDisposable
{
    private readonly AbsolutePath _path;
    private readonly string _connectionString;
    private readonly SQLiteConnection _conn;
    private readonly Dictionary<EntityCategory,string> _getStatements;
    private readonly Dictionary<EntityCategory,string> _putStatements;
    private readonly Dictionary<EntityCategory,string> _casStatements;
    private readonly Lazy<JsonSerializerOptions> _jsonOptions;
    private readonly Dictionary<EntityCategory,string> _prefixStatements;
    private readonly Dictionary<EntityCategory,string> _deleteStatements;
    private readonly ConcurrentLru<Id,Entity> _cache;
    
    private readonly IMessageProducer<RootChange> _rootChangeProducer;
    private readonly IMessageConsumer<RootChange> _rootChangeConsumer;
    private readonly ConcurrentQueue<RootChange> _pendingRootChanges = new();
    private readonly ConcurrentQueue<IdPut> _pendingIdPuts = new();
    private readonly CancellationTokenSource _enqueuerTcs;
    private readonly Task _enqueuerTask;
    private readonly ILogger<SqliteDataStore> _logger;
    private readonly IMessageProducer<IdPut> _idPutProducer;
    private readonly IMessageConsumer<IdPut> _idPutConsumer;

    public SqliteDataStore(ILogger<SqliteDataStore> logger, AbsolutePath path, IServiceProvider provider, 
        IMessageProducer<RootChange> rootChangeProducer, 
        IMessageConsumer<RootChange> rootChangeConsumer,
        IMessageProducer<IdPut> idPutProducer,
        IMessageConsumer<IdPut> idPutConsumer)
    {
        _logger = logger;
        _path = path;
        _connectionString = string.Intern($"Data Source={path}");
        _conn = new SQLiteConnection(_connectionString);
        _conn.Open();
        
        _getStatements = new Dictionary<EntityCategory, string>();
        _putStatements = new Dictionary<EntityCategory, string>();
        _casStatements = new Dictionary<EntityCategory, string>();
        _prefixStatements = new Dictionary<EntityCategory, string>();
        _deleteStatements = new Dictionary<EntityCategory, string>();
        EnsureTables();
        
        _jsonOptions = new Lazy<JsonSerializerOptions>(provider.GetRequiredService<JsonSerializerOptions>);
        _cache = new ConcurrentLru<Id, Entity>(1000);
        
        _rootChangeProducer = rootChangeProducer;
        _rootChangeConsumer = rootChangeConsumer;
        _idPutProducer = idPutProducer;
        _idPutConsumer = idPutConsumer;

        _enqueuerTcs = new CancellationTokenSource();
        _enqueuerTask = Task.Run(EnqueuerLoop);
    }

    private async Task EnqueuerLoop()
    {
        while (!_enqueuerTcs.Token.IsCancellationRequested)
        {
            if (_pendingIdPuts.TryDequeue(out var put))
            {
                await _idPutProducer.Write(put, _enqueuerTcs.Token);
                continue;
            }
            if (_pendingRootChanges.TryDequeue(out var change))
            {
                await _rootChangeProducer.Write(change, _enqueuerTcs.Token);
                continue;
            }
            await Task.Delay(100);
        }
    }

    private void EnsureTables()
    {
        foreach (var table in Enum.GetValues<EntityCategory>())
        {
            using var cmd = new SQLiteCommand($"CREATE TABLE IF NOT EXISTS {table} (Id BLOB PRIMARY KEY, Data BLOB)", _conn);
            cmd.ExecuteNonQuery();
            
            _getStatements[table] = $"SELECT Data FROM [{table}] WHERE Id = @id";
            _putStatements[table] = $"INSERT OR REPLACE INTO [{table}] (Id, Data) VALUES (@Id, @data)";
            _casStatements[table] =
                $"UPDATE [{table}] SET Data = @newData WHERE Data = @oldData AND Id = @id RETURNING *;";
            _prefixStatements[table] = $"SELECT Id, Data FROM [{table}] WHERE Id >= @prefix ORDER BY Id ASC";
            _deleteStatements[table] = $"DELETE FROM [{table}] WHERE Id = @id";
        }
    }
    
    public Id Put<T>(T value) where T : Entity
    {
        using var cmd = new SQLiteCommand(_putStatements[value.Category], _conn); 
        var ms = new MemoryStream();
        JsonSerializer.Serialize(ms, value, _jsonOptions.Value);
        var msBytes = ms.ToArray();
        var hash = new xxHashAlgorithm(0).HashBytes(msBytes);
        var idBytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(idBytes, hash);
        cmd.Parameters.AddWithValue("@id", idBytes);
        cmd.Parameters.AddWithValue("@data", msBytes);
        
        cmd.ExecuteNonQuery();

        var id = new Id64(value.Category, hash);
        _pendingIdPuts.Enqueue(new IdPut(IdPut.PutType.Put, id));
        return id;
    }

    public void Put<T>(Id id, T value) where T : Entity
    {
        using var cmd = new SQLiteCommand(_putStatements[value.Category], _conn); 
        Span<byte> idSpan = stackalloc byte[id.SpanSize];
        id.ToSpan(idSpan);
        cmd.Parameters.AddWithValue("@Id", idSpan.ToArray());
        var ms = new MemoryStream();
        JsonSerializer.Serialize(ms, value, _jsonOptions.Value);
        ms.Position = 0;
        cmd.Parameters.AddWithValue("@Data", ms.ToArray());
        cmd.ExecuteNonQuery();
        _pendingIdPuts.Enqueue(new IdPut(IdPut.PutType.Put, id));
    }

    public T? Get<T>(Id id, bool canCache) where T : Entity
    {
        if (canCache && _cache.TryGet(id, out var cached))
            return (T)cached;

        using var cmd = new SQLiteCommand(_getStatements[id.Category], _conn);
        var idBytes = new byte[id.SpanSize];
        id.ToSpan(idBytes.AsSpan());
        cmd.Parameters.AddWithValue("@id", idBytes);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        var blob = reader.GetStream(0);
        var value = (T?)JsonSerializer.Deserialize<Entity>(blob, _jsonOptions.Value);
        if (value == null) return null;
        value.DataStoreId = id;
        
        if (canCache)
            _cache.AddOrUpdate(id, value);
        
        return value;
    }

    public bool PutRoot(RootType type, Id oldId, Id newId)
    {
        using var cmd = new SQLiteCommand(_putStatements[EntityCategory.Roots], _conn);
        cmd.Parameters.AddWithValue("@id", (byte)type);
        var newData = new byte[newId.SpanSize + 1];
        newId.ToTaggedSpan(newData.AsSpan());
        cmd.Parameters.AddWithValue("@data", newData);

        cmd.ExecuteNonQuery();
        
        _pendingRootChanges.Enqueue(new RootChange {Type = type, From = oldId, To = newId});
        
        return true;
    }

    public Id? GetRoot(RootType type)
    {
        using var cmd = new SQLiteCommand(_getStatements[EntityCategory.Roots], _conn);
        cmd.Parameters.AddWithValue("@id", (byte)type);
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            var blob = reader.GetStream(0);
            var bytes = new byte[blob.Length];
            blob.Read(bytes, 0, bytes.Length);
            return Id.FromTaggedSpan(bytes);
        }

        return null;

    }


    public byte[]? GetRaw(Id id)
    {
        using var cmd = new SQLiteCommand(_getStatements[id.Category], _conn);
        var idBytes = new byte[id.SpanSize];
        id.ToSpan(idBytes.AsSpan());
        cmd.Parameters.AddWithValue("@id", idBytes);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        var size = reader.GetBytes(0, 0, null, 0, 0);
        var bytes = new byte[size];
        reader.GetBytes(0, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public void PutRaw(Id id, ReadOnlySpan<byte> val)
    {
        using var cmd = new SQLiteCommand(_putStatements[id.Category], _conn);
        var idBytes = new byte[id.SpanSize];
        id.ToSpan(idBytes.AsSpan());
        cmd.Parameters.AddWithValue("@id", idBytes);
        cmd.Parameters.AddWithValue("@data", val.ToArray());
        cmd.ExecuteNonQuery();
        _pendingIdPuts.Enqueue(new IdPut(IdPut.PutType.Put, id));

    }

    public void Delete(Id id)
    {
        using var cmd = new SQLiteCommand(_deleteStatements[id.Category], _conn);
        var idBytes = new byte[id.SpanSize];
        id.ToSpan(idBytes.AsSpan());
        cmd.Parameters.AddWithValue("@id", idBytes);
        cmd.ExecuteNonQuery();
        _pendingIdPuts.Enqueue(new IdPut(IdPut.PutType.Delete, id));
    }

    public async Task<long> PutRaw(IAsyncEnumerable<(Id Key, byte[] Value)> kvs, CancellationToken token = default)
    {
        var iterator = kvs.GetAsyncEnumerator(token);
        var processed = 0;
        var totalLoaded = 0L;
        var done = false;

        while (true)
        {
            {
                await using var tx = await _conn.BeginTransactionAsync(token);

                while (processed < 100)
                {
                    if (!await iterator.MoveNextAsync())
                    {
                        done = true;
                        break;
                    }

                    var (id, val) = iterator.Current;
                    await using var cmd = new SQLiteCommand(_putStatements[id.Category], _conn);
                    var idBytes = new byte[id.SpanSize];
                    id.ToSpan(idBytes.AsSpan());
                    cmd.Parameters.AddWithValue("@id", idBytes);
                    cmd.Parameters.AddWithValue("@data", val);
                    await cmd.ExecuteNonQueryAsync(token);
                    processed++;
                }

                if (processed > 0)
                {
                    await tx.CommitAsync(token);
                    totalLoaded += processed;
                }

                if (done)
                    break;
                processed = 0;
            }
        }

        return totalLoaded;

    }

    public IEnumerable<T> GetByPrefix<T>(Id prefix) where T : Entity
    {
        using var cmd = new SQLiteCommand(_prefixStatements[prefix.Category], _conn);
        var idBytes = new byte[prefix.SpanSize];
        prefix.ToSpan(idBytes.AsSpan());
        cmd.Parameters.AddWithValue("@prefix", idBytes);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetId(prefix.Category, 0);
            if (!id.IsPrefixedBy(prefix))
            {
                yield break;
            }
            
            var blob = reader.GetStream(1);
            var value = JsonSerializer.Deserialize<Entity>(blob, _jsonOptions.Value);
            if (value is T tc)
            {
                value.DataStoreId = id;
                yield return tc;
            }
        }
    }

    public IObservable<RootChange> RootChanges => _rootChangeConsumer.Messages.SelectMany(WaitTillRootReady);
    public IObservable<Id> IdChanges  => _idPutConsumer.Messages.SelectMany(WaitTillPutReady);
    
    /// <summary>
    /// Sometimes we may get a change notification before the underlying SQLite database has actually updated the value.
    /// So we wait a bit to make sure the value is actually there before we forward the change.
    /// </summary>
    /// <param name="change"></param>
    /// <returns></returns>
    private async Task<RootChange> WaitTillRootReady(RootChange change)
    {
        var maxCycles = 0;
        while (GetRaw(change.To) == null && maxCycles < 10)
        {
            _logger.LogDebug("Waiting for root change {To} to be ready", change.To);
            await Task.Delay(100);
            maxCycles++;
        }
        _logger.LogDebug("Root change {To} is ready", change.To);
        return change;
    }
    
    /// <summary>
    /// Sometimes we may get a change notification before the underlying SQLite database has actually updated the value.
    /// So we wait a bit to make sure the value is actually there before we forward the change.
    /// </summary>
    /// <param name="change"></param>
    /// <returns></returns>
    private async Task<Id> WaitTillPutReady(IdPut change)
    {
        var maxCycles = 0;
        while (change.Type != IdPut.PutType.Delete && GetRaw(change.Id) == null && maxCycles < 10)
        {
            _logger.LogDebug("Waiting for write to {Id} to complete", change.Id);
            await Task.Delay(100);
            maxCycles++;
        }
        _logger.LogTrace("Id {Id} is updated", change.Id);
        return change.Id;
    }

    public void Dispose()
    {
        _conn.Dispose();
        _enqueuerTcs.Dispose();
    }
}


internal static class SqlExtensions
{
    public static Id GetId(this SQLiteDataReader reader, EntityCategory ent, int column)
    {
        var blob = reader.GetStream(column);
        var bytes = new byte[blob.Length];
        blob.Read(bytes, 0, bytes.Length);
        return Id.FromSpan(ent, bytes);
    }
}