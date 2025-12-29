# Code Interpreter Client (.NET)

A .NET console application that demonstrates file upload and code interpreter capabilities using Azure AI Foundry Agents with the **Azure.AI.Agents.Persistent (Legacy) SDK**.

## Architecture Overview

### Framework Ecosystem

This project navigates the current Azure AI agent SDK landscape:

#### **Microsoft Agent Framework (MAF)**
The overarching framework for building AI agents in the Microsoft ecosystem. It provides:
- Standardized patterns for agent creation and interaction
- Consistent APIs across different hosting options
- Integration with Azure AI Foundry

#### **Azure AI Foundry Types**

1. **Azure AI Foundry (New/Modern)**
   - Portal: [ai.azure.com](https://ai.azure.com)
   - SDK: `Microsoft.Agents.AI.AzureAI` (preview)
   - Pre-created agents via portal
   - Simplified API surface
   - **Limitation**: File upload APIs not yet exposed
   - Status: Active development, recommended for new projects *without file upload*

2. **Azure AI Foundry Classic**
   - Portal: [ai.azure.com](https://ai.azure.com) (same portal, different agent type)
   - SDK: `Azure.AI.Agents.Persistent` (stable)
   - Full-featured API including file operations
   - Agents created programmatically
   - Complete control over agent lifecycle
   - Status: Stable, feature-complete

### Why We Use the Legacy SDK

**Primary Reason: File Upload Capability**

The modern `Microsoft.Agents.AI` SDK currently does not expose file upload APIs, which are essential for our use case:
- Upload Excel/CSV files for data analysis
- Attach files to conversation threads
- Download agent-generated visualizations

The legacy `Azure.AI.Agents.Persistent` SDK provides:
- ✅ `Files.UploadFileAsync()` - Upload files to agents
- ✅ `Threads.CreateThread(toolResources)` - Attach files to threads
- ✅ `Files.GetFileContent()` - Download generated files
- ✅ `Administration.CreateAgent()` - Programmatic agent creation
- ✅ Full Message/Run lifecycle management

**Trade-off**: We create agents programmatically instead of using pre-created portal agents.

## Key Technologies

### NuGet Packages
```xml
<PackageReference Include="Azure.AI.Agents.Persistent" Version="*-*" />
<PackageReference Include="Azure.AI.Projects" Version="*-*" />
<PackageReference Include="Azure.Identity" Version="*-*" />
<PackageReference Include="Microsoft.Extensions.Configuration" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="9.0.0" />
```

### SDK Comparison

| Feature | Legacy (`Azure.AI.Agents.Persistent`) | Modern (`Microsoft.Agents.AI`) |
|---------|---------------------------------------|--------------------------------|
| **File Upload** | ✅ Supported | ❌ Not exposed |
| **File Download** | ✅ Supported | ❌ Not exposed |
| **Agent Creation** | Programmatic only | Portal or Programmatic |
| **Pre-created Agents** | ❌ Cannot access | ✅ Can retrieve |
| **API Complexity** | More explicit | More abstracted |
| **Status** | Stable | Preview |
| **Threading Model** | `PersistentAgentThread` | `AgentThread` |
| **Use Case** | Full agent control + file ops | Simplified agent interaction |

## Features

- ✅ Upload Excel files (.xlsx, .xls) for data analysis
- ✅ Animated progress indicators during file upload
- ✅ Code interpreter integration for data analysis and visualization
- ✅ Automatic detection of agent-generated images/charts
- ✅ Download visualizations to current directory
- ✅ Conversational interface with full context retention
- ✅ Configuration externalization via `appsettings.json`

## How It Works

### 1. **Agent Setup**
```csharp
// Initialize the Persistent Agents Client
var persistentClient = new PersistentAgentsClient(projectEndpoint, credential);

// Try to get existing agent or create new one
var agents = persistentClient.Administration.GetAgents();
var agent = agents.FirstOrDefault(a => a.Name == agentName) 
    ?? persistentClient.Administration.CreateAgent(
        model: "gpt-4o",
        name: agentName,
        instructions: "You are a helpful data analysis assistant...",
        tools: new List<ToolDefinition> { new CodeInterpreterToolDefinition() }
    ).Value;
```
**Why**: Legacy SDK cannot access portal-created agents, so we create agents programmatically with code interpreter tool enabled.

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

### 4. **Agent Execution**
```csharp
// Create user message
persistentClient.Messages.CreateMessage(
    thread.Id,
    MessageRole.User,
    userRequest);

// Run the agent
var run = persistentClient.Runs.CreateRun(thread, agent).Value;

// Poll for completion
while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
{
    await Task.Delay(500);
    run = persistentClient.Runs.GetRun(thread.Id, run.Id).Value;
}
```
**Why**: Explicit run management gives us control over execution and status polling.

### 5. **Response Processing & File Download**
```csharp
// Get messages from the run
var messages = persistentClient.Messages.GetMessages(
    threadId: thread.Id,
    order: ListSortOrder.Descending);

foreach (var message in messages)
{
    if (message.Role == MessageRole.Agent && message.RunId == completedRun.Id)
    {
        foreach (var content in message.ContentItems)
        {
            if (content is MessageTextContent textContent)
            {
                Console.WriteLine(textContent.Text);
            }
            else if (content is MessageImageFileContent imageFileContent)
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
**Why**: Legacy SDK provides `Files.GetFileContent()` to download agent-generated files.

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
   - Sends to agent for processing
   - Shows progress dots during agent execution
   - Displays agent responses
   - Detects generated images/visualizations

5. **File Download** (if files generated)
   - Prompts: "X file(s) generated. Download to current directory? (y/n)"
   - Downloads files with timestamped names
   - Format: `agent_output_yyyyMMdd_HHmmss_<fileId>.png`

6. **Exit**
   - Type "exit" to end session

6. **Exit**
   - Type "exit" to end session

## Example Session

```
=== Code Interpreter Client ===

Setting up agent 'CodeInterpreterTest'...
✓ Connected to existing agent: CodeInterpreterTest
✓ Connected to agent: CodeInterpreterTest

Available Excel files in current directory:
===========================================
1. marketplace_data_50k.xlsx (2,1 MB)

Enter the number of the Excel file to upload (or press Enter to skip):
1

Uploading marketplace_data_50k.xlsx.......................... ✓ Done (2,1 MB)
   File ID: assistant-Rs2PN85aP5rHuP3U2VPxas

✓ Thread created with file attached

What would you like the code interpreter to do? (type 'exit' to quit)
> generate a bar chart comparing top 5 products by sales

=== Agent Response ===
.......................................................
Agent: 
[Generated image: assistant-Sxyt44nw8GuHCqcyL16cbs]
Here is the bar chart illustrating the top 5 products by total sales. According to the chart:

- **Laptop** stands out as the product with the highest sales.
- **CPU, Tablet, and Graphics Card** have relatively similar sales figures.
- **Smartphone** rounds out the top five with noticeably lower sales compared to the Laptop.

If you need further analysis or insights, feel free to ask!

1 file(s) generated. Download to current directory? (y/n)
> y
Downloading assistant-Sxyt44nw8GuHCqcyL16cbs ✓ Saved to: agent_output_20251229_180411_cyL16cbs.png

--- Ready for next request ---

What would you like the code interpreter to do? (type 'exit' to quit)
> exit

Exiting...

=== Session Complete ===
```

## Key AI Components

**Agent Creation**
```csharp
var persistentClient = new PersistentAgentsClient(projectEndpoint, credential);
var agent = persistentClient.Administration.CreateAgent(
    model: "gpt-4o",
    name: agentName,
    instructions: "You are a helpful data analysis assistant...",
    tools: new List<ToolDefinition> { new CodeInterpreterToolDefinition() }
).Value;
```
Creates an agent programmatically with code interpreter capabilities.

**Thread with File Attachment**
```csharp
var toolResources = new ToolResources
{
    CodeInterpreter = new CodeInterpreterToolResource()
};
toolResources.CodeInterpreter.FileIds.Add(uploadedFileId);
var thread = persistentClient.Threads.CreateThread(toolResources: toolResources);
```
**Critical**: Files must be attached during thread creation for agent access.

**Agent Execution**
```csharp
persistentClient.Messages.CreateMessage(thread.Id, MessageRole.User, userRequest);
var run = persistentClient.Runs.CreateRun(thread, agent).Value;

while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
{
    await Task.Delay(500);
    run = persistentClient.Runs.GetRun(thread.Id, run.Id).Value;
}
```
Sends user message and polls for completion.

**Response Processing**
```csharp
var messages = persistentClient.Messages.GetMessages(thread.Id);
foreach (var message in messages.Where(m => m.Role == MessageRole.Agent))
{
    foreach (var content in message.ContentItems)
    {
        if (content is MessageTextContent textContent)
            Console.WriteLine(textContent.Text);
        else if (content is MessageImageFileContent imageFileContent)
            // Download generated visualization
            var fileContent = persistentClient.Files.GetFileContent(imageFileContent.FileId).Value;
    }
}
```
Retrieves agent responses and detects generated images.

## Migration Path to Modern SDK

When `Microsoft.Agents.AI` adds file upload support:

1. **Replace Package**:
   ```xml
   <!-- Remove -->
   <PackageReference Include="Azure.AI.Agents.Persistent" Version="*-*" />
   
   <!-- Add -->
   <PackageReference Include="Microsoft.Agents.AI.AzureAI" Version="*-*" />
   ```

2. **Update Client**:
   ```csharp
   // Instead of PersistentAgentsClient
   var aiProjectClient = new AIProjectClient(new Uri(projectEndpoint), credential);
   AIAgent agent = aiProjectClient.GetAIAgent(name: agentName);
   ```

3. **Use Portal-Created Agents**:
   No need for programmatic agent creation—just retrieve by name.

4. **Watch for**: File upload/download APIs in future SDK versions.

## Relationship to Microsoft Agent Framework

```
Microsoft Agent Framework (MAF)
├── Hosting Options
│   ├── Azure Functions (Durable Agents)
│   ├── ASP.NET Core
│   └── Console Apps (this project)
├── SDK Versions
│   ├── Modern: Microsoft.Agents.AI (preview)
│   │   ├── Simplified API
│   │   └── Portal integration
│   └── Legacy: Azure.AI.Agents.Persistent (stable)
│       ├── Full API surface
│       └── File operations ← WE ARE HERE
└── Azure AI Foundry
    ├── Project endpoint
    ├── Model deployments
    └── Agent configurations
```

This project uses MAF patterns (agent instructions, tools, threads) via the legacy SDK to access file upload capabilities not yet available in the modern SDK.

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
