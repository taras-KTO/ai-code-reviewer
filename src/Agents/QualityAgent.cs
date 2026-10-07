using System.Text.Json;
using System.Text.Json.Serialization;
using AiCodeReviewer.Models;
using AiCodeReviewer.Services;
using Microsoft.Extensions.Logging;

namespace AiCodeReviewer.Agents;

public class QualityAgent
{
    private const string AgentName = "QualityAgent";

    private const string SystemPrompt = """
        You are a constructive code quality reviewer for C# source code. Your goal is to help the author improve the code, not to criticize style choices that don't matter. Ignore security concerns entirely (secrets, injection, input validation) — another reviewer covers those.

        Focus exclusively on:
        - Naming conventions (unclear, non-descriptive, or inconsistent identifier names such as single-letter variables or misleading names)
        - Method length and complexity (methods that do too much, are hard to follow, or run well beyond ~50 lines)
        - SOLID principle violations (especially Single Responsibility and Open/Closed violations)
        - Code duplication (repeated logic that should be extracted into a shared method or type)

        For every finding, suggest a concrete improvement — a short code example or a specific refactoring — not just a description of the problem. Be specific and actionable rather than vague: "rename `x` to `customerId`" is useful, "consider improving names" is not.

        Only report findings you are confident represent a genuine quality issue in the categories above. Do not comment on anything outside these categories (no security, no formatting/whitespace nitpicks). If the code is already in good shape for these categories, return an empty findings list.

        Respond with ONLY a single JSON object — no markdown code fences, no commentary before or after — in exactly this shape:
        {
          "summary": "one or two sentence overview of this code's overall quality",
          "findings": [
            {
              "description": "specific description of the issue plus a concrete suggested improvement or example, phrased constructively",
              "severity": "HIGH"
            }
          ]
        }

        Severity must be exactly one of "HIGH", "MEDIUM", or "LOW":
        - HIGH: significantly hurts maintainability or readability (e.g., a 200-line method mixing multiple responsibilities)
        - MEDIUM: a real but contained issue (e.g., one poorly named method, a single SOLID violation)
        - LOW: a minor polish opportunity (e.g., a variable name that could be clearer)
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ClaudeApiService _claudeApiService;
    private readonly ILogger<QualityAgent>? _logger;

    public QualityAgent(ClaudeApiService claudeApiService, ILogger<QualityAgent>? logger = null)
    {
        _claudeApiService = claudeApiService ?? throw new ArgumentNullException(nameof(claudeApiService));
        _logger = logger;
    }

    public async Task<AgentResponse> AnalyzeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        try
        {
            var responseText = await _claudeApiService
                .SendMessageAsync(SystemPrompt, code, cancellationToken)
                .ConfigureAwait(false);

            return ParseResponse(responseText);
        }
        catch (ClaudeApiException ex)
        {
            _logger?.LogError(ex, "{Agent} failed to analyze code", AgentName);
            return new AgentResponse
            {
                AgentName = AgentName,
                Summary = $"Quality analysis failed: {ex.Message}"
            };
        }
    }

    private AgentResponse ParseResponse(string responseText)
    {
        QualityAnalysisDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<QualityAnalysisDto>(StripCodeFences(responseText), JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "{Agent} returned a response that could not be parsed as JSON", AgentName);
            return new AgentResponse
            {
                AgentName = AgentName,
                Summary = "Quality analysis failed: Claude returned a response that could not be parsed as JSON."
            };
        }

        if (dto is null)
        {
            return new AgentResponse
            {
                AgentName = AgentName,
                Summary = "Quality analysis failed: Claude returned an empty response."
            };
        }

        var findings = (dto.Findings ?? [])
            .Select(f => new Finding
            {
                Description = f.Description ?? string.Empty,
                Severity = ParseSeverity(f.Severity)
            })
            .ToList();

        return new AgentResponse
        {
            AgentName = AgentName,
            Summary = dto.Summary ?? string.Empty,
            Findings = findings
        };
    }

    private Severity ParseSeverity(string? severity)
    {
        if (Enum.TryParse<Severity>(severity, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        _logger?.LogWarning("{Agent} received unrecognized severity '{Severity}', defaulting to Medium", AgentName, severity);
        return Severity.Medium;
    }

    private static string StripCodeFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline >= 0)
        {
            trimmed = trimmed[(firstNewline + 1)..];
        }

        var closingFenceIndex = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (closingFenceIndex >= 0)
        {
            trimmed = trimmed[..closingFenceIndex];
        }

        return trimmed.Trim();
    }

    private sealed class QualityAnalysisDto
    {
        [JsonPropertyName("summary")]
        public string? Summary { get; init; }

        [JsonPropertyName("findings")]
        public List<FindingDto>? Findings { get; init; }
    }

    private sealed class FindingDto
    {
        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("severity")]
        public string? Severity { get; init; }
    }
}
