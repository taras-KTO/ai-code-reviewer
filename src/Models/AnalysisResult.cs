namespace AiCodeReviewer.Models;

public class AnalysisResult
{
    public List<AgentResponse> AgentResponses { get; set; } = [];
    public List<string> FilesAnalyzed { get; set; } = [];
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
