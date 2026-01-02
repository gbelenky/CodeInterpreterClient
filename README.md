# Code Interpreter Client (.NET)

A .NET console application that demonstrates file upload and code interpreter capabilities using Azure AI Foundry Agents with a **Hybrid Architecture**: Microsoft Agent Framework (MAF) for streaming chat + `Azure.AI.Agents.Persistent` SDK for file operations.

## Architecture Overview

### Hybrid Approach

This project uses a **hybrid architecture** that combines the best of both worlds:

| Component | Technology | Purpose |
|-----------|------------|---------|
| **Chat Interface** | Microsoft Agent Framework (MAF) | Real-time streaming responses via `IChatClient` |
| **File Upload** | `Azure.AI.Agents.Persistent` | Upload Excel files for analysis |
| **File Download** | `Azure.AI.Agents.Persistent` | Download agent-generated visualizations |
| **Agent Creation** | `Azure.AI.Agents.Persistent` | Programmatic agent setup with code interpreter |

### Why Hybrid?

**MAF Benefits:**
- ✅ `IChatClient` interface - standardized chat abstraction
- ✅ `RunStreamingAsync()` - real-time token streaming
- ✅ `ChatClientAgent` - clean agent wrapper pattern
- ✅ Future-proof architecture aligned with Microsoft's direction

**MAF Limitation:**
- ❌ Abstracts away `MessageImageFileContent` - file IDs appear as `sandbox:` URLs in text output

**Solution**: After MAF streaming completes, use the persistent client to query messages and extract actual file IDs for download.

### How MAF and Persistent Agent Interact

The hybrid architecture works by linking MAF's abstraction layer to the underlying persistent agent infrastructure:

```
┌────────────────────────────────────────────────────────────────────────────┐
│                         Request/Response Flow                               │
├────────────────────────────────────────────────────────────────────────────┤
│                                                                            │
│  User Input                                                                │
│      │                                                                     │
│      ▼                                                                     │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │  MAF Layer (ChatClientAgent)                                         │  │
│  │                                                                      │  │
│  │  1. RunStreamingAsync(userRequest, mafThread)                        │  │
│  │     └─► Internally calls persistent agent via IChatClient            │  │
│  │                                                                      │  │
│  │  2. Streams tokens back in real-time                                 │  │
│  │     └─► foreach (var update in stream) Console.Write(update.Text)   │  │
│  │                                                                      │  │
│  │  ⚠️  File references abstracted as sandbox: URLs in text            │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│      │                                                                     │
│      │ After streaming completes                                           │
│      ▼                                                                     │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │  Persistent Layer (PersistentAgentsClient)                           │  │
│  │                                                                      │  │
│  │  3. GetRuns(persistentThread.Id) → Find latest run ID                │  │
│  │                                                                      │  │
│  │  4. GetMessages(threadId, Descending) → Get all messages             │  │
│  │                                                                      │  │
│  │  5. Extract MessageImageFileContent from agent messages              │  │
│  │     └─► Real file IDs like "assistant-2kXRAbFNbmmd..."              │  │
│  │                                                                      │  │
│  │  6. Files.GetFileContent(fileId) → Download binary data              │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│      │                                                                     │
│      ▼                                                                     │
│  Downloaded File (e.g., agent_output_20260102_182152_cQ871hn6.png)        │
│                                                                            │
└────────────────────────────────────────────────────────────────────────────┘
```

#### Key Integration Points

1. **`AsIChatClient()` Extension Method**
   ```csharp
   IChatClient chatClient = persistentClient.AsIChatClient(persistentAgent.Id);
   ```
   This bridges the persistent SDK to MAF by creating an `IChatClient` wrapper around a specific agent. The wrapper handles message creation, run execution, and response retrieval internally.

2. **Linked Thread IDs**
   ```csharp
   var persistentThread = persistentClient.Threads.CreateThread(toolResources).Value;
   var mafThread = mafAgent.GetNewThread(persistentThread.Id);  // Same ID!
   ```
   Both threads share the **same conversation ID**. When MAF executes a run, it operates on the same thread that the persistent client can query. This enables post-run inspection of messages.

