using Microsoft.Extensions.Options;

namespace Microsoft.AI.DevTeam;

public interface IAnalyzeCode 
{
    Task<IEnumerable<CodeAnalysisResponse>> Analyze(string content);
}

public class CodeAnalyzer : IAnalyzeCode
{
    private readonly ServiceOptions _serviceOptions;
    private readonly HttpClient _httpClient;
    private readonly ILogger<CodeAnalyzer> _logger;

    public CodeAnalyzer(IOptions<ServiceOptions> serviceOptions, HttpClient httpClient, ILogger<CodeAnalyzer> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceOptions);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);

        _serviceOptions = serviceOptions.Value;
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = _serviceOptions.IngesterUrl;
    }

    public async Task<IEnumerable<CodeAnalysisResponse>> Analyze(string content)
    {
        try
        {
             var request = new CodeAnalysisRequest { Content = content };
            var response = await _httpClient.PostAsJsonAsync("api/AnalyzeCode", request);
            var result = await response.Content.ReadFromJsonAsync<IEnumerable<CodeAnalysisResponse>>();
            return result!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing code");
            return [];
        }
    }
}

public class CodeAnalysisRequest
{
    public required string Content { get; set; }
}

public class CodeAnalysisResponse
{
    public required string Meaning { get; set; }
    public required string CodeBlock { get; set; }
}
