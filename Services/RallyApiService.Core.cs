using System;
using System.Net.Http;
using System.Text;
using Rally_to_ADO_Migration.Models;
using System.Net.Http.Headers;

namespace Rally_to_ADO_Migration.Services
{
    public partial class RallyApiService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly LoggingService _loggingService;
        private string _apiKey;
        private string _serverUrl;
        private string _workspace;
        private string _project;

        public RallyApiService(LoggingService loggingService)
        {
            _httpClient = new HttpClient();
            _loggingService = loggingService;
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        private void ConfigureConnection(ConnectionSettings settings)
        {
            _apiKey = settings.RallyApiKey?.Trim();
            _serverUrl = settings.RallyServerUrl?.TrimEnd('/') ?? "https://rally1.rallydev.com";
            _workspace = settings.RallyWorkspace?.Trim();
            _project = settings.RallyProject?.Trim();
            _loggingService.LogDebug("Rally connection configured:");
            _loggingService.LogDebug($"- Server URL: {_serverUrl}");
            _loggingService.LogDebug($"- Workspace: {_workspace ?? "Not specified"}");
            _loggingService.LogDebug($"- Project: {_project ?? "Not specified"}");
            _loggingService.LogDebug($"- API Key: {(_apiKey?.Length > 0 ? $"***{_apiKey.Substring(Math.Max(0, _apiKey.Length - 4))}" : "Not provided")}");
        }

        private void AddAuthenticationHeader(HttpRequestMessage request)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _loggingService.LogError("Rally API Key is null or empty - cannot authenticate");
                return;
            }
            try
            {
                var cleanApiKey = _apiKey.Trim();
                var authString = cleanApiKey + ":"; // username=apikey, empty password
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.UserAgent.Clear();
                request.Headers.UserAgent.ParseAdd("Rally-ADO-Migration-Tool/1.0");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to add Rally authentication header", ex);
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
