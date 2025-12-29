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

namespace CodeInterpreterClient;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Code Interpreter Client ===\n");

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
            var agent = agents.FirstOrDefault(a => a.Name == agentName);
            
            if (agent == null)
            {
                Console.WriteLine($"Creating new agent '{agentName}'...");
                agent = persistentClient.Administration.CreateAgent(
                    model: "gpt-4o",
                    name: agentName,
                    instructions: "You are a helpful data analysis assistant with access to a code interpreter. " +
                                  "When you receive Excel files, analyze them thoroughly and provide insights. " +
                                  "Use the code interpreter to read, process, and visualize data as needed.",
                    tools: new List<ToolDefinition> { new CodeInterpreterToolDefinition() }
                ).Value;
                Console.WriteLine($"✓ Agent created: {agent.Name}");
            }
            else
            {
                Console.WriteLine($"✓ Connected to existing agent: {agent.Name}");
            }
            
            Console.WriteLine($"✓ Connected to agent: {agent.Name}");
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

            // Step 2: Ask user to select Excel file to upload (one time at start)
            Console.Write("Enter the number of the Excel file to upload (or press Enter to skip): ");
            var selection = Console.ReadLine();

            string? uploadedFilePath = null;
            string? uploadedFileId = null;
            
            if (!string.IsNullOrWhiteSpace(selection) && int.TryParse(selection, out int fileIndex) 
                && fileIndex > 0 && fileIndex <= availableFiles.Count)
            {
                uploadedFilePath = availableFiles[fileIndex - 1];
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
                
                uploadedFileId = await uploadTask;
                var fileSize = new FileInfo(uploadedFilePath).Length;
                Console.WriteLine($" ✓ Done ({FormatFileSize(fileSize)})");
                Console.WriteLine($"   File ID: {uploadedFileId}\n");
            }
            else
            {
                Console.WriteLine("No file selected.\n");
            }

            // Create a new thread for this session
            PersistentAgentThread thread;
            
            // Attach file to thread if uploaded
            if (!string.IsNullOrEmpty(uploadedFileId))
            {
                var toolResources = new ToolResources
                {
                    CodeInterpreter = new CodeInterpreterToolResource()
                };
                toolResources.CodeInterpreter.FileIds.Add(uploadedFileId);
                
                // Create thread with tool resources  
                thread = persistentClient.Threads.CreateThread(toolResources: toolResources);
                Console.WriteLine($"✓ Thread created with file attached\n");
            }
            else
            {
                thread = persistentClient.Threads.CreateThread();
            }

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

                // Step 4: Send request to agent and get response
                Console.WriteLine("\n=== Agent Response ===");
                
                // Create message in thread
                persistentClient.Messages.CreateMessage(
                    thread.Id,
                    MessageRole.User,
                    userRequest);
                
                // Show progress indicator while running
                var runTask = Task.Run(async () =>
                {
                    // Run the agent
                    var runResponse = persistentClient.Runs.CreateRun(thread, agent);
                    var run = runResponse.Value;
                    
                    // Wait for completion
                    while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
                    {
                        await Task.Delay(500);
                        run = persistentClient.Runs.GetRun(thread.Id, run.Id).Value;
                    }
                    
                    return run;
                });
                
                // Show progress dots
                while (!runTask.IsCompleted)
                {
                    Console.Write(".");
                    await Task.Delay(500);
                }
                Console.WriteLine();
                
                var completedRun = await runTask;
                
                // Get messages and display the assistant's response
                var messages = persistentClient.Messages.GetMessages(
                    threadId: thread.Id,
                    order: ListSortOrder.Descending);
                
                List<string> imageFileIds = new List<string>();
                bool foundResponse = false;
                
                // Get the latest agent messages from this run
                foreach (var message in messages)
                {
                    // Only process messages from this run
                    if (message.Role == MessageRole.Agent && message.RunId == completedRun.Id)
                    {
                        if (!foundResponse)
                        {
                            Console.Write("Agent: ");
                            foundResponse = true;
                        }
                        
                        foreach (var content in message.ContentItems)
                        {
                            if (content is MessageTextContent textContent)
                            {
                                Console.WriteLine(textContent.Text);
                            }
                            else if (content is MessageImageFileContent imageFileContent)
                            {
                                Console.WriteLine($"\n[Generated image: {imageFileContent.FileId}]");
                                imageFileIds.Add(imageFileContent.FileId);
                            }
                        }
                    }
                    else if (foundResponse)
                    {
                        // Stop after processing all messages from this run
                        break;
                    }
                }
                
                // Offer to download generated images/files
                if (imageFileIds.Count > 0)
                {
                    Console.WriteLine($"\n{imageFileIds.Count} file(s) generated. Download to current directory? (y/n)");
                    Console.Write("> ");
                    var downloadResponse = Console.ReadLine();
                    
                    if (downloadResponse?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
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
