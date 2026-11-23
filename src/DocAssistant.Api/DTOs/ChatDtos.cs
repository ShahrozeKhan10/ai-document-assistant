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
