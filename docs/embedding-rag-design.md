# AgentQ Embedding and RAG Design

## Goal

AgentQ should move beyond keyword-only search and use embedding-based retrieval to understand the meaning of project code and documentation.

The goals are:

- find only the code needed for the current task
- collect accurate context in large projects
- reduce LLM token usage
- reduce hallucination by grounding answers in retrieved evidence
- support semantic search for concepts that do not share exact keywords

## What Embeddings Do

An embedding converts text or code into a numeric vector.

```text
"login handling"
-> [0.123, -0.551, 0.992, ...]
```

Texts with similar meaning should be close to each other in vector space. For example, `login`, `signin`, `auth`, and `user authentication` can be retrieved together even when the exact words differ.

## Why AgentQ Needs This

Keyword search fails when the user's wording does not match the codebase.

Example user request:

```text
Find the member authentication flow.
```

The actual project might use:

- `auth.ts`
- `signin`
- `jwt validation`
- `session middleware`

Embedding search can find these related chunks by meaning, while grep-style search may miss them.

## Search Architecture

AgentQ should use hybrid search, not embedding search alone.

```text
Keyword Search
+ Embedding Search
+ Path and Recent File Priority
+ Project Map Signals
+ Reranking
```

The current desktop app already has keyword search, project map signals, evidence trail, confidence scoring, and search retry. Embedding/RAG should extend those systems instead of replacing them.

## Retrieval Flow

1. Project files are indexed.
2. Text/code files are split into chunks.
3. Each chunk is sent to an embedding model.
4. Vectors and metadata are stored in a local project cache.
5. The user's query is embedded.
6. Similar chunks are retrieved.
7. Results are reranked with project signals.
8. The highest-value chunks are added to model context.
9. Evidence is shown so the user can see why the context was selected.

## V1 Scope

V1 should be deliberately local and low-friction.

- Provider: OpenAI
- Model: `text-embedding-3-small`
- API key: reuse the existing OpenAI key when available
- Storage: `.agentq/embeddings/`
- Index mode: manual build first
- Re-indexing: changed files only, based on hash and modification time
- Vector storage: local JSON/JSONL cache or SQLite
- Search tool: `semantic_search`
- Ranking: vector similarity plus simple local boosts

V1 should avoid requiring users to install a separate vector database.

## Storage Layout

```text
.agentq/
  embeddings/
    index.json
    chunks.jsonl
```

This folder should not be committed.

```gitignore
.agentq/embeddings/
```

The repository ignores this path because vectors can be large, local to a machine, and derived from private project code. Shared retrieval behavior should be represented by source code and configuration, not by committing generated vectors.

Suggested chunk metadata:

- chunk id
- relative file path
- language or extension
- chunk text
- start line
- end line
- file hash
- file modified time
- embedding model
- vector
- created time

## Chunking Strategy

Chunking quality strongly affects search quality.

Recommended progression:

- V1: fixed-size text chunks with line numbers
- V1.5: simple code-aware chunking around functions/classes when easy to detect
- V2: AST-based symbol chunks
- V2+: import graph and dependency graph aware chunks

V1 should preserve enough file path and line-number metadata for evidence display.

## Ranking Strategy

Start with simple scoring before adding heavy reranking.

```text
finalScore =
  vectorSimilarity
  + keywordBoost
  + projectMapBoost
  + recentFileBoost
  + keyFileBoost
```

Possible boosts:

- current or recently changed files
- files in detected project roles such as UI, API, tests, database, or domain logic
- key files such as `README.md`, project files, and configuration
- exact keyword hits in the chunk or path

LLM reranking can be added later, but should not be required for V1.

## Evidence and Confidence

Every semantic result should be explainable.

Evidence examples:

```text
Semantic match: src/auth/session.ts
Reason: high similarity to "login failure"; path maps to Domain logic.
Score: 0.84
```

Confidence scoring should eventually use:

- number of retrieved chunks
- top similarity score
- spread between top results
- whether keyword and semantic search agree
- whether build/test verification ran
- whether answers cite file evidence

## Token Optimization

Embeddings cost tokens during indexing, but reduce repeated context cost.

The system should:

- index only text/code files
- skip generated, binary, dependency, build, and artifact folders
- chunk large files
- reuse existing vectors
- re-embed only changed files
- pass only the highest-value chunks to the LLM

## Provider Strategy

Recommended provider rollout:

- V1: OpenAI `text-embedding-3-small`
- V1.5: custom OpenAI-compatible embedding endpoint
- V2: local/Ollama embedding provider
- Later: Google, Anthropic-compatible options if practical

Chat model selection and embedding model selection should stay separate. Mixing chat models and embedding models in the same dropdown would make provider selection confusing.

## Future Improvements

- AST-based symbol indexing
- import graph analysis
- git recency and blame signals
- memory embedding search
- automatic fallback from grep/glob to semantic search
- confidence score v2 using semantic match scores
- evaluation dashboard for retrieval quality
- project map integration with semantic result explanations

## Target Agent Loop

The long-term AgentQ loop is:

```text
search
-> analyze
-> verify
-> modify
-> test
-> explain with evidence
```

Embedding/RAG is the retrieval layer that makes this loop reliable on larger projects.
