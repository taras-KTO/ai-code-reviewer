using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiCodeReviewer.Services;

public class ClaudeApiService
{
    private const string DefaultBaseUrl = "https://api.anthropic.com/v1/messages";
    private const string DefaultModel = "claude-sonnet-4-6";
    private const string AnthropicVersion = "2023-06-01";
    private const int DefaultTimeoutSeconds = 30;
    private const int DefaultMaxTokens = 4096;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<ClaudeApiService>? _logger;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;
    private readonly TimeSpan _timeout;

    public ClaudeApiService(HttpClient httpClient, IConfiguration? configuration = null, ILogger<ClaudeApiService>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;

        _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException(
                "Environment variable ANTHROPIC_API_KEY is not set. Add it to your .env file or environment.");

        _model = configuration?["Claude:Model"] ?? DefaultModel;
        _baseUrl = configuration?["Claude:BaseUrl"] ?? DefaultBaseUrl;

        var timeoutSeconds = int.TryParse(configuration?["Claude:TimeoutSeconds"], out var seconds)
            ? seconds
            : DefaultTimeoutSeconds;
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    public async Task<string> SendMessageAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);

        var requestBody = new ClaudeRequest
        {
            Model = _model,
            MaxTokens = DefaultMaxTokens,
            System = systemPrompt,
            Messages = [new ClaudeMessage { Role = "user", Content = userMessage }]
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            using var response = await _httpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = ExtractErrorMessage(responseBody) ?? responseBody;
                _logger?.LogError("Claude API returned {StatusCode}: {Error}", (int)response.StatusCode, errorMessage);
                throw new ClaudeApiException($"Claude API returned {(int)response.StatusCode} {response.StatusCode}: {errorMessage}");
            }

            return ParseResponseText(responseBody);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger?.LogError("Claude API request timed out after {TimeoutSeconds}s", _timeout.TotalSeconds);
            throw new ClaudeApiException($"Claude API request timed out after {_timeout.TotalSeconds:0}s.");
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "Claude API request failed");
            throw new ClaudeApiException($"Claude API request failed: {ex.Message}", ex);
        }
    }

    private string ParseResponseText(string responseBody)
    {
        ClaudeResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ClaudeResponse>(responseBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Failed to parse Claude API response");
            throw new ClaudeApiException("Failed to parse Claude API response.", ex);
        }

        var text = parsed?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ClaudeApiException("Claude API returned an empty response.");
        }

        return text;
    }

    private static string? ExtractErrorMessage(string responseBody)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ClaudeErrorResponse>(responseBody, JsonOptions);
            return error?.Error?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class ClaudeRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("max_tokens")]
        public required int MaxTokens { get; init; }

        [JsonPropertyName("system")]
        public string? System { get; init; }

        [JsonPropertyName("messages")]
        public required List<ClaudeMessage> Messages { get; init; }
    }

    private sealed class ClaudeMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    private sealed class ClaudeResponse
    {
        [JsonPropertyName("content")]
        public List<ClaudeContentBlock>? Content { get; init; }
    }

    private sealed class ClaudeContentBlock
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("text")]
        public string? Text { get; init; }
    }

    private sealed class ClaudeErrorResponse
    {
        [JsonPropertyName("error")]
        public ClaudeErrorDetail? Error { get; init; }
    }

    private sealed class ClaudeErrorDetail
    {
        [JsonPropertyName("message")]
        public string? Message { get; init; }
    }
}

public class ClaudeApiException : Exception
{
    public ClaudeApiException(string message) : base(message)
    {
    }

    public ClaudeApiException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
