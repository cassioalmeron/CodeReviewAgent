# Code Review Agent

An agent for automated code reviewing, powered by an LLM.

> Developed as an activity for the **Rambo EPC method**, specifically **Pilar 3**.

> **Status: early work in progress.** Currently the project only implements the
> LLM request layer — a small abstraction that sends a prompt to either a local
> (Ollama) or hosted (Claude) model and logs the response, token usage, and
> latency. The code-review logic is not built yet.

## Tech stack

- .NET 10 console application
- Raw `HttpClient` calls to the LLM APIs (no SDK)
- Pluggable LLM engines behind a single `ILlmClient` interface

## Project layout

```
src/CodeReviewerAgent/CodeReviewerAgent.Console/
├── Program.cs            # Entry point: builds the request, logs output/tokens/latency
├── ILlmServer.cs         # ILlmClient interface + AnthropicClient and OllamaClient
├── LlmClientFactory.cs   # Picks the client based on LLM_ENGINE
└── EnvLoader.cs          # Minimal .env file loader
```

## Configuration

Configuration is read from a `.env` file (gitignored). Copy `.env.example` and
fill in your values:

```
# Which engine to use: ollama | claude
LLM_ENGINE=ollama

# Claude (used when LLM_ENGINE=claude)
ANTHROPIC_API_KEY=your-key-here
ANTHROPIC_MODEL=claude-opus-4-8

# Ollama (used when LLM_ENGINE=ollama)
OLLAMA_HOST=http://localhost:11434
OLLAMA_MODEL=qwen2.5-coder:7b
```

> The API key is never committed — keep it only in your local `.env`.

## Running

```bash
cd src/CodeReviewerAgent/CodeReviewerAgent.Console
dotnet run
```

Example output:

```
The capital of France is Paris.
[tokens] input: 36, output: 8, total: 44
[latency] 312 ms
```

## How it works

`LlmClientFactory.Create()` reads `LLM_ENGINE` and returns the matching
`ILlmClient` implementation:

- **`AnthropicClient`** — POSTs to the Claude Messages API, using `ANTHROPIC_MODEL`.
- **`OllamaClient`** — POSTs to a local Ollama `/api/chat` endpoint, using
  `OLLAMA_MODEL`, and maps the response back to a common shape.

Both return a shared `MessageResponse`, so the caller logs the text, token
usage, and latency the same way regardless of engine.

### Adding another engine

1. Implement `ILlmClient`.
2. Add a case for it in `LlmClientFactory`.
3. Document its environment variables in `.env.example`.

## Roadmap

- [ ] Feed source files / diffs to the model instead of a hardcoded prompt
- [ ] Prompt templates tailored for code review
- [ ] Structured review output (issues, severity, suggestions)
- [ ] Integration with git / pull requests
```

