using LightRAGNet;
using LightRAGNet.Core.Models;
using LightRAGNet.Core.Utils;
using LightRAGNet.Example;
using LightRAGNet.Hosting;
using LightRAGNet.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .Build();

var services = new ServiceCollection();

services.AddLightRAG(configuration);

services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

// Interactive console program
Console.WriteLine("=== LightRAG.NET Test Program ===");
Console.WriteLine();
Console.WriteLine("Available commands:");
Console.WriteLine("  clear        - Clear all stored data");
Console.WriteLine("  insert       - Insert default document (SKILL.md) into LightRAG system");
Console.WriteLine("  insert <path> - Insert specified file into LightRAG system (supports quotes, e.g.: insert \"d:\\path\\1.md\")");
Console.WriteLine("  test         - Read SKILL.md files from all subdirectories in ./skills and insert into RAG system");
Console.WriteLine("  query / q    - Execute 3 preset test queries");
Console.WriteLine("  query <question> / q <question> - Execute custom query");
Console.WriteLine("  token <text> - Calculate token count for text using DeepSeekTokenizer");
Console.WriteLine("  token <file> - Calculate token count for specified file using DeepSeekTokenizer (supports quotes, e.g.: token \"d:\\path\\1.md\")");
Console.WriteLine("  exit         - Exit program");
Console.WriteLine();

var serviceProvider = services.BuildServiceProvider();

// Subscribe to task state change events
var rag = serviceProvider.GetRequiredService<LightRAG>();
var taskStateLogFile = Path.Combine(Directory.GetCurrentDirectory(), $"./logs/task_state_{DateTime.Now:yyyyMMdd.HHmmss}.txt");
var fInfo = new FileInfo(taskStateLogFile);
if (fInfo.Directory?.Exists == false)
{
    fInfo.Directory?.Create();
}
rag.TaskStateChanged += (_, state) => OnTaskStateChanged(state, taskStateLogFile);
Console.WriteLine($"Task state log will be saved to: {taskStateLogFile}");
Console.WriteLine();

