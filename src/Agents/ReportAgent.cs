using System.Text;
using AiCodeReviewer.Models;
using Microsoft.Extensions.Logging;

namespace AiCodeReviewer.Agents;

public class ReportAgent
{
    private const string OutputFileName = "report.md";

    private readonly ILogger<ReportAgent>? _logger;

    public ReportAgent(ILogger<ReportAgent>? logger = null)
    {
        _logger = logger;
    }

    public async Task<string> GenerateReportAsync(AnalysisResult analysisResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysisResult);

        var markdown = BuildMarkdown(analysisResult);

        var outputPath = Path.Combine(Directory.GetCurrentDirectory(), OutputFileName);
        await File.WriteAllTextAsync(outputPath, markdown, cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("Report written to {OutputPath}", outputPath);

        return markdown;
    }

    private static string BuildMarkdown(AnalysisResult analysisResult)
    {
        var allFindings = analysisResult.AgentResponses
            .SelectMany(response => response.Findings.Select(finding => (Agent: response.AgentName, Finding: finding)))
            .ToList();

        var highCount = allFindings.Count(x => x.Finding.Severity == Severity.High);
        var mediumCount = allFindings.Count(x => x.Finding.Severity == Severity.Medium);
        var lowCount = allFindings.Count(x => x.Finding.Severity == Severity.Low);

        var sb = new StringBuilder();

        sb.AppendLine("# AI Code Review Report");
        sb.AppendLine();
        sb.AppendLine($"_Generated {analysisResult.Timestamp:yyyy-MM-dd HH:mm:ss} UTC_");
        sb.AppendLine();

        AppendExecutiveSummary(sb, analysisResult, allFindings.Count, highCount, mediumCount, lowCount);
        AppendFilesAnalyzed(sb, analysisResult.FilesAnalyzed);
        AppendFindingsBySeverity(sb, allFindings);
        AppendAgentSections(sb, analysisResult.AgentResponses);
        AppendRecommendations(sb, allFindings);

        return sb.ToString();
    }

    private static void AppendExecutiveSummary(
        StringBuilder sb,
        AnalysisResult analysisResult,
        int totalFindings,
        int highCount,
        int mediumCount,
        int lowCount)
    {
        sb.AppendLine("## Executive Summary");
        sb.AppendLine();
        sb.AppendLine(
            $"Analyzed **{analysisResult.FilesAnalyzed.Count}** file(s) across **{analysisResult.AgentResponses.Count}** agent(s), " +
            $"identifying **{totalFindings}** finding(s): **{highCount} High**, **{mediumCount} Medium**, **{lowCount} Low**.");
        sb.AppendLine();

        foreach (var response in analysisResult.AgentResponses)
        {
            if (!string.IsNullOrWhiteSpace(response.Summary))
            {
                sb.AppendLine($"- **{response.AgentName}**: {response.Summary}");
            }
        }

        sb.AppendLine();
    }

    private static void AppendFilesAnalyzed(StringBuilder sb, IReadOnlyList<string> filesAnalyzed)
    {
        sb.AppendLine("## Files Analyzed");
        sb.AppendLine();

        if (filesAnalyzed.Count == 0)
        {
            sb.AppendLine("No files analyzed.");
        }
        else
        {
            foreach (var file in filesAnalyzed)
            {
                sb.AppendLine($"- `{file}`");
            }
        }

        sb.AppendLine();
    }

    private static void AppendFindingsBySeverity(StringBuilder sb, IReadOnlyList<(string Agent, Finding Finding)> allFindings)
    {
        sb.AppendLine("## Findings by Severity");
        sb.AppendLine();

        AppendSeverityGroup(sb, "High", allFindings.Where(x => x.Finding.Severity == Severity.High));
        AppendSeverityGroup(sb, "Medium", allFindings.Where(x => x.Finding.Severity == Severity.Medium));
        AppendSeverityGroup(sb, "Low", allFindings.Where(x => x.Finding.Severity == Severity.Low));
    }

    private static void AppendSeverityGroup(StringBuilder sb, string label, IEnumerable<(string Agent, Finding Finding)> findings)
    {
        var list = findings.ToList();

        sb.AppendLine($"### {label}");
        sb.AppendLine();

        if (list.Count == 0)
        {
            sb.AppendLine("None.");
        }
        else
        {
            foreach (var (agent, finding) in list)
            {
                sb.AppendLine($"- **[{agent}]** {finding.Description}");
            }
        }

        sb.AppendLine();
    }

    private static void AppendAgentSections(StringBuilder sb, IReadOnlyList<AgentResponse> agentResponses)
    {
        sb.AppendLine("## Agent Reports");
        sb.AppendLine();

        foreach (var response in agentResponses)
        {
            sb.AppendLine($"### {response.AgentName}");
            sb.AppendLine();
            sb.AppendLine(string.IsNullOrWhiteSpace(response.Summary) ? "_No summary provided._" : response.Summary);
            sb.AppendLine();

            if (response.Findings.Count == 0)
            {
                sb.AppendLine("No findings.");
            }
            else
            {
                foreach (var finding in response.Findings.OrderByDescending(f => f.Severity))
                {
                    sb.AppendLine($"- **[{finding.Severity}]** {finding.Description}");
                }
            }

            sb.AppendLine();
        }
    }

    private static void AppendRecommendations(StringBuilder sb, IReadOnlyList<(string Agent, Finding Finding)> allFindings)
    {
        sb.AppendLine("## Recommendations");
        sb.AppendLine();

        var actionable = allFindings
            .Where(x => x.Finding.Severity is Severity.High or Severity.Medium)
            .OrderByDescending(x => x.Finding.Severity)
            .ToList();

        if (actionable.Count == 0)
        {
            sb.AppendLine("No High or Medium severity issues were found. Consider addressing any Low severity items as time allows.");
        }
        else
        {
            foreach (var (agent, finding) in actionable)
            {
                sb.AppendLine($"1. ({finding.Severity}, {agent}) {finding.Description}");
            }
        }
    }
}
