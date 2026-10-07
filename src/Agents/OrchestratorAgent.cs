using System.Text;
using AiCodeReviewer.Models;
using Microsoft.Extensions.Logging;

namespace AiCodeReviewer.Agents;

public class OrchestratorAgent
{
    private static readonly string BinSegment = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
    private static readonly string ObjSegment = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";

    private readonly SecurityAgent _securityAgent;
    private readonly QualityAgent _qualityAgent;
    private readonly ReportAgent _reportAgent;
    private readonly ILogger<OrchestratorAgent>? _logger;

    public OrchestratorAgent(
        SecurityAgent securityAgent,
        QualityAgent qualityAgent,
        ReportAgent reportAgent,
        ILogger<OrchestratorAgent>? logger = null)
    {
        _securityAgent = securityAgent ?? throw new ArgumentNullException(nameof(securityAgent));
        _qualityAgent = qualityAgent ?? throw new ArgumentNullException(nameof(qualityAgent));
        _reportAgent = reportAgent ?? throw new ArgumentNullException(nameof(reportAgent));
        _logger = logger;
    }

    public async Task<string> RunAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var filePaths = ResolveCSharpFiles(path);
        _logger?.LogInformation("Analyzing {FileCount} C# file(s) under '{Path}'", filePaths.Count, path);

        var combinedSource = await BuildCombinedSourceAsync(filePaths, cancellationToken).ConfigureAwait(false);

        var securityTask = _securityAgent.AnalyzeAsync(combinedSource, cancellationToken);
        var qualityTask = _qualityAgent.AnalyzeAsync(combinedSource, cancellationToken);

        await Task.WhenAll(securityTask, qualityTask).ConfigureAwait(false);

        var analysisResult = new AnalysisResult
        {
            AgentResponses = [securityTask.Result, qualityTask.Result],
            FilesAnalyzed = filePaths,
            Timestamp = DateTime.UtcNow
        };

        return await _reportAgent.GenerateReportAsync(analysisResult, cancellationToken).ConfigureAwait(false);
    }

    private static List<string> ResolveCSharpFiles(string path)
    {
        if (File.Exists(path))
        {
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"'{path}' is not a .cs file.");
            }

            return [path];
        }

        if (Directory.Exists(path))
        {
            var files = Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(BinSegment, StringComparison.OrdinalIgnoreCase)
                    && !f.Contains(ObjSegment, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                throw new FileNotFoundException($"No .cs files found under '{path}'.");
            }

            return files;
        }

        throw new FileNotFoundException($"Path not found: '{path}'");
    }

    private static async Task<string> BuildCombinedSourceAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();

        foreach (var filePath in filePaths)
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            builder.AppendLine($"// ===== File: {filePath} =====");
            builder.AppendLine(content);
            builder.AppendLine();
        }

        return builder.ToString();
    }
}
