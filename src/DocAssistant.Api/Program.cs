using System.Text;
using System.Text.Json.Serialization;
using DocAssistant.Api.Data;
using DocAssistant.Api.Models;
using DocAssistant.Api.Options;
using DocAssistant.Api.Services;
using DocAssistant.Api.Services.Ai;
using DocAssistant.Api.Services.Auth;
using DocAssistant.Api.Services.VectorStore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------- Services ----------
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Adds the "Authorize" button to Swagger so you can paste a token and call protected endpoints.
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the token from /api/auth/login"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<ChunkingOptions>(builder.Configuration.GetSection(ChunkingOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<QdrantOptions>(builder.Configuration.GetSection(QdrantOptions.SectionName));
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

// Phase 4: authentication (JWT bearer tokens)
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key is missing or shorter than 32 characters. Run setup-phase4.bat (local) or set JWT_KEY in .env (Docker).");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep claim names as in the token ("sub", "email")
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "email"
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Phase 1: ingestion
builder.Services.AddSingleton<IPdfTextExtractor, PdfTextExtractor>();
builder.Services.AddSingleton<ITextChunker, TextChunker>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<IDocumentService>(sp => sp.GetRequiredService<DocumentService>());
builder.Services.AddScoped<IDocumentScope>(sp => sp.GetRequiredService<DocumentService>());

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

// The React dev server runs on Vite's default port (it also proxies /api, so this is a fallback).
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

// ---------- Startup tasks ----------
using (var scope = app.Services.CreateScope())
{
    // In Docker, SQL Server starts slower than the API, so apply migrations with a few retries.
    if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                app.Logger.LogInformation("Database is up to date");
                break;
            }
            catch (Exception ex) when (attempt < 12)
            {
                app.Logger.LogWarning("Database not ready (attempt {Attempt}): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    // Create the Qdrant collection if Qdrant is already running; otherwise it is created on first use.
    try
    {
        await scope.ServiceProvider.GetRequiredService<IVectorStore>().EnsureCollectionAsync(CancellationToken.None);
        app.Logger.LogInformation("Qdrant is ready");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning("Qdrant not reachable at startup ({Message}). Run: docker compose up -d qdrant", ex.Message);
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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Lets the test project reference the Program type if needed.
public partial class Program { }
