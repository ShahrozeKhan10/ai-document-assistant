using DocAssistant.Api.DTOs;
using DocAssistant.Api.Services;
using DocAssistant.Api.Services.Ai;
using Microsoft.AspNetCore.Mvc;

namespace DocAssistant.Api.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly IRagService _rag;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IRagService rag, ILogger<ChatController> logger)
    {
        _rag = rag;
        _logger = logger;
    }

    /// <summary>
    /// Ask a question about your documents. Leave documentIds empty to search all documents.
    /// The answer cites sources like [1], matching the numbers in "sources".
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ask([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return Problem("Please enter a question.", statusCode: 400);

        if (request.Question.Length > 2000)
            return Problem("Question is too long (max 2000 characters).", statusCode: 400);

        try
        {
            return Ok(await _rag.AskAsync(request, ct));
        }
        catch (GeminiException ex)
        {
            _logger.LogError(ex, "Gemini call failed");
            return Problem(ex.Message, statusCode: 502, title: "AI service error");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Vector store call failed");
            return Problem("Could not reach Qdrant. Is Docker running? Try: docker compose up -d",
                statusCode: 503, title: "Vector database unavailable");
        }
    }
}
