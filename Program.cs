using Azure.AI.Projects;
using Azure.Identity;
using Azure;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Azure.AI.Agents.Persistent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CodeInterpreterClient;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Code Interpreter Client (MAF Hybrid) ===\n");

        try
        {
            // Load configuration from appsettings.json
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var projectEndpoint = configuration["AzureAI:ProjectEndpoint"] 
                ?? throw new InvalidOperationException("ProjectEndpoint not found in configuration");
            var agentName = configuration["AzureAI:AgentName"] 
                ?? throw new InvalidOperationException("AgentName not found in configuration");

            // Initialize Azure credentials
            var credential = new DefaultAzureCredential();
            
            // Initialize Persistent Agents Client
            var persistentClient = new PersistentAgentsClient(projectEndpoint, credential);

            Console.WriteLine($"Setting up agent '{agentName}'...");

            // Try to get existing agent or create a new one
            var agents = persistentClient.Administration.GetAgents();
            var persistentAgent = agents.FirstOrDefault(a => a.Name == agentName);
            
            if (persistentAgent == null)
            {
                Console.WriteLine($"Creating new agent '{agentName}'...");
                persistentAgent = persistentClient.Administration.CreateAgent(
                    model: "gpt-4o",
                    name: agentName,
                    instructions: "You are a helpful data analysis assistant with access to a code interpreter. " +
                                  "When you receive Excel files, analyze them thoroughly and provide insights. " +
                                  "Use the code interpreter to read, process, and visualize data as needed.",
                    tools: new List<ToolDefinition> { new CodeInterpreterToolDefinition() }
                ).Value;
                Console.WriteLine($"✓ Agent created: {persistentAgent.Name}");
            }
            else
            {
                Console.WriteLine($"✓ Connected to existing agent: {persistentAgent.Name}");
            }
            
            // Wrap the persistent agent in Microsoft Agent Framework (MAF) for streaming chat
            IChatClient chatClient = persistentClient.AsIChatClient(persistentAgent.Id);
            ChatClientAgent mafAgent = new ChatClientAgent(
                chatClient,
                options: new ChatClientAgentOptions
                {
                    Id = persistentAgent.Id,
                    Name = persistentAgent.Name,
                    Description = persistentAgent.Description
                });
            Console.WriteLine($"✓ Wrapped agent in MAF: {mafAgent.Name}");
            Console.WriteLine();

            // Step 1: List available Excel files in current directory (one time at start)
            Console.WriteLine("Available Excel files in current directory:");
            Console.WriteLine("===========================================");
            var availableFiles = Directory.GetFiles(Directory.GetCurrentDirectory())
                .Where(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || 
                           f.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFileName(f))
                .ToList();

            for (int i = 0; i < availableFiles.Count; i++)
            {
                var fileInfo = new FileInfo(availableFiles[i]);
                Console.WriteLine($"{i + 1}. {availableFiles[i]} ({FormatFileSize(fileInfo.Length)})");
            }
            Console.WriteLine();

            // Step 2: Ask user to select Excel file to upload (required)
            if (availableFiles.Count == 0)
            {
                Console.WriteLine("No Excel files found in current directory.");
                Console.WriteLine("Please add .xlsx or .xls files to the directory and try again.");
                return;
            }

            int fileIndex;
            while (true)
            {
                Console.Write("Enter the number of the Excel file to upload: ");
                var selection = Console.ReadLine();
                
                if (int.TryParse(selection, out fileIndex) && fileIndex > 0 && fileIndex <= availableFiles.Count)
                    break;
                    
                Console.WriteLine($"Please enter a number between 1 and {availableFiles.Count}");
            }

            var uploadedFilePath = availableFiles[fileIndex - 1];
            Console.Write($"\nUploading {uploadedFilePath}");
            
            // Upload file with progress dots
            var uploadTask = Task.Run(async () =>
            {
                var fileInfo = await persistentClient.Files.UploadFileAsync(
                    filePath: uploadedFilePath,
                    purpose: PersistentAgentFilePurpose.Agents);
                return fileInfo.Value.Id;
            });
            
            // Show progress dots while uploading
            while (!uploadTask.IsCompleted)
            {
                Console.Write(".");
                await Task.Delay(300);
            }
            
            var uploadedFileId = await uploadTask;
            var fileSize = new FileInfo(uploadedFilePath).Length;
            Console.WriteLine($" ✓ Done ({FormatFileSize(fileSize)})");
            Console.WriteLine($"   File ID: {uploadedFileId}\n");

            // Create a new thread with file attached (hybrid: persistent thread + MAF thread)
            var toolResources = new ToolResources
            {
                CodeInterpreter = new CodeInterpreterToolResource()
            };
            toolResources.CodeInterpreter.FileIds.Add(uploadedFileId);
            
            var persistentThread = persistentClient.Threads.CreateThread(toolResources: toolResources).Value;
            var mafThread = mafAgent.GetNewThread(persistentThread.Id);
            Console.WriteLine($"✓ Thread created with file attached (ID: {persistentThread.Id})\n");

            // Main conversation loop - continues until user types "exit"
            while (true)
            {
                // Step 3: Ask what needs to be done
                Console.WriteLine("What would you like the code interpreter to do? (type 'exit' to quit)");
                Console.Write("> ");
                var userRequest = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(userRequest))
                {
                    Console.WriteLine("No request provided.\n");
                    continue;
                }

                // Check for exit command
                if (userRequest.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\nExiting...");
                    break;
                }

                // Step 4: Send request to agent using MAF streaming
                Console.WriteLine("\n=== Agent Response ===");
                Console.Write("Agent: ");
                
                // Use MAF streaming for real-time response
                await foreach (var update in mafAgent.RunStreamingAsync(userRequest, mafThread))
                {
                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        Console.Write(update.Text);
                    }
                }
                Console.WriteLine();
                
                // HYBRID: Use persistent client to check for generated files
                // Get the latest run to find its ID
                var runs = persistentClient.Runs.GetRuns(persistentThread.Id);
                var latestRun = runs.FirstOrDefault();
                
                List<string> imageFileIds = new List<string>();
                
                if (latestRun != null)
                {
                    // Get messages and extract file IDs from the latest run
                    var messages = persistentClient.Messages.GetMessages(
                        threadId: persistentThread.Id,
                        order: ListSortOrder.Descending);
                    
                    foreach (var message in messages)
                    {
                        if (message.Role == MessageRole.Agent && message.RunId == latestRun.Id)
                        {
                            foreach (var content in message.ContentItems)
                            {
                                if (content is MessageImageFileContent imageFileContent)
                                {
                                    imageFileIds.Add(imageFileContent.FileId);
                                }
                            }
                        }
                        else if (message.Role == MessageRole.User)
                        {
                            // Stop when we hit user messages
                            break;
                        }
                    }
                }
                
                // Offer to download generated images/files
                if (imageFileIds.Count > 0)
                {
                    Console.WriteLine($"\n{imageFileIds.Count} file(s) generated. Download to current directory? (y/n)");
                    Console.Write("> ");
                    var downloadResponse = Console.ReadLine()?.Trim().ToLowerInvariant();
                    
                    if (downloadResponse == "y" || downloadResponse == "yes")
                    {
                        foreach (var fileId in imageFileIds)
                        {
                            await DownloadFileAsync(persistentClient, fileId);
                        }
                    }
                }
                
                Console.WriteLine("\n--- Ready for next request ---\n");
            }

            Console.WriteLine("\n=== Session Complete ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Error: {ex.Message}");
            Console.WriteLine($"\nDetails: {ex}");
        }
    }



    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private static async Task DownloadFileAsync(PersistentAgentsClient client, string fileId)
    {
        try
        {
            Console.Write($"Downloading {fileId}");
            
            // Get file content
            BinaryData fileContent = client.Files.GetFileContent(fileId).Value;
            
            // Create filename with timestamp to avoid conflicts
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"agent_output_{timestamp}_{fileId.Substring(fileId.Length - 8)}.png";
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
            
            // Save to disk
            await File.WriteAllBytesAsync(filePath, fileContent.ToArray());
            
            Console.WriteLine($" ✓ Saved to: {fileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($" ✗ Failed: {ex.Message}");
        }
    }
}
