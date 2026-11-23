namespace DocAssistant.Api.Options;

public class QdrantOptions
{
    public const string SectionName = "Qdrant";

    /// <summary>Qdrant REST endpoint (docker-compose maps it to port 6333).</summary>
    public string Url { get; set; } = "http://localhost:6333/";

    public string Collection { get; set; } = "document_chunks";
}