// Main loop: wait for user input
while (true)
{
    Console.Write("Enter command (clear/insert/insert <path>/test/query/q/token <text or file>/exit): ");
    var input = Console.ReadLine()?.Trim();
    
    if (string.IsNullOrEmpty(input))
    {
        continue;
    }
    
    var inputLower = input.ToLower();
    
    if (inputLower is "exit" or "quit")
    {
        Console.WriteLine("Program exiting");
        break;
    }
    
    if (inputLower is "clear" or "clr")
    {
        Console.WriteLine();
        Console.WriteLine("=== Clearing LightRAG Data ===");
        Console.WriteLine();
        await CleanData.CleanAllAsync(serviceProvider);
        Console.WriteLine();
        Console.WriteLine("Data clearing completed!");
        Console.WriteLine();
        continue;
    }
    
    if (inputLower.StartsWith("token "))
    {
        var content = input[6..].Trim(); // Remove "token " prefix
        content = RemoveQuotes(content); // Remove quotes
        if (string.IsNullOrEmpty(content))
        {
            Console.WriteLine("Error: token command requires text content or file path");
            Console.WriteLine("Usage: token <text> or token <file path>");
            Console.WriteLine("Example: token hello");
            Console.WriteLine("Example: token SKILL.md");
            Console.WriteLine("Example: token \"d:\\path\\1.md\"");
            Console.WriteLine();
            continue;
        }
        
        Console.WriteLine();
        Console.WriteLine("=== DeepSeekTokenizer Test ===");
        Console.WriteLine();
        
        try
        {
            var deepSeekTokenizer = new DeepSeekTokenizer();
            
            string textToTest;
            var isFile = false;
            var filePath = "";
            
            // Check if it's a file path
            if (File.Exists(content))
            {
                // Absolute path or file in current directory
                filePath = content;
                isFile = true;
            }
            else
            {
                // Try to find file relative to current directory
                var relativePath = Path.Combine(Directory.GetCurrentDirectory(), content);
                if (File.Exists(relativePath))
                {
                    filePath = relativePath;
                    isFile = true;
                }
            }
            
            if (isFile)
            {
                // Read file content
                textToTest = await File.ReadAllTextAsync(filePath);
                var fileInfo = new FileInfo(filePath);
                
                Console.WriteLine($"File path: {filePath}");
                Console.WriteLine($"File size: {fileInfo.Length} bytes");
                Console.WriteLine($"File content length: {textToTest.Length} characters");
            }
            else
            {
                // Treat as text
                textToTest = content;
                Console.WriteLine($"Test text: \"{textToTest}\"");
            }

            Console.WriteLine();

            var tokens = deepSeekTokenizer.Encode(textToTest);
            var tokenCount = deepSeekTokenizer.CountTokens(textToTest);
            
            if (isFile)
            {
                Console.WriteLine($"Token count: {tokenCount}");
                Console.WriteLine($"Average tokens per character: {(double)tokenCount / textToTest.Length:F4}");
                Console.WriteLine($"Average characters per token: {(double)textToTest.Length / tokenCount:F2}");
            }
            else
            {
                Console.WriteLine($"Token IDs: [{string.Join(", ", tokens)}]");
                Console.WriteLine($"Token count: {tokenCount}");
                var decodedText = deepSeekTokenizer.Decode(tokens);
                Console.WriteLine($"Decoded result: \"{decodedText}\"");
            }
            
            Console.WriteLine();
            
            deepSeekTokenizer.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DeepSeekTokenizer test failed: {ex.Message}");
            Console.WriteLine();
        }
        
        continue;
    }
    
    if (inputLower == "insert" || inputLower.StartsWith("insert "))
    {
        // Parse file path
        string? filePath = null;
        if (inputLower.StartsWith("insert "))
        {
            filePath = input[7..].Trim(); // Remove "insert " prefix
            filePath = RemoveQuotes(filePath); // Remove quotes
            if (string.IsNullOrEmpty(filePath))
            {
                filePath = null; // If path is empty, use default file
            }
        }
        
        // Execute document insertion (supports multiple insertions, InsertAsync method internally checks if document already exists)
        await InsertDocumentAsync(serviceProvider, filePath);
        
        Console.WriteLine();
        Console.WriteLine("Document insertion completed!");
        Console.WriteLine();
        continue;
    }
    
    if (inputLower == "test")
    {
        Console.WriteLine();
        Console.WriteLine("=== Batch Insert Documents from skills Directory ===");
        Console.WriteLine();
        
        await InsertSkillsDocumentsAsync(serviceProvider);
        
        Console.WriteLine();
        Console.WriteLine("Batch insertion completed!");
        Console.WriteLine();
        continue;
    }
    
    if (inputLower == "query" || inputLower.StartsWith("query ") || 
        inputLower == "q" || inputLower.StartsWith("q "))
    {
        Console.WriteLine();
        
        // Handle shorthand command q
        var commandPrefix = inputLower.StartsWith("q ") ? "q " : 
                               inputLower == "q" ? "q" :
                               inputLower.StartsWith("query ") ? "query " : "query";
        
        if (inputLower is "query" or "q")
        {
            // Execute 3 preset test queries
            await RunPresetQueriesAsync(serviceProvider);
        }
        else
        {
            // Execute custom query
            var question = input[commandPrefix.Length..].Trim(); // Remove command prefix
            question = RemoveQuotes(question); // Remove quotes
            if (string.IsNullOrEmpty(question))
            {
                Console.WriteLine("Error: query command requires a query question");
                Console.WriteLine("Usage: query <question> or q <question>");
                Console.WriteLine("Example: query How to use YuQue MCP tool to create documents?");
                Console.WriteLine("Example: q How to use YuQue MCP tool to create documents?");
                Console.WriteLine();
                continue;
            }
            
            await ExecuteQueryAsync(serviceProvider, question);
        }
        
        Console.WriteLine();
        continue;
    }
    
    Console.WriteLine($"Unknown command: {input}");
    Console.WriteLine("Available commands: clear, insert, insert <path>, test, query/q, query/q <question>, token <text or file>, exit");
    Console.WriteLine();
}

return;

static string RemoveQuotes(string str)
{
    if (string.IsNullOrEmpty(str))
    {
        return str;
    }
    
    str = str.Trim();

    return str switch
    {
        // Check and remove double quotes
        ['"', _, ..] when str[^1] == '"' => str.Substring(1, str.Length - 2),
        // Check and remove single quotes
        ['\'', _, ..] when str[^1] == '\'' => str.Substring(1, str.Length - 2),
        _ => str
    };
}

