# AI Document Assistant

Upload PDFs and ask questions about them. Answers come with citations to the page they came from.

Built with **ASP.NET Core 8 Web API**, **Entity Framework Core + SQL Server**, **Qdrant** (vector search), **Google Gemini**, and a **React + TypeScript** frontend.

> Status: in progress. Phases 1–3 (ingestion, RAG, React chat UI) are done.

## Roadmap

- [x] **Phase 1:** PDF upload, page-by-page text extraction, overlapping chunking, SQL Server storage
- [x] **Phase 2:** embeddings + Qdrant, question answering with Gemini, source citations
- [x] **Phase 3:** React chat UI with streaming answers and clickable citations
- [ ] **Phase 4:** JWT auth, Docker Compose, GitHub Actions CI, live deployment

## How it works

**Ingestion (upload)**
```
PDF ──► PdfPig extracts text page by page
    ──► TextChunker: ~1000-char chunks, 200-char overlap, cut at sentence/word boundaries
    ──► Gemini gemini-embedding-001 turns each chunk into a 768-dim vector (RETRIEVAL_DOCUMENT)
    ──► Qdrant stores vector + payload (document id, page, file name, text)
    ──► SQL Server stores the document and its chunks
```

**Question answering (RAG)**
```
Question ──► embed (RETRIEVAL_QUERY) ──► Qdrant cosine search, top-k chunks
         ──► prompt Gemini with the chunks as numbered sources [1]..[k]
         ──► answer that cites [n], streamed to the browser piece by piece (Server-Sent Events)
         ──► only the sources the answer actually cited are returned
```

The model is told to answer only from the sources and to say when the answer is not in the documents.

## Project structure

```
src/DocAssistant.Api/
├── Controllers/        DocumentsController, ChatController (HTTP only)
├── Services/           DocumentService (ingestion), RagService (retrieve + generate)
│   ├── Ai/             GeminiClient: embeddings + generation over REST, with retry on 429/5xx
│   └── VectorStore/    QdrantVectorStore: collection setup, upsert, filtered search, delete
├── Data/               AppDbContext (EF Core)
├── Models/             Document, DocumentChunk
├── DTOs/               API request/response shapes
└── Options/            Strongly typed settings (Gemini, Qdrant, Rag, Chunking, Storage)

web/                    React 18 + TypeScript + Vite
├── src/api.ts          fetch calls + a small SSE parser for the streaming endpoint
├── src/components/     DocumentPanel (upload, list, scope), ChatPanel, MessageView (markdown + citations)
└── vite.config.ts      dev proxy: /api → http://localhost:5080
```

## Run locally

Requirements: .NET 8 SDK, SQL Server (Express/LocalDB is fine), Docker Desktop, a free Gemini API key from Google AI Studio.

1. Set `ConnectionStrings:Default` in `appsettings.json`, for example `Server=.\\SQLEXPRESS;...`.
2. Start Qdrant: `docker compose up -d`
3. Store the Gemini key outside the repo:
   ```bash
   cd src/DocAssistant.Api
   dotnet user-secrets set "Gemini:ApiKey" "<your key>"
   ```
4. Create the database and run:
   ```bash
   dotnet tool install --global dotnet-ef --version 8.0.11   # once
   dotnet ef database update
   dotnet run --launch-profile http
   ```

Swagger opens at `http://localhost:5080/swagger`; the Qdrant dashboard is at `http://localhost:6333/dashboard`.
5. Start the web app (Node.js 18+):
   ```bash
   cd web
   npm install
   npm run dev
   ```
   Open `http://localhost:5173`.

On Windows, `set-gemini-key.bat`, `run.bat` (API) and `web.bat` (web app) do these steps for you.

## API

| Method | Route | What it does |
| --- | --- | --- |
| POST | `/api/documents` | Upload a PDF (form field `file`); it is chunked, embedded and indexed |
| GET | `/api/documents` | List documents |
| GET | `/api/documents/{id}` | Document details |
| GET | `/api/documents/{id}/chunks` | Chunks the document was split into |
| POST | `/api/documents/{id}/reindex` | Re-create a document's embeddings |
| DELETE | `/api/documents/{id}` | Delete document, chunks, vectors and stored file |
| POST | `/api/chat` | Ask a question: `{ "question": "...", "documentIds": [], "topK": 5 }` |
| POST | `/api/chat/stream` | Same, streamed as Server-Sent Events: `delta`, `sources`, `error`, `done` |

Example answer:

```json
{
  "answer": "The warranty covers parts for 24 months [1], but not labour [2].",
  "sources": [
    { "number": 1, "fileName": "manual.pdf", "pageNumber": 4, "score": 0.81, "snippet": "..." },
    { "number": 2, "fileName": "manual.pdf", "pageNumber": 5, "score": 0.77, "snippet": "..." }
  ]
}
```