3. **Message Visibility**
   - **MAF sees**: Text content streamed token-by-token
   - **Persistent client sees**: Full message structure including `MessageImageFileContent` with actual file IDs
   
   This difference is why the hybrid approach is necessary—MAF provides better UX for chat, but persistent client provides access to file metadata.

4. **Run Synchronization**
   After `RunStreamingAsync()` completes, the run is finished on the server. The persistent client can immediately query:
   - `Runs.GetRuns()` to find the completed run
   - `Messages.GetMessages()` to inspect all content types
   - `Files.GetFileContent()` to download generated files

### Framework Ecosystem

#### **Microsoft Agent Framework (MAF)**
The overarching framework for building AI agents in the Microsoft ecosystem. It provides:
- Standardized patterns for agent creation and interaction
- `IChatClient` interface from `Microsoft.Extensions.AI`
- Integration with Azure AI Foundry via `AsIChatClient()`

#### **Azure AI Foundry Types**

1. **Azure AI Foundry (Modern)**
   - Portal: [ai.azure.com](https://ai.azure.com)
   - SDK: `Microsoft.Agents.AI.AzureAI` (preview)
   - Pre-created agents via portal
   - Simplified API surface
   - Status: Active development

2. **Azure AI Foundry Classic**
   - Portal: [ai.azure.com](https://ai.azure.com) (same portal, different agent type)
   - SDK: `Azure.AI.Agents.Persistent` (stable)
   - Full-featured API including file operations
   - Agents created programmatically
   - Status: Stable, feature-complete

## Key Technologies

### NuGet Packages
```xml
<PackageReference Include="Azure.AI.Agents.Persistent" Version="*-*" />
<PackageReference Include="Azure.AI.Projects" Version="*-*" />
<PackageReference Include="Azure.Identity" Version="*-*" />
<PackageReference Include="Microsoft.Agents.AI.AzureAI" Version="*-*" />
<PackageReference Include="Microsoft.Extensions.AI" Version="*-*" />
<PackageReference Include="Microsoft.Extensions.Configuration" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="9.0.0" />
```

### SDK Comparison

| Feature | Persistent SDK | MAF (`Microsoft.Agents.AI`) | **Hybrid (This Project)** |
|---------|----------------|----------------------------|---------------------------|
| **File Upload** | ✅ Supported | ❌ Not exposed | ✅ Via Persistent |
| **File Download** | ✅ Supported | ❌ Not exposed | ✅ Via Persistent |
| **Streaming Chat** | ❌ Polling only | ✅ `RunStreamingAsync` | ✅ Via MAF |
| **IChatClient** | ❌ Not available | ✅ Native support | ✅ Via MAF |
| **Agent Creation** | Programmatic | Portal or Programmatic | Programmatic |
| **Threading Model** | `PersistentAgentThread` | `AgentThread` | Both linked |

## Features

- ✅ **Real-time streaming** responses via MAF `RunStreamingAsync()`
- ✅ Upload Excel files (.xlsx, .xls) for data analysis
- ✅ Animated progress indicators during file upload
- ✅ Code interpreter integration for data analysis and visualization
- ✅ Automatic detection of agent-generated images/charts
- ✅ Download visualizations to current directory
- ✅ Conversational interface with full context retention
- ✅ Configuration externalization via `appsettings.json`
- ✅ **IChatClient pattern** - standardized chat abstraction

## How It Works

### 1. **Agent Setup & MAF Wrapper**
```csharp
// Initialize the Persistent Agents Client
var persistentClient = new PersistentAgentsClient(projectEndpoint, credential);

// Try to get existing agent or create new one
var agents = persistentClient.Administration.GetAgents();
var persistentAgent = agents.FirstOrDefault(a => a.Name == agentName) 
    ?? persistentClient.Administration.CreateAgent(
        model: "gpt-4o",
        name: agentName,
        instructions: "You are a helpful data analysis assistant...",
        tools: new List<ToolDefinition> { new CodeInterpreterToolDefinition() }
    ).Value;

// Wrap in Microsoft Agent Framework (MAF) for streaming chat
IChatClient chatClient = persistentClient.AsIChatClient(persistentAgent.Id);
ChatClientAgent mafAgent = new ChatClientAgent(
    chatClient,
    options: new ChatClientAgentOptions
    {
        Id = persistentAgent.Id,
        Name = persistentAgent.Name,
        Description = persistentAgent.Description
    });
```
**Why Hybrid**: MAF provides the `IChatClient` pattern and streaming, while persistent SDK handles file operations.

### 2. **File Upload**
```csharp
// Upload file with progress dots
var uploadTask = Task.Run(async () =>
{
    var fileInfo = await persistentClient.Files.UploadFileAsync(
        filePath: uploadedFilePath,
        purpose: PersistentAgentFilePurpose.Agents);
    return fileInfo.Value.Id;
});

// Show animated progress
while (!uploadTask.IsCompleted)
{
    Console.Write(".");
    await Task.Delay(300);
}
```
**Why**: Modern SDK doesn't expose this. Legacy SDK's `Files.UploadFileAsync()` is the only way to upload files.

### 3. **Thread Creation with File Attachment**
```csharp
// Create thread with file attached
var toolResources = new ToolResources
{
    CodeInterpreter = new CodeInterpreterToolResource()
};
toolResources.CodeInterpreter.FileIds.Add(uploadedFileId);

PersistentAgentThread thread = persistentClient.Threads.CreateThread(
    toolResources: toolResources);
```
**Why**: Files must be attached to threads via `toolResources` for the agent to access them.

### 4. **Linked Thread Creation**
```csharp
// Create persistent thread (with or without file attachment)
PersistentAgentThread persistentThread = persistentClient.Threads.CreateThread(
    toolResources: toolResources).Value;

// Create MAF thread linked to the same conversation
AgentThread mafThread = mafAgent.GetNewThread(persistentThread.Id);
```
**Why**: Both threads share the same conversation ID, enabling MAF streaming while persistent client tracks messages.

### 5. **Streaming Chat via MAF**
```csharp
// Real-time streaming response
await foreach (var update in mafAgent.RunStreamingAsync(userRequest, mafThread))
{
    if (!string.IsNullOrEmpty(update.Text))
    {
        Console.Write(update.Text);  // Token-by-token output
    }
}
```
**Why**: MAF's `RunStreamingAsync()` provides real-time token streaming instead of polling.

### 6. **Hybrid File Download**
```csharp
// After MAF streaming completes, use persistent client to find generated files
var runs = persistentClient.Runs.GetRuns(persistentThread.Id);
var latestRun = runs.FirstOrDefault();

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
                // Download generated visualization
                BinaryData fileContent = persistentClient.Files.GetFileContent(
                    imageFileContent.FileId).Value;
                await File.WriteAllBytesAsync(filePath, fileContent.ToArray());
            }
        }
    }
}
```
**Why**: MAF abstracts away `MessageImageFileContent`, so we fall back to the persistent client to extract file IDs and download them.

## Configuration

### appsettings.json
```json
{
  "AzureAI": {
    "ProjectEndpoint": "https://YOUR_FOUNDRY_RESOURCE.services.ai.azure.com/api/projects/YOUR_FOUNDRY_PROJECT",
    "AgentName": "CodeInterpreterTest"
  }
}
```

**Important**: `appsettings.json` is in `.gitignore` and should not be committed to source control.

### Environment Variables (Alternative)
```bash
export AZURE_AI_ENDPOINT="https://YOUR_FOUNDRY_RESOURCE.services.ai.azure.com/api/projects/YOUR_FOUNDRY_PROJECT"
```

## Prerequisites

- .NET 9.0 SDK or later
- Azure AI Foundry project with model deployment (gpt-4o)
- Azure authentication configured:
  - Azure CLI: `az login`
  - Or Visual Studio credentials
  - Or Environment variables (AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET)

## Setup

1. **Clone/Download** the project

2. **Install dependencies**:
   ```bash
   dotnet restore
   ```

3. **Configure settings**:
   ```bash
   # Copy the sample configuration file
   cp appsettings.sample.json appsettings.json
   
   # Edit appsettings.json with your values
   # - ProjectEndpoint: Your Azure AI Foundry project endpoint (found in Azure portal)
   # - AgentName: Name for your code interpreter agent (can be anything)
   ```
   
   **To find your ProjectEndpoint**:
   - Go to [ai.azure.com](https://ai.azure.com)
   - Select your project
   - Go to **Settings** → **Project details**
   - Copy the **Project connection string** or **API endpoint**
   - Format: `https://YOUR_FOUNDRY_RESOURCE.services.ai.azure.com/api/projects/YOUR_FOUNDRY_PROJECT`

4. **Authenticate with Azure**:
   ```bash
   az login
   ```

## Usage

### Important: Working Directory
The application searches for Excel files in the **current working directory** (where you run the command from). 

**Recommended**: Place your data files in the `CodeInterpreterClient` directory and run from there, or run the app from any directory containing Excel files you want to analyze.

### Option 1: Run from Project Directory (Recommended)
```bash
cd CodeInterpreterClient
dotnet run
```
This is the simplest approach - the app will find Excel files in the same directory.

### Option 2: Run from Solution File
```bash
# From the root directory (where CodeInterpreter.sln is located)
# First, ensure your Excel files are in the CodeInterpreter root directory
dotnet run --project CodeInterpreterClient
```
**Note**: When running from the solution root, the app looks for Excel files in the solution root directory (`CodeInterpreter/`), not in `CodeInterpreterClient/`.

### Option 3: Using Visual Studio or VS Code
- **Visual Studio**: Open `CodeInterpreter.sln`, set `CodeInterpreterClient` as startup project, press F5
- **VS Code**: Open the workspace, use "Run and Debug" (F5) or use the integrated terminal
- **Note**: The working directory will be set by the IDE, typically the project directory

### Option 4: Build and Run Separately
```bash
# Build the solution
dotnet build CodeInterpreter.sln

# Run from the directory containing your data files
cd CodeInterpreterClient
dotnet bin/Debug/net9.0/CodeInterpreterClient.dll

# Or from solution root (data files must be in solution root)
dotnet CodeInterpreterClient/bin/Debug/net9.0/CodeInterpreterClient.dll
```

### Interactive Session Flow

1. **Agent Connection**
   - Retrieves or creates agent "CodeInterpreterTest"
   - Wraps agent in MAF `ChatClientAgent`
   - Confirms connection and displays agent name

2. **File Selection**
   - Lists Excel files (.xlsx, .xls) in current directory
   - Displays file sizes in human-readable format
   - Prompts for file selection (or skip)

3. **File Upload** (if selected)
   - Shows animated progress dots during upload
   - Displays file ID upon completion
   - Creates thread with file attached

4. **Conversation Loop**
   - Accepts natural language requests
   - **Streams response in real-time** via MAF
   - Displays agent responses token-by-token
   - Detects generated images/visualizations via persistent client

5. **File Download** (if files generated)
   - Prompts: "X file(s) generated. Download to current directory? (y/n)"
   - Downloads files with timestamped names
   - Format: `agent_output_yyyyMMdd_HHmmss_<fileId>.png`

6. **Exit**
   - Type "exit" to end session

## Example Session

```
=== Code Interpreter Client (MAF Hybrid) ===

Setting up agent 'CodeInterpreterTest'...
✓ Connected to existing agent: CodeInterpreterTest
✓ Wrapped agent in MAF: CodeInterpreterTest

Available Excel files in current directory:
===========================================
1. marketplace_data_50k.xlsx (2,1 MB)

Enter the number of the Excel file to upload (or press Enter to skip):
1

Uploading marketplace_data_50k.xlsx.......................... ✓ Done (2,1 MB)
   File ID: assistant-Rs2PN85aP5rHuP3U2VPxas

✓ Thread created with file attached (ID: thread_FzsBgRVkWEEgBrIvEL25Kx0Y)

What would you like the code interpreter to do? (type 'exit' to quit)
> what are the best and the worst sellers? Create a bar chart

=== Agent Response ===
Agent: To identify the best and worst sellers from the data file you uploaded, I'll first 
need to examine its contents. Let's load the file and inspect its structure...

The analysis shows the following:
- **Best Seller**: Laptops are the best-selling item with the highest total sales.
- **Worst Seller**: USB Cables are the worst-selling item with the lowest total sales.

The bar chart above visualizes the total sales of different items.

1 file(s) generated. Download to current directory? (y/n)
> y
Downloading assistant-2kXRAbFNbmmdAccQ871hn6 ✓ Saved to: agent_output_20260102_182152_cQ871hn6.png

--- Ready for next request ---

What would you like the code interpreter to do? (type 'exit' to quit)
> exit

Exiting...

=== Session Complete ===
```

## Key AI Components

**Agent Creation & MAF Wrapper**
```csharp
var persistentClient = new PersistentAgentsClient(projectEndpoint, credential);
var persistentAgent = persistentClient.Administration.CreateAgent(
    model: "gpt-4o",
    name: agentName,
    instructions: "You are a helpful data analysis assistant...",
    tools: new List<ToolDefinition> { new CodeInterpreterToolDefinition() }
).Value;

// Wrap in MAF for streaming
IChatClient chatClient = persistentClient.AsIChatClient(persistentAgent.Id);
ChatClientAgent mafAgent = new ChatClientAgent(chatClient, options: new ChatClientAgentOptions
{
    Id = persistentAgent.Id,
    Name = persistentAgent.Name
});
```

**Thread with File Attachment (Linked)**
```csharp
var toolResources = new ToolResources
{
    CodeInterpreter = new CodeInterpreterToolResource()
};
toolResources.CodeInterpreter.FileIds.Add(uploadedFileId);

// Create persistent thread with file
var persistentThread = persistentClient.Threads.CreateThread(toolResources: toolResources).Value;
// Link MAF thread to same conversation
var mafThread = mafAgent.GetNewThread(persistentThread.Id);
```
**Critical**: Both threads share the same conversation ID for hybrid operation.

**Streaming Chat via MAF**
```csharp
await foreach (var update in mafAgent.RunStreamingAsync(userRequest, mafThread))
{
    if (!string.IsNullOrEmpty(update.Text))
        Console.Write(update.Text);  // Real-time token streaming
}
```
Real-time response streaming instead of polling.

**Hybrid File Download**
```csharp
// After streaming completes, query persistent client for file IDs
var runs = persistentClient.Runs.GetRuns(persistentThread.Id);
var latestRun = runs.FirstOrDefault();

var messages = persistentClient.Messages.GetMessages(persistentThread.Id);
foreach (var message in messages.Where(m => m.Role == MessageRole.Agent && m.RunId == latestRun.Id))
{
    foreach (var content in message.ContentItems)
    {
        if (content is MessageImageFileContent imageFileContent)
        {
            // Download generated visualization
            var fileContent = persistentClient.Files.GetFileContent(imageFileContent.FileId).Value;
            await File.WriteAllBytesAsync(filePath, fileContent.ToArray());
        }
    }
}
```
MAF abstracts file references, so persistent client extracts actual file IDs.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    Code Interpreter Client                       │
│                      (MAF Hybrid Architecture)                   │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────────┐         ┌──────────────────────────────┐  │
│  │  User Interface  │         │  Microsoft Agent Framework   │  │
│  │                  │────────▶│  (ChatClientAgent)           │  │
│  │  Console I/O     │         │                              │  │
│  └──────────────────┘         │  • IChatClient interface     │  │
│          │                    │  • RunStreamingAsync()       │  │
│          │                    │  • Real-time tokens          │  │
│          │                    └──────────────┬───────────────┘  │
│          │                                   │                   │
│          │                    ┌──────────────▼───────────────┐  │
│          │                    │  Azure.AI.Agents.Persistent  │  │
│          └───────────────────▶│  (PersistentAgentsClient)    │  │
│                               │                              │  │
│                               │  • Files.UploadFileAsync()   │  │
│                               │  • Messages.GetMessages()    │  │
│                               │  • Files.GetFileContent()    │  │
│                               └──────────────┬───────────────┘  │
│                                              │                   │
└──────────────────────────────────────────────┼───────────────────┘
                                               │
                                               ▼
                              ┌────────────────────────────────┐
                              │     Azure AI Foundry           │
                              │                                │
                              │  • gpt-4o model deployment     │
                              │  • Code Interpreter tool       │
                              │  • File storage                │
                              └────────────────────────────────┘
```

## Future Migration

When MAF adds native file operation support, the hybrid approach can be simplified:

```csharp
// Future: Pure MAF approach (when file APIs are available)
IChatClient chatClient = persistentClient.AsIChatClient(agentId);
ChatClientAgent agent = new ChatClientAgent(chatClient, options);

// All operations via MAF - no need for persistent client fallback
await foreach (var update in agent.RunStreamingAsync(request, thread))
{
    // Handle text and file content directly from MAF
}
```

## Relationship to Microsoft Agent Framework

```
Microsoft Agent Framework (MAF)
├── Interfaces
│   ├── IChatClient (Microsoft.Extensions.AI)
│   ├── ChatClientAgent (Microsoft.Agents.AI)
│   └── AgentThread
├── Hosting Options
│   ├── Azure Functions (Durable Agents)
│   ├── ASP.NET Core
│   └── Console Apps (this project)
├── SDK Integration
│   ├── AsIChatClient() extension method
│   └── Persistent SDK compatibility
└── Azure AI Foundry
    ├── Project endpoint
    ├── Model deployments
    └── Agent configurations

This Project (Hybrid Architecture)
├── MAF Layer
│   ├── ChatClientAgent wrapper
│   ├── RunStreamingAsync() for chat
│   └── IChatClient pattern
└── Persistent Layer ← File operations
    ├── Files.UploadFileAsync()
    ├── Messages.GetMessages()
    └── Files.GetFileContent()
```

This project uses MAF for the chat interface while falling back to the persistent SDK for file operations not yet exposed in MAF.

## Project Files

### Configuration Files
- **`appsettings.sample.json`** - Sample configuration file (committed to git)
  - Copy this to `appsettings.json` and update with your values
  - Contains placeholders for ProjectEndpoint and AgentName
  
- **`appsettings.json`** - Actual configuration file (gitignored)
  - Contains your real Azure AI Foundry project endpoint
  - Not committed to source control for security

- **`.gitignore`** - Git ignore rules
  - Excludes build outputs (`bin/`, `obj/`)
  - Excludes `appsettings.json` (to protect secrets)
  - Excludes downloaded agent outputs (`agent_output_*.png`)
  - Includes VS Code and .NET specific ignores

### Source Files
- **`Program.cs`** - Main application code (307 lines)
- **`CodeInterpreterClient.csproj`** - Project file with NuGet package references
- **`README.md`** - This documentation

### Sample Data Files
- **`marketplace_data_50k.xlsx`** - Sample dataset with 50,000 marketplace transactions
  - Includes product sales data across multiple months
  - Use for testing data analysis and visualization features
  
- **`agent_output_20251229_180411_cyL16cbs.png`** - Example visualization
  - Generated by the agent using the prompt: *"generate sales development of the best-selling and the worst-selling product across all available months"*
  - Demonstrates the agent's ability to create charts and visualizations
  - Shows comparison between Laptop (best-selling) and HDMI Cable (worst-selling) sales trends
  - ![Sample Visualization](agent_output_20251229_183059_6coPHXqA.png)

**Note**: Downloaded agent outputs follow the naming pattern `agent_output_YYYYMMDD_HHmmss_<fileId>.png`

## Troubleshooting

### "appsettings.json not found"
```bash
cp appsettings.sample.json appsettings.json
# Then edit appsettings.json with your project endpoint
```

### "Agent 'X' not found"
- Legacy SDK cannot access portal-created agents
- Solution: Let the app create the agent programmatically

### File not visible to agent
- Ensure file uploaded before thread creation
- Verify `toolResources.CodeInterpreter.FileIds` contains file ID
- Use same thread for upload and queries

### Authentication errors
```bash
az login --scope https://management.azure.com/.default
```

### No images detected
- Verify agent has code interpreter tool enabled
- Check message content types in response
- Ensure agent actually generated visualization

## Related Documentation

- [Azure AI Foundry Agents (Classic)](https://learn.microsoft.com/azure/ai-foundry/agents/)
- [Code Interpreter Tool](https://learn.microsoft.com/azure/ai-foundry/agents/how-to/tools-classic/code-interpreter)
- [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/)
- [Azure.AI.Agents.Persistent SDK](https://www.nuget.org/packages/Azure.AI.Agents.Persistent)

## License

This sample code is provided for educational purposes.
