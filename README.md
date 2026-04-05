# LocalMind
> **Work in progress**

Local **RAG + ReAct agent** stack: [Ollama](https://ollama.com/) for chat and embeddings, [Qdrant](https://qdrant.tech/) for vector search, optional **SQLite** for structured queries, and a **.NET** agent loop (tool calls, structured JSON answers, semantic cache).



## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com/) running locally (default `http://localhost:11434`)
- [Docker](https://docs.docker.com/get-docker/) (for Qdrant via Compose)

Pull models you plan to use, for example:

```bash
ollama pull qwen3
ollama pull nomic-embed-text
```

Use a chat model that supports **tools** (see `ollama show <model> --modelfile`).

## Quick start

**1. Start Qdrant**

```bash
docker compose up -d
```

The stack exposes Qdrant on [http://localhost:6333](http://localhost:6333).

**2. Build the solution**

```bash
dotnet build LocalMind.sln
```

**3. Run the CLI** (placeholder entry point today)

```bash
dotnet run --project src/LocalMind.Cli/LocalMind.Cli.csproj
```

Wire up `LocalMind.Cli` with DI (`AddAgent`, `AddToolInfrastructure`, tools, Ollama client, Qdrant) when you are ready to drive the agent from the console or a host app.

## Solution layout

| Project | Role |
|--------|------|
| **LocalMind.Agent** | ReAct loop, Ollama chat, structured output parsing, traces, semantic cache integration |
| **LocalMind.Tools** | Tool registry, executor, manifests (e.g. knowledge search, SQL, calculator) |
| **LocalMind.Ingestion** | Chunk documents, embed with Ollama, upsert into Qdrant (`knowledge` collection) |
| **LocalMind.Cache** | Semantic cache support for the agent |
| **LocalMind.Cli** | Executable host (to be connected to services) |

## Flow Diagrams

### React Loop

![ReAct loop state machine](react_loop_state_machine.svg)

### Tool Executor Dispatch

![Tool executor dispatch flow](tool_executor_dispatch_flow.svg)

## Configuration

Bind **`Agent`** from configuration (see `AgentOptions`):

```json
{
  "Agent": {
    "ModelName": "qwen3",
    "MaxIterations": 8,
    "MaxOutputRetries": 3,
    "SemanticCacheThreshold": 0.92,
    "EnableSemanticCache": true
  }
}
```

Register with:

```csharp
services.AddAgent(builder.Configuration);
```

Requires the **`Microsoft.Extensions.Options.ConfigurationExtensions`** package (already referenced on the Agent project) so `Configure<AgentOptions>(IConfigurationSection)` resolves correctly.

## Docker Compose

`docker-compose.yml` runs **Qdrant** with a persistent volume. Optional **TimescaleDB** / **pgAdmin** blocks are present but commented out for later use.