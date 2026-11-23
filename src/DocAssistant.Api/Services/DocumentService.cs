using DocAssistant.Api.Data;
using DocAssistant.Api.DTOs;
using DocAssistant.Api.Models;
using DocAssistant.Api.Options;
using DocAssistant.Api.Services.Ai;
using DocAssistant.Api.Services.VectorStore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocAssistant.Api.Services;

public class DocumentService : IDocumentService
{
    private readonly AppDbContext _db;
    private readonly IPdfTextExtractor _extractor;
    private readonly ITextChunker _chunker;
    private readonly IEmbeddingService _embeddings;
    private readonly IVectorStore _vectors;
    private readonly ILogger<DocumentService> _logger;
    private readonly string _storageRoot;

    public DocumentService(
        AppDbContext db,
        IPdfTextExtractor extractor,
        ITextChunker chunker,
        IEmbeddingService embeddings,
        IVectorStore vectors,
        IOptions<StorageOptions> storage,
        IWebHostEnvironment env,
        ILogger<DocumentService> logger)
    {
        _db = db;
        _extractor = extractor;
        _chunker = chunker;
        _embeddings = embeddings;
        _vectors = vectors;
        _logger = logger;
        _storageRoot = Path.Combine(env.ContentRootPath, storage.Value.RootPath);
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<DocumentDetailDto> UploadAsync(IFormFile file, CancellationToken ct)
    {
        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = Path.GetFileName(file.FileName),
            SizeBytes = file.Length,
            Status = DocumentStatus.Processing
        };
        document.StoredFileName = $"{document.Id}.pdf";

        // 1) Keep a copy of the original file (needed later for re-processing).
        var path = Path.Combine(_storageRoot, document.StoredFileName);
        await using (var fs = File.Create(path))
        {
            await file.CopyToAsync(fs, ct);
        }

        // 2) Extract text page by page and split each page into chunks.
        try
        {
            await using var stream = File.OpenRead(path);
            var pages = _extractor.Extract(stream);
            document.PageCount = pages.Count;

            var index = 0;
            foreach (var page in pages)
            {
                foreach (var piece in _chunker.Chunk(page.Text))
                {
                    document.Chunks.Add(new DocumentChunk
                    {
                        Id = Guid.NewGuid(),
                        Index = index++,
                        PageNumber = page.PageNumber,
                        Content = piece,
                        CharCount = piece.Length
                    });
                }
            }

            if (document.Chunks.Count == 0)
            {
                document.Status = DocumentStatus.Failed;
                document.Error = "No text found. The PDF may be scanned images (OCR is not supported yet).";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process {FileName}", document.FileName);
            document.Status = DocumentStatus.Failed;
            document.Error = "Could not read this PDF. It may be corrupted or password-protected.";
            document.Chunks.Clear();
        }

        // 3) Embed the chunks and store the vectors in Qdrant.
        if (document.Status != DocumentStatus.Failed)
        {
            await TryIndexAsync(document, document.Chunks, ct);
        }

        // 4) Save the document and its chunks (kept even if indexing failed, so it can be re-indexed).
        _db.Documents.Add(document);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Uploaded {FileName}: {Pages} pages, {Chunks} chunks, status {Status}",
            document.FileName, document.PageCount, document.Chunks.Count, document.Status);

        return ToDetail(document, document.Chunks.Count);
    }

    public async Task<DocumentDetailDto?> ReindexAsync(Guid id, CancellationToken ct)
    {
        var document = await _db.Documents
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null) return null;

        if (document.Chunks.Count == 0)
        {
            document.Status = DocumentStatus.Failed;
            document.Error = "This document has no text chunks to index.";
        }
        else
        {
            // Remove old vectors first so we never keep duplicates.
            try { await _vectors.DeleteByDocumentAsync(id, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not delete old vectors for {Id}", id); }

            var ordered = document.Chunks.OrderBy(c => c.Index).ToList();
            await TryIndexAsync(document, ordered, ct);
        }

        await _db.SaveChangesAsync(ct);
        return ToDetail(document, document.Chunks.Count);
    }

    public async Task<IReadOnlyList<DocumentSummaryDto>> ListAsync(CancellationToken ct) =>
        await _db.Documents
            .AsNoTracking()
            .OrderByDescending(d => d.UploadedAtUtc)
            .Select(d => new DocumentSummaryDto(
                d.Id, d.FileName, d.SizeBytes, d.PageCount, d.Chunks.Count, d.Status, d.UploadedAtUtc))
            .ToListAsync(ct);

    public async Task<DocumentDetailDto?> GetAsync(Guid id, CancellationToken ct) =>
        await _db.Documents
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new DocumentDetailDto(
                d.Id, d.FileName, d.SizeBytes, d.PageCount, d.Chunks.Count, d.Status, d.Error, d.UploadedAtUtc))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ChunkDto>?> GetChunksAsync(Guid id, CancellationToken ct)
    {
        var exists = await _db.Documents.AnyAsync(d => d.Id == id, ct);
        if (!exists) return null;

        return await _db.DocumentChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == id)
            .OrderBy(c => c.Index)
            .Select(c => new ChunkDto(c.Index, c.PageNumber, c.CharCount, c.Content))
            .ToListAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null) return false;

        // Remove vectors first; if Qdrant is down we still delete from the database.
        try { await _vectors.DeleteByDocumentAsync(id, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete vectors for {Id}", id); }

        _db.Documents.Remove(document); // chunks are removed by cascade delete
        await _db.SaveChangesAsync(ct);

        var path = Path.Combine(_storageRoot, document.StoredFileName);
        if (File.Exists(path)) File.Delete(path);

        return true;
    }

    /// <summary>Embeds chunks and upserts them into Qdrant. Sets Ready or Failed on the document.</summary>
    private async Task TryIndexAsync(Document document, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct)
    {
        try
        {
            var vectors = await _embeddings.EmbedDocumentsAsync(chunks.Select(c => c.Content).ToList(), ct);

            var points = chunks.Select((c, i) => new VectorPoint(
                Id: c.Id,
                Vector: vectors[i],
                DocumentId: document.Id,
                ChunkIndex: c.Index,
                PageNumber: c.PageNumber,
                FileName: document.FileName,
                Text: c.Content)).ToList();

            await _vectors.UpsertAsync(points, ct);

            document.Status = DocumentStatus.Ready;
            document.Error = null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Indexing failed for {FileName}", document.FileName);
            document.Status = DocumentStatus.Failed;
            document.Error = ex is GeminiException
                ? $"Text was extracted but embedding failed: {ex.Message}"
                : "Text was extracted but Qdrant could not be reached. Start Docker, then call /reindex.";
        }
    }

    private static DocumentDetailDto ToDetail(Document d, int chunkCount) =>
        new(d.Id, d.FileName, d.SizeBytes, d.PageCount, chunkCount, d.Status, d.Error, d.UploadedAtUtc);
}
