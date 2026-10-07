namespace AiCodeReviewer.Models;

public class AgentResponse
{
    public string AgentName { get; set; } = string.Empty;
    public List<Finding> Findings { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
}

public class Finding
{
    public string Description { get; set; } = string.Empty;
    public Severity Severity { get; set; }
}

public enum Severity
{
    Low,
    Medium,
    High
}
