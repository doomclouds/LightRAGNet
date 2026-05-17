using System.Collections.Concurrent;
using System.Text.Json;
using LightRAGNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Services.TaskQueue;

/// <summary>
/// Task state persistence service implementation
/// </summary>
public class RagTaskStateStore : IRagTaskStateStore
{
    private readonly string _tasksFilePath;
    private readonly ILogger<RagTaskStateStore> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ConcurrentDictionary<string, RagTask> _tasksCache = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RagTaskStateStore(IOptions<LightRAGOptions> options, ILogger<RagTaskStateStore> logger)
    {
        var workingDir = options.Value.WorkingDir;
        
        // If relative path, convert to absolute path based on application runtime path
        if (!Path.IsPathRooted(workingDir))
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            workingDir = Path.Combine(baseDir, workingDir);
        }
        
        Directory.CreateDirectory(workingDir);
        _tasksFilePath = Path.Combine(workingDir, "tasks.json");
        _logger = logger;
        
        // Load task state on startup
        _ = Task.Run(async () =>
        {
            try
            {
                await LoadAllTasksAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load task state on startup");
            }
        });
    }

    public async Task SaveTaskStateAsync(RagTask task, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            // Update cache
            _tasksCache.AddOrUpdate(task.TaskId, task, (_, _) => task);
            
            // Save to file
            await SaveToFileAsync(cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<List<RagTask>> LoadAllTasksAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_tasksFilePath))
            {
                _logger.LogInformation("Task state file does not exist, returning empty list");
                return [];
            }

            try
            {
                var json = await File.ReadAllTextAsync(_tasksFilePath, cancellationToken);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return [];
                }

                var data = JsonSerializer.Deserialize<TasksFileData>(json, _jsonOptions);
                if (data?.Tasks == null)
                {
                    return [];
                }

                // Update cache
                _tasksCache.Clear();
                foreach (var task in data.Tasks)
                {
                    _tasksCache.TryAdd(task.TaskId, task);
                }

                _logger.LogInformation("Successfully loaded {Count} task states", data.Tasks.Count);
                return data.Tasks;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse task state file, file path: {FilePath}", _tasksFilePath);
                // Backup corrupted file
                var backupPath = $"{_tasksFilePath}.backup.{DateTime.UtcNow:yyyyMMddHHmmss}";
                File.Copy(_tasksFilePath, backupPath, true);
                _logger.LogWarning("Backed up corrupted file to: {BackupPath}", backupPath);
                return [];
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<RagTask?> LoadTaskStateAsync(string taskId, CancellationToken cancellationToken = default)
    {
        // First read from cache
        if (_tasksCache.TryGetValue(taskId, out var cachedTask))
        {
            return cachedTask;
        }

        // Cache miss, load from file
        var allTasks = await LoadAllTasksAsync(cancellationToken);
        return allTasks.FirstOrDefault(t => t.TaskId == taskId);
    }

    public async Task DeleteTaskStateAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            // Remove from cache
            _tasksCache.TryRemove(taskId, out _);
            
            // Save to file
            await SaveToFileAsync(cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAllTasksAsync(List<RagTask> tasks, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            // Update cache
            _tasksCache.Clear();
            foreach (var task in tasks)
            {
                _tasksCache.TryAdd(task.TaskId, task);
            }
            
            // Save to file
            await SaveToFileAsync(cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task ClearAllTasksAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            // Clear cache
            _tasksCache.Clear();
            
            // Delete file
            if (File.Exists(_tasksFilePath))
            {
                File.Delete(_tasksFilePath);
                _logger.LogInformation("Deleted task state file: {FilePath}", _tasksFilePath);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task SaveToFileAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tasks = _tasksCache.Values.ToList();
            var data = new TasksFileData
            {
                Version = "1.0",
                LastUpdated = DateTime.UtcNow,
                Tasks = tasks
            };

            var json = JsonSerializer.Serialize(data, _jsonOptions);
            
            // Atomic write: write to temporary file first, then rename
            var tempPath = $"{_tasksFilePath}.tmp";
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);
            File.Move(tempPath, _tasksFilePath, overwrite: true);
            
            _logger.LogDebug("Task state saved to file, task count: {Count}", tasks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save task state to file");
            throw;
        }
    }

    private class TasksFileData
    {
        public string Version { get; set; } = "1.0";
        public DateTime LastUpdated { get; set; }
        public List<RagTask> Tasks { get; set; } = [];
    }
}
