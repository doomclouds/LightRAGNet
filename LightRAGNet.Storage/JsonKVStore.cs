using System.Text.Json;
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
    
    public Task<Dictionary<string, object>?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        _data.TryGetValue(id, out var value);
        return Task.FromResult(value);
    }
    
    public Task<List<Dictionary<string, object>>> GetByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        var result = ids
            .Where(id => _data.ContainsKey(id))
            .Select(id => _data[id])
            .ToList();
        
        return Task.FromResult(result);
    }
    
    public Task<HashSet<string>> FilterKeysAsync(
        HashSet<string> keys,
        CancellationToken cancellationToken = default)
    {
        var existing = _data.Keys.ToHashSet();
        var filtered = keys.Where(k => !existing.Contains(k)).ToHashSet();
        return Task.FromResult(filtered);
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
                _data[kvp.Key] = kvp.Value;
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
    
    public Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_data.Count == 0);
    }
    
    public async Task IndexDoneCallbackAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            SaveData();
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
            // Clear data in memory
            _data.Clear();
            
            // Persist empty state to file immediately
            SaveData();
            
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
    
    private void SaveData()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save data to {FilePath}", _filePath);
        }
    }
}

