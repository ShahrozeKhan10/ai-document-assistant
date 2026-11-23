namespace DocAssistant.Api.Options;

public class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>How many chunks to retrieve and give to the model as sources.</summary>
    public int TopK { get; set; } = 5;

    /// <summary>Chunks with a lower cosine similarity than this are ignored (0 = keep all).</summary>
    public double MinScore { get; set; } = 0.0;
}
