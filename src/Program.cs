using AiCodeReviewer.Agents;
using AiCodeReviewer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace AiCodeReviewer;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        var path = ParsePathArgument(args);
        if (path is null)
        {
            Console.Error.WriteLine("Usage: dotnet run -- --path <file-or-directory>");
            return 1;
        }

        LoadDotEnv();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        using var loggerFactory = LoggerFactory.Create(builder => builder
            .AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            })
            .AddNLog(Path.Combine(AppContext.BaseDirectory, "nlog.config"))
            .SetMinimumLevel(LogLevel.Information));

        using var httpClient = new HttpClient();

        ClaudeApiService claudeApiService;
        try
        {
            claudeApiService = new ClaudeApiService(httpClient, configuration, loggerFactory.CreateLogger<ClaudeApiService>());
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        var securityAgent = new SecurityAgent(claudeApiService, loggerFactory.CreateLogger<SecurityAgent>());
        var qualityAgent = new QualityAgent(claudeApiService, loggerFactory.CreateLogger<QualityAgent>());
        var reportAgent = new ReportAgent(loggerFactory.CreateLogger<ReportAgent>());
        var orchestrator = new OrchestratorAgent(
            securityAgent,
            qualityAgent,
            reportAgent,
            loggerFactory.CreateLogger<OrchestratorAgent>());

        try
        {
            var report = await orchestrator.RunAsync(path);
            Console.WriteLine(report);
            Console.WriteLine($"Report saved to {Path.Combine(Directory.GetCurrentDirectory(), "report.md")}");
            return 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static string? ParsePathArgument(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--path" && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static void LoadDotEnv()
    {
        var envPath = FindUpwards(Directory.GetCurrentDirectory(), ".env")
            ?? FindUpwards(AppContext.BaseDirectory, ".env");

        if (envPath is null)
        {
            return;
        }

        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim().Trim('"');

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static string? FindUpwards(string startDirectory, string fileName)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
