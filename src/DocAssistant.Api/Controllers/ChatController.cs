using System.Text.Json;
using System.Text.Json.Serialization;
using DocAssistant.Api.DTOs;
using DocAssistant.Api.Extensions;
using DocAssistant.Api.Services;
using DocAssistant.Api.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace DocAssistant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private const string QdrantDownMessage = "Could not reach Qdrant. Is Docker running? Try: docker compose up -d";

    private static readonly JsonSerializerOptions SseJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IRagService _rag;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IRagService rag, ILogger<ChatController> logger)
    {
        _rag = rag;
        _logger = logger;
    }

    /// <summary>
    /// Ask a question about your documents. Leave documentIds empty to search all documents.
    /// The answer cites sources like [1]; "sources" lists only the ones it cited.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ask([FromBody] ChatRequest request, CancellationToken ct)
    {
        var problem = Validate(request);
        if (problem is not null) return Problem(problem, statusCode: 400);

        try
        {
            return Ok(await _rag.AskAsync(request, User.GetUserId(), ct));
        }
        catch (GeminiException ex)
        {
            _logger.LogError(ex, "Gemini call failed");
            return Problem(ex.Message, statusCode: 502, title: "AI service error");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Vector store call failed");
            return Problem(QdrantDownMessage, statusCode: 503, title: "Vector database unavailable");
        }
    }

    /// <summary>
    /// Same as POST /api/chat, but streams the answer as Server-Sent Events:
    ///   event: delta    data: {"text":"..."}
    ///   event: sources  data: {"sources":[...]}
    ///   event: error    data: {"message":"..."}
    ///   event: done     data: {}
    /// </summary>
    [HttpPost("stream")]
    public async Task Stream([FromBody] ChatRequest request, CancellationToken ct)
    {
        var problem = Validate(request);
        if (problem is not null)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new ProblemDetails { Title = "Bad request", Detail = problem, Status = 400 }, ct);
            return;
        }

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            await foreach (var ev in _rag.AskStreamAsync(request, User.GetUserId(), ct))
            {
                if (ev.Type == "delta")
                    await WriteEventAsync("delta", new { text = ev.Text }, ct);
                else
                    await WriteEventAsync("sources", new { sources = ev.Sources }, ct);
            }
            await WriteEventAsync("done", new { }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The user pressed Stop or closed the tab: nothing to send.
        }
        catch (GeminiException ex)
        {
            _logger.LogError(ex, "Gemini call failed while streaming");
            await WriteEventAsync("error", new { message = ex.Message }, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Vector store call failed while streaming");
            await WriteEventAsync("error", new { message = QdrantDownMessage }, ct);
        }
    }

    private async Task WriteEventAsync(string type, object data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, data.GetType(), SseJson);
        await Response.WriteAsync($"event: {type}\ndata: {json}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private static string? Validate(ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question)) return "Please enter a question.";
        if (request.Question.Length > 2000) return "Question is too long (max 2000 characters).";
        return null;
    }
}