static async Task InsertDocumentAsync(IServiceProvider serviceProvider, string? filePath = null)
{
    var rag = serviceProvider.GetRequiredService<LightRAG>();
    
    // Determine file path to insert
    string actualFilePath;
    string displayFileName;
    
    if (string.IsNullOrEmpty(filePath))
    {
        // Use default file SKILL.md
        actualFilePath = Path.Combine(Directory.GetCurrentDirectory(), "SKILL.md");
        displayFileName = "SKILL.md";
        Console.WriteLine("=== LightRAG.NET Example - Insert Default Document ===");
    }
    else
    {
        // Use specified file path
        // Absolute path
        actualFilePath = Path.IsPathRooted(filePath) ? filePath :
            // Relative path, relative to current directory
            Path.Combine(Directory.GetCurrentDirectory(), filePath);
        displayFileName = Path.GetFileName(actualFilePath);
        Console.WriteLine("=== LightRAG.NET Example - Insert Specified Document ===");
    }
    
    Console.WriteLine();

    // Check if file exists
    if (!File.Exists(actualFilePath))
    {
        Console.WriteLine($"Error: File not found {actualFilePath}");
        return;
    }

    Console.WriteLine($"Reading file: {actualFilePath}");
    var document = await File.ReadAllTextAsync(actualFilePath);
    Console.WriteLine($"File size: {document.Length} characters");
    Console.WriteLine();

    Console.WriteLine("Inserting document into LightRAG system (auto-chunking)...");
    var docId = await rag.InsertAsync(document, filePath: displayFileName);
    Console.WriteLine($"Document inserted, ID: {docId}");
    Console.WriteLine("Document has been automatically chunked and stored in vector database and graph database");
    Console.WriteLine();

    // Wait a bit to ensure data is fully written
    await Task.Delay(1000);
}

static async Task RunPresetQueriesAsync(IServiceProvider serviceProvider)
{
    var rag = serviceProvider.GetRequiredService<LightRAG>();
    
    // Test query 1: Basic information about YuQue document management
    Console.WriteLine("=== Test Query 1 ===");
    Console.WriteLine("Question: How to use YuQue MCP tool to create documents?");
    await ExecuteQueryWithStreamAsync(rag, "How to use YuQue MCP tool to create documents?");
    Console.WriteLine();

    // Test query 2: About search functionality
    Console.WriteLine("=== Test Query 2 ===");
    Console.WriteLine("Question: How to use search functionality to find documents?");
    await ExecuteQueryWithStreamAsync(rag, "How to use search functionality to find documents?");
    Console.WriteLine();

    // Test query 3: About error handling
    Console.WriteLine("=== Test Query 3 ===");
    Console.WriteLine("Question: How should I handle document creation failures?");
    await ExecuteQueryWithStreamAsync(rag, "How should I handle document creation failures?");
    Console.WriteLine();

    Console.WriteLine("=== Preset Queries Completed ===");
}

static async Task ExecuteQueryWithStreamAsync(LightRAG rag, string question)
{
    var queryResult = await rag.QueryAsync(
        question,
        new QueryParam
        {
            Mode = QueryMode.Mix,
            TopK = 10,
            ChunkTopK = 5,
            EnableRerank = true,
            Stream = true // Enable streaming output
        });

    // Display reference sources first
    if (queryResult.ReferenceList.Count > 0)
    {
        Console.WriteLine("Reference sources:");
        foreach (var reference in queryResult.ReferenceList)
        {
            Console.WriteLine($"  [{reference.ReferenceId}] {reference.FilePath}");
        }
        Console.WriteLine();
    }

    // Stream answer output
    Console.WriteLine("Answer:");
    if (queryResult is { IsStreaming: true, ResponseIterator: not null })
    {
        // Streaming output
        await foreach (var chunk in queryResult.ResponseIterator)
        {
            Console.Write(chunk);
        }
        Console.WriteLine(); // New line
    }
    else
    {
        // Non-streaming output (fallback)
        Console.WriteLine(queryResult.Content ?? "");
    }
}

