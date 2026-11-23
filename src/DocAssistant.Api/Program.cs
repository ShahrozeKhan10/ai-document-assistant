using System.Text.Json.Serialization;
using DocAssistant.Api.Data;
using DocAssistant.Api.Options;
using DocAssistant.Api.Services;
using DocAssistant.Api.Services.Ai;
using DocAssistant.Api.Services.VectorStore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ---------- Services ----------
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<ChunkingOptions>(builder.Configuration.GetSection(ChunkingOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<QdrantOptions>(builder.Configuration.GetSection(QdrantOptions.SectionName));
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName));

// Phase 1: ingestion
builder.Services.AddSingleton<IPdfTextExtractor, PdfTextExtractor>();
builder.Services.AddSingleton<ITextChunker, TextChunker>();
builder.Services.AddScoped<IDocumentService, DocumentService>();

// Phase 2: AI + vector search (typed HttpClients, one class per external service)
builder.Services.AddHttpClient<GeminiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(120);
});
builder.Services.AddTransient<IEmbeddingService>(sp => sp.GetRequiredService<GeminiClient>());
builder.Services.AddTransient<IChatModel>(sp => sp.GetRequiredService<GeminiClient>());

builder.Services.AddHttpClient<QdrantVectorStore>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
    client.BaseAddress = new Uri(options.Url);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddTransient<IVectorStore>(sp => sp.GetRequiredService<QdrantVectorStore>());

builder.Services.AddScoped<IRagService, RagService>();

// The React app (Phase 3) will run on Vite's default port.
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

// Create the Qdrant collection at startup if Qdrant is already running.
// If it is not, the app still starts and will retry on the first upload or question.
using (var scope = app.Services.CreateScope())
{
    try
    {
        await scope.ServiceProvider.GetRequiredService<IVectorStore>().EnsureCollectionAsync(CancellationToken.None);
        app.Logger.LogInformation("Qdrant is ready");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning("Qdrant not reachable at startup ({Message}). Run: docker compose up -d", ex.Message);
    }

    var gemini = scope.ServiceProvider.GetRequiredService<IOptions<GeminiOptions>>().Value;
    if (string.IsNullOrWhiteSpace(gemini.ApiKey))
        app.Logger.LogWarning("Gemini API key is not set. Run set-gemini-key.bat");
}

// ---------- Pipeline ----------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.MapControllers();

app.Run();
