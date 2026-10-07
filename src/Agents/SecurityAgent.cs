using System.Text.Json;
using System.Text.Json.Serialization;
using AiCodeReviewer.Models;
using AiCodeReviewer.Services;
using Microsoft.Extensions.Logging;

namespace AiCodeReviewer.Agents;

public class SecurityAgent
{
    private const string AgentName = "SecurityAgent";

    private const string SystemPrompt = """
        You are a strict application security auditor reviewing C# source code for real, exploitable vulnerabilities. You are not a style or quality checker — ignore naming, formatting, SOLID principles, and other non-security concerns entirely.

        Focus exclusively on:
        - Hardcoded secrets (API keys, passwords, connection strings, tokens embedded directly in source)
        - SQL injection risks (string-concatenated or interpolated SQL, unparameterized ADO.NET/Dapper/EF raw queries)
        - Missing input validation (unchecked or unsanitized input reaching sensitive operations)
        - Insecure or outdated dependencies referenced in the code

        Only report findings you are confident represent a real vulnerability in the categories above. Do not speculate, do not pad the list with theoretical or stylistic issues, and do not comment on anything outside these categories. If the code has no genuine issues in these categories, return an empty findings list.

        Respond with ONLY a single JSON object — no markdown code fences, no commentary before or after — in exactly this shape:
        {
          "summary": "one or two sentence overview of this code's security posture",
          "findings": [
            {
              "description": "specific description of the vulnerability: what it is, why it matters, and where it occurs",
              "severity": "HIGH"
            }
          ]
        }

        Severity must be exactly one of "HIGH", "MEDIUM", or "LOW":
        - HIGH: directly exploitable (e.g., SQL injection, hardcoded production credentials)
        - MEDIUM: a real weakness that needs another condition to exploit (e.g., missing validation on an internal-only path)
        - LOW: a minor hardening opportunity, unlikely to be directly exploitable
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ClaudeApiService _claudeApiService;
    private readonly ILogger<SecurityAgent>? _logger;

    public SecurityAgent(ClaudeApiService claudeApiService, ILogger<SecurityAgent>? logger = null)
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
                Summary = $"Security analysis failed: {ex.Message}"
            };
        }
    }

    private AgentResponse ParseResponse(string responseText)
    {
        SecurityAnalysisDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SecurityAnalysisDto>(StripCodeFences(responseText), JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "{Agent} returned a response that could not be parsed as JSON", AgentName);
            return new AgentResponse
            {
                AgentName = AgentName,
                Summary = "Security analysis failed: Claude returned a response that could not be parsed as JSON."
            };
        }

        if (dto is null)
        {
            return new AgentResponse
            {
                AgentName = AgentName,
                Summary = "Security analysis failed: Claude returned an empty response."
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

    private sealed class SecurityAnalysisDto
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
