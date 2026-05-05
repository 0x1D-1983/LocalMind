# LocalMind

> **Work in progress**

Local **RAG + ReAct agent** stack: [Ollama](https://ollama.com/) for chat and embeddings, [Qdrant](https://qdrant.tech/) for vector search, optional **SQLite** tooling for structured queries, and a **.NET** agent loop (tool calls, structured JSON answers, tracing). A **semantic cache** stores successful `AgentResponse` payloads in a dedicated Qdrant collection keyed by embedding similarity (optional, on by default). A **query rewriter** expands follow-up questions using recent turns so cache lookup uses a self-contained query.

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

Compose maps **REST** to [http://localhost:6333](http://localhost:6333) and **gRPC** to port **6334**. The Qdrant .NET client uses gRPC, so `appsettings.json` should use port `6334` (see the sample configs under each app).

**2. Build the solution**

```bash
dotnet build LocalMind.sln
```

**3. Ingest a document** (creates the collection if needed, then chunks and embeds)

```bash
dotnet run --project src/LocalMind.IngestConsoleApp/LocalMind.IngestConsoleApp.csproj -- -d /path/to/your.md
```

Configuration is loaded from `src/LocalMind.IngestConsoleApp/appsettings.json` (`Serilog`, `KnowledgeBase`, `DocumentIngest`, `Ollama`, `Qdrant`).

**4. Run the knowledge chat console**

```bash
dotnet run --project src/LocalMind.KnowledgeChatBot/LocalMind.KnowledgeChatBot.csproj
```

The chat app builds a generic `IHost` and calls **`StartAsync`** so hosted services run (including startup that ensures the semantic-cache Qdrant collection exists). Type questions at the prompt; use `exit` or `quit` to leave. Ensure the chat model in `Agent:ModelName` matches a pulled Ollama tag (for example `qwen3:8b`), and that the Qdrant **knowledge** collection name matches what you used at ingest time (default `knowledge`). The semantic cache uses a separate collection (default `semantic_cache`); align `SemanticCache` options with your embedding model and vector width.

## Solution layout

| Project | Role |
|--------|------|
| **LocalMind.KnowledgeChatBot** | Console host: Serilog, `IHost` startup, agent, knowledge search tool, Ollama + Qdrant |
| **LocalMind.IngestConsoleApp** | CLI to ingest a single file into Qdrant (chunk + embed + upsert) |
| **LocalMind.Agent** | ReAct loop, structured JSON output parsing, traces, conversation store, query rewriter, semantic cache integration |
| **LocalMind.Cache** | `SemanticCache<T>`, options, hosted initializer (ensure Qdrant collection) |
| **LocalMind.Tools** | Tool registry, executor, manifests (`search_knowledge_base`, `calculate`, `query_database` stub, …) |
| **LocalMind.Ingestion** | Document chunking, embedding via Ollama, Qdrant upsert; `KnowledgeBaseOptions` + `DocumentIngestOptions` |
| **LocalMind.Ollama** | `OllamaApiClient` + `OllamaApiClientOptions` DI |
| **LocalMind.Qdrant** | `QdrantClient` + `QdrantClientOptions` DI |
| **LocalMind.Telemetry** | Prometheus metric server hosted service + agent LLM call metrics |

## Flow diagrams

### ReAct loop

![ReAct loop state machine](react_loop_state_machine.svg)

### Tool executor dispatch

![Tool executor dispatch flow](tool_executor_dispatch_flow.svg)

## Configuration

Each executable has its own `appsettings.json`. Common sections:

**Agent** (`AgentOptions` — see `src/LocalMind.KnowledgeChatBot/appsettings.json`):

```json
{
  "Agent": {
    "ModelName": "qwen3:8b",
    "MaxIterations": 8,
    "MaxOutputRetries": 3,
    "MaxConversationTurns": 20
  }
}
```

Register in DI with `services.AddAgent(configuration)` in `src/LocalMind.Agent/AgentExtensions.cs`. Keys omitted from JSON use defaults from `AgentOptions` (for example `MaxConversationTurns` defaults to `20`).

**Metrics** (`PrometheusMetricServerOptions` — chat host registers `AddPrometheusMetricServer`):

```json
{
  "Metrics": {
    "Port": 9091
  }
}
```

Metrics are exposed from the chat process and scraped by Prometheus. Current agent metrics:

- `localmind_agent_llm_calls_total` (labels: `model`, `cache_hit`)
- `localmind_agent_llm_tokens_total` (labels: `model`, `token_type`)
- `localmind_agent_llm_duration_ms` (labels: `model`, `cache_hit`)
- `localmind_agent_llm_tool_calls_requested` (label: `model`)
- `localmind_agent_llm_iteration` (label: `model`)

**Semantic cache** (`SemanticCacheOptions` — chat host registers `AddSemanticCacheOptions`):

```json
{
  "SemanticCache": {
    "Enabled": true,
    "CollectionName": "semantic_cache",
    "EmbeddingModel": "nomic-embed-text",
    "VectorSize": 768,
    "SimilarityThreshold": 0.92
  }
}
```

When enabled, the agent embeds the (rewritten) user query, searches this collection above `SimilarityThreshold`, and on a miss stores the final structured response after a successful run. The collection is created on startup if missing (same vector size and distance as configured here).

**Query rewriter** (`QueryRewriterOptions` — bound in `AddAgent`):

```json
{
  "QueryRewriter": {
    "Model": "qwen3:8b"
  }
}
```

Used before the cache lookup when there is conversation history: the model rewrites the user message into a standalone question so pronouns and ellipses still match sensible cache keys.

**Knowledge base / vector index** (`KnowledgeBaseOptions` — used by ingest and `search_knowledge_base`):

```json
{
  "KnowledgeBase": {
    "CollectionName": "knowledge",
    "EmbeddingModel": "nomic-embed-text",
    "VectorSize": 768
  }
}
```

**Document ingest** (`DocumentIngestOptions` — ingest console only; chunking plus batch sizes for embed/upsert):

```json
{
  "DocumentIngest": {
    "ChunkSize": 2000,
    "Overlap": 300,
    "EmbeddingBatchSize": 16,
    "UpsertBatchSize": 32
  }
}
```

The chat host binds **`KnowledgeBase`** and **`SemanticCache`** options; the ingest app binds **`KnowledgeBase`** and **`DocumentIngest`**. `EmbeddingModel` and `VectorSize` define the index contract for the knowledge collection (ingest and search must agree). The semantic cache uses the **`SemanticCache`** section’s model and dimensions for its own collection.

**Ollama** (`OllamaApiClientOptions`) and **Qdrant** (`QdrantClientOptions`): host, port, API key, timeouts — see sample `appsettings.json` files in each project.

### Options packages (class libraries)

If you call `Configure<T>(IConfiguration)` or `OptionsBuilder.Bind(IConfiguration)` from a library project, reference **`Microsoft.Extensions.Options.ConfigurationExtensions`**. To enforce **`[Required]`** / **`[Range]`** at startup, add **`Microsoft.Extensions.Options.DataAnnotations`** and use `.ValidateDataAnnotations().ValidateOnStart()` on the options builder (the ingest project does this for `KnowledgeBaseOptions` and `DocumentIngestOptions`).

## Docker Compose

`docker-compose.yml` runs **Qdrant**, **Prometheus**, **Loki**, and **Grafana** with persistent volumes where configured. Optional **TimescaleDB** / **pgAdmin** blocks are commented out for later use.

For metrics, `prometheus.yml` includes:

- Scrape job: `localmind-knowledge-chat-bot`
- Target: `host.docker.internal:9091`

Grafana dashboard JSON for the agent metrics is available at `localmind-agent-metrics-dashboard.json`.
Import it via **Dashboards → New → Import** and select the `Prometheus` datasource.
