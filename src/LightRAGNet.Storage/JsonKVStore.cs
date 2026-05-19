using System.Text.Json;
using LightRAGNet.Core.IO;
using LightRAGNet.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LightRAGNet.Storage;

/// <summary>
/// JSON file-based KV store implementation
/// Reference: Python version kg/json_kv_impl.py
/// </summary>
public class JsonKVStore : IKVStore
{
    private readonly string _filePath;
    private readonly ILogger<JsonKVStore> _logger;
    private Dictionary<string, Dictionary<string, object>> _data = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    
    public JsonKVStore(
        string filePath,
        ILogger<JsonKVStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
        
        // Load existing data
        LoadData();
    }
    
    public async Task<Dictionary<string, object>?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return _data.TryGetValue(id, out var value)
                ? CloneRecord(value)
                : null;
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task<List<Dictionary<string, object>>> GetByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return ids
                .Where(_data.ContainsKey)
                .Select(id => CloneRecord(_data[id]))
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task<HashSet<string>> FilterKeysAsync(
        HashSet<string> keys,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var existing = _data.Keys.ToHashSet(StringComparer.Ordinal);
            return keys.Where(k => !existing.Contains(k)).ToHashSet();
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task UpsertAsync(
        Dictionary<string, Dictionary<string, object>> data,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            foreach (var kvp in data)
            {
                _data[kvp.Key] = CloneRecord(kvp.Value);
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task DeleteAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            foreach (var id in ids)
            {
                _data.Remove(id);
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return _data.Count == 0;
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task IndexDoneCallbackAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await SaveDataAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task DropAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var previousData = CloneData(_data);

            // Clear data in memory
            _data.Clear();
            
            // Persist empty state to file immediately
            try
            {
                await SaveDataAsync(cancellationToken);
            }
            catch
            {
                _data = previousData;
                throw;
            }
            
            _logger.LogInformation("Cleared data in memory and file: {FilePath}", _filePath);
        }
        finally
        {
            _lock.Release();
        }
    }
    
    private void LoadData()
    {
        if (!File.Exists(_filePath))
        {
            _data = new Dictionary<string, Dictionary<string, object>>();
            return;
        }
        
        try
        {
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                _data = new Dictionary<string, Dictionary<string, object>>();
                return;
            }
            
            _data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(json)
                ?? new Dictionary<string, Dictionary<string, object>>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load data from {FilePath}", _filePath);
            _data = new Dictionary<string, Dictionary<string, object>>();
        }
    }
    
    private async Task SaveDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await AtomicFileWriter.WriteAllTextAsync(
                _filePath,
                json,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save data to {FilePath}", _filePath);
            throw;
        }
    }

    private static Dictionary<string, object> CloneRecord(Dictionary<string, object> source)
    {
        return source.ToDictionary(
            pair => pair.Key,
            pair => CloneValue(pair.Value),
            StringComparer.Ordinal);
    }

    private static Dictionary<string, Dictionary<string, object>> CloneData(
        Dictionary<string, Dictionary<string, object>> source)
    {
        return source.ToDictionary(
            pair => pair.Key,
            pair => CloneRecord(pair.Value),
            StringComparer.Ordinal);
    }

    private static object CloneValue(object value)
    {
        return value switch
        {
            Dictionary<string, object> dictionary => CloneRecord(dictionary),
            IReadOnlyDictionary<string, object> dictionary => dictionary.ToDictionary(
                pair => pair.Key,
                pair => CloneValue(pair.Value),
                StringComparer.Ordinal),
            List<string[]> list => list.Select(item => item.ToArray()).ToList(),
            List<string> list => list.ToList(),
            string[][] array => array.Select(item => item.ToArray()).ToArray(),
            string[] array => array.ToArray(),
            List<object> list => list.Select(CloneValue).ToList(),
            object[] array => array.Select(item => item is null ? null : CloneValue(item)).ToArray(),
            JsonElement json => JsonSerializer.Deserialize<object>(json.GetRawText()) ?? json.ToString(),
            System.Collections.IEnumerable enumerable and not string => enumerable
                .Cast<object?>()
                .Select(item => item is null ? null : CloneValue(item))
                .ToList(),
            _ => value
        };
    }
}

