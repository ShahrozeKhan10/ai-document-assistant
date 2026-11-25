namespace DocAssistant.Api.DTOs;

public record ChatRequest(
    string Question,
    List<Guid>? DocumentIds = null,
    int? TopK = null);

public record SourceDto(
    int Number,
    Guid DocumentId,
    string FileName,
    int PageNumber,
    double Score,
    string Snippet);

public record ChatResponse(
    string Answer,
    IReadOnlyList<SourceDto> Sources);

/// <summary>
/// One event of a streamed answer:
///   "delta"   = the next piece of answer text
///   "sources" = the sources the answer actually cited (sent once, at the end)
/// </summary>
public record ChatStreamEvent(
    string Type,
    string? Text = null,
    IReadOnlyList<SourceDto>? Sources = null);
