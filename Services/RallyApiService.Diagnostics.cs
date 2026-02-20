using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    public partial class RallyApiService
    {
        public async Task<bool> TestConnectionAsync(ConnectionSettings settings)
        {
            try
            {
                ConfigureConnection(settings);
                if (string.IsNullOrEmpty(_apiKey)) { _loggingService.LogError("Rally API Key is required for connection test"); return false; }
                if (string.IsNullOrEmpty(_workspace)) { _loggingService.LogError("Rally Workspace is required for connection test"); return false; }
                var testUrl = $"{_serverUrl}/slm/webservice/v2.0/hierarchicalrequirement?workspace=/workspace/{_workspace}&pagesize=1&fetch=FormattedID,Name";
                var request = new HttpRequestMessage(HttpMethod.Get, testUrl);
                AddAuthenticationHeader(request);
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return false;
                var content = await response.Content.ReadAsStringAsync();
                return content.Contains("\"QueryResult\"") && content.Contains("\"Results\"");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Rally connection test failed", ex);
                return false;
            }
        }

        public async Task<string> DiagnoseConnectionAsync(ConnectionSettings settings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== RALLY CONNECTION DIAGNOSTICS ===");
            try
            {
                ConfigureConnection(settings);
                sb.AppendLine($"Server URL: {_serverUrl}");
                sb.AppendLine($"Workspace: {_workspace ?? "NOT SET"}");
                sb.AppendLine($"Project: {_project ?? "NOT SET"}");
                sb.AppendLine($"API Key: {(_apiKey?.Length > 0 ? "SET" : "NOT SET")}");
                if (string.IsNullOrEmpty(_apiKey)) { sb.AppendLine("? Missing API Key"); return sb.ToString(); }
                if (string.IsNullOrEmpty(_workspace)) { sb.AppendLine("? Missing Workspace"); return sb.ToString(); }
                var basicUrl = $"{_serverUrl}/slm/webservice/v2.0/hierarchicalrequirement?workspace=/workspace/{_workspace}&pagesize=1&fetch=FormattedID";
                var request = new HttpRequestMessage(HttpMethod.Get, basicUrl); AddAuthenticationHeader(request);
                var resp = await _httpClient.SendAsync(request);
                sb.AppendLine($"Basic status: {resp.StatusCode}");
                var content = await resp.Content.ReadAsStringAsync();
                if (resp.IsSuccessStatusCode && content.Contains("\"QueryResult\"") && content.Contains("\"Results\"")) sb.AppendLine("? Structure verified");
                else sb.AppendLine("? Unexpected response structure");
            }
            catch (Exception ex) { sb.AppendLine($"EXCEPTION: {ex.Message}"); }
            return sb.ToString();
        }

        public async Task<string> TestAuthenticationMethodsAsync(ConnectionSettings settings)
        {
            var results = new StringBuilder();
            results.AppendLine("=== RALLY AUTH TEST ===");
            ConfigureConnection(settings);
            if (string.IsNullOrEmpty(_apiKey)) return "No API key";
            var testUrl = $"{_serverUrl}/slm/webservice/v2.0/hierarchicalrequirement?workspace=/workspace/{_workspace}&pagesize=1&fetch=FormattedID";
            string[] patterns = { $"_:{_apiKey}", _apiKey, $"{_apiKey}:" };
            foreach (var p in patterns)
            {
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, testUrl);
                    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(p));
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                    req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                    var resp = await _httpClient.SendAsync(req);
                    results.AppendLine($"Pattern '{p.Substring(0, Math.Min(6, p.Length))}*' => {resp.StatusCode}");
                    if (resp.IsSuccessStatusCode) { results.AppendLine("SUCCESS"); break; }
                }
                catch (Exception ex) { results.AppendLine($"Pattern '{p}' exception: {ex.Message}"); }
            }
            return results.ToString();
        }
    }
}
