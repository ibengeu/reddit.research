# reddit.research

A lean RAG system that crawls Reddit via the [Arctic Shift](https://arctic-shift.photon-reddit.com) API, embeds content with Ollama, stores vectors in PostgreSQL/pgvector, and lets you ask questions against your own dataset through a Blazor web UI. No Reddit API credentials required.

## How it works

```
Arctic Shift API → Crawl & chunk → Ollama (nomic-embed-text) → pgvector
                                                                    ↓
                                          Question → embed → similarity search
                                                                    ↓
                                              OpenRouter / Ollama (chat) → answer
```

## Stack

- **Crawler** — .NET console app, fetches posts + comment trees from Arctic Shift
- **Embeddings** — Ollama running `nomic-embed-text` locally
- **Vector store** — PostgreSQL with pgvector extension
- **Chat** — OpenRouter (cloud) or Ollama (local) for answer synthesis
- **UI** — Blazor Server with Tailwind CSS

## Quick start

**Prerequisites:** Docker + Docker Compose

```bash
# Copy and fill in your OpenRouter key
cp .env.example .env

# Start everything (Ollama, Postgres, web UI)
docker compose up

# Open the UI
open http://localhost:8080
```

Navigate to **Ingestion Jobs** to crawl a subreddit, then go to **New Chat** to ask questions.

## Configuration

Key environment variables (set in `.env` or `docker-compose.yaml`):

| Variable | Default | Description |
|---|---|---|
| `OpenRouter__ApiKey` | — | Your OpenRouter API key |
| `OpenRouter__Model` | `deepseek/deepseek-chat` | Chat model via OpenRouter |
| `Rag__ChatProvider` | `OpenRouter` | `OpenRouter` or `Ollama` |
| `Rag__TopK` | `5` | Chunks retrieved per query |
| `Crawler__OutputPath` | `./output` | JSONL archive directory |

## Local development

```bash
# .NET 10 SDK required
dotnet restore
dotnet build

# Run tests
dotnet test

# Start dependencies only
docker compose up ollama postgres
dotnet run --project src/RedditCrawler.Web
```

## Project structure

```
src/
  RedditCrawler.Core/       Crawler, embeddings, vector store, chat services
  RedditCrawler.Web/        Blazor Server UI (pages: Ask, Browse, Crawl, Settings)
tests/
  RedditCrawler.Tests/      xUnit tests
docker-compose.yaml         Local development stack
docker-compose.server.yaml  Production/server deployment
```
