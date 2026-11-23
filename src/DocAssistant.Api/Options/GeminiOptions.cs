namespace DocAssistant.Api.Options;

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>Set with user-secrets (set-gemini-key.bat). Never commit the key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";

    public string EmbeddingModel { get; set; } = "gemini-embedding-001";

    /// <summary>gemini-embedding-001 returns 3072 values by default; 768 is smaller and almost as good.</summary>
    public int EmbeddingDimensions { get; set; } = 768;

    /// <summary>How many chunks to embed in one API call.</summary>
    public int EmbeddingBatchSize { get; set; } = 50;

    public string ChatModel { get; set; } = "gemini-3.6-flash";
}