static async Task ExecuteQueryAsync(IServiceProvider serviceProvider, string question)
{
    var rag = serviceProvider.GetRequiredService<LightRAG>();
    
    Console.WriteLine("=== Custom Query ===");
    Console.WriteLine($"Question: {question}");
    Console.WriteLine();
    
    await ExecuteQueryWithStreamAsync(rag, question);
    
    Console.WriteLine();
}

static void OnTaskStateChanged(TaskState state, string logFile)
{
    try
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var progressInfo = state.Total > 0 
            ? $"{state.Current}/{state.Total} ({state.ProgressPercentage:F2}%)"
            : "Batch operation";
        
        var logEntry = $"""
            ========================================
            [Time] {timestamp}
            [Stage] {state.Stage}
            [Progress] {progressInfo}
            [Description] {state.Description}
            """;
        
        if (!string.IsNullOrEmpty(state.Details))
        {
            logEntry += $"[Details] {state.Details}\n";
        }
        
        if (!string.IsNullOrEmpty(state.DocId))
        {
            logEntry += $"[Document ID] {state.DocId}\n";
        }
        
        logEntry += $"[Completed] {(state.IsCompleted ? "Yes" : "No")}\n";
        
        // Only show progress percentage when there's valid progress
        if (state.Total > 0)
        {
            logEntry += $"[Progress Percentage] {state.ProgressPercentage:F2}%\n";
        }
        
        logEntry += "========================================\n\n";
        
        // Append to file
        File.AppendAllText(logFile, logEntry);
        
        // Also output to console (simplified version)
        var consoleProgress = state.Total > 0 
            ? $"({state.Current}/{state.Total} - {state.ProgressPercentage:F1}%)"
            : "";
        Console.WriteLine($"[{timestamp}] [{state.Stage}] {state.Description} {consoleProgress}".TrimEnd());
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to write task state log: {ex.Message}");
    }
}

static async Task InsertSkillsDocumentsAsync(IServiceProvider serviceProvider)
{
    var rag = serviceProvider.GetRequiredService<LightRAG>();
    
    // Get skills directory path
    var skillsDir = Path.Combine(Directory.GetCurrentDirectory(), "skills");
    
    if (!Directory.Exists(skillsDir))
    {
        Console.WriteLine($"Error: Directory not found {skillsDir}");
        return;
    }
    
    Console.WriteLine($"Scanning directory: {skillsDir}");
    Console.WriteLine();
    
    // Get all subdirectories
    var subDirectories = Directory.GetDirectories(skillsDir);
    
    if (subDirectories.Length == 0)
    {
        Console.WriteLine("No subdirectories found");
        return;
    }
    
    Console.WriteLine($"Found {subDirectories.Length} subdirectories");
    Console.WriteLine();
    
    var successCount = 0;
    var skipCount = 0;
    var errorCount = 0;
    
    // Iterate through each subdirectory
    foreach (var subDir in subDirectories)
    {
        var dirName = Path.GetFileName(subDir);
        var skillFilePath = Path.Combine(subDir, "SKILL.md");
        var fileName = $"{dirName}.md"; // Use folder name as file name
        
        Console.WriteLine($"Processing directory: {dirName}");
        
        // Check if SKILL.md file exists
        if (!File.Exists(skillFilePath))
        {
            Console.WriteLine($"  Skipped: SKILL.md file not found");
            skipCount++;
            Console.WriteLine();
            continue;
        }
        
        try
        {
            // Read file content
            Console.WriteLine($"  Reading file: {skillFilePath}");
            var document = await File.ReadAllTextAsync(skillFilePath);
            Console.WriteLine($"  File size: {document.Length} characters");
            
            // Insert document into RAG system
            Console.WriteLine($"  Inserting document into LightRAG system (file name: {fileName})...");
            var docId = await rag.InsertAsync(document, filePath: fileName);
            Console.WriteLine($"  Document inserted, ID: {docId}");
            
            successCount++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error: Failed to insert document - {ex.Message}");
            errorCount++;
        }
        
        Console.WriteLine();
    }
    
    // Output statistics
    Console.WriteLine("=== Batch Insertion Statistics ===");
    Console.WriteLine($"Success: {successCount}");
    Console.WriteLine($"Skipped: {skipCount}");
    Console.WriteLine($"Failed: {errorCount}");
    Console.WriteLine($"Total: {subDirectories.Length} directories");
}


