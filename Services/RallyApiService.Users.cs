using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Rally_to_ADO_Migration.Services
{
    public partial class RallyApiService
    {
        // Cache to store DisplayName -> Email mappings
        private readonly Dictionary<string, string> _userEmailCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Fetch user email from Rally User API using the _ref URL
        /// Results are cached to avoid repeated API calls
        /// </summary>
        public async Task<string> FetchUserEmailByRefAsync(string userRefUrl)
        {
            if (string.IsNullOrWhiteSpace(userRefUrl))
            {
                _loggingService.LogDebug("FetchUserEmailByRefAsync: userRefUrl is null or empty");
                return null;
            }

            // Check cache first
            if (_userEmailCache.ContainsKey(userRefUrl))
            {
                var cachedEmail = _userEmailCache[userRefUrl];
                _loggingService.LogDebug($"Using cached email for user ref: {cachedEmail}");
                return cachedEmail;
            }

            try
            {
                // Fetch user details from Rally with EmailAddress field
                var urlWithFetch = $"{userRefUrl}?fetch=EmailAddress,Email,DisplayName,UserName";
                _loggingService.LogDebug($"Fetching user details from: {urlWithFetch}");
                
                var request = new HttpRequestMessage(HttpMethod.Get, urlWithFetch);
                AddAuthenticationHeader(request);
                
                var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    _loggingService.LogWarning($"Failed to fetch user details: {response.StatusCode}");
                    return null;
                }
                
                var json = await response.Content.ReadAsStringAsync();
                _loggingService.LogDebug($"User API response length: {json.Length} chars");
                
                // Parse the User object from Rally response
                // Rally returns: {"User": {"EmailAddress": "...", "DisplayName": "...", ...}}
                var userObjectJson = ExtractJsonValue(json, "User");
                if (string.IsNullOrEmpty(userObjectJson))
                {
                    _loggingService.LogWarning("No User object in Rally response");
                    return null;
                }
                
                // Extract email fields
                var emailAddress = ExtractJsonValue(userObjectJson, "EmailAddress");
                if (string.IsNullOrEmpty(emailAddress))
                {
                    emailAddress = ExtractJsonValue(userObjectJson, "Email");
                }
                
                var displayName = ExtractJsonValue(userObjectJson, "DisplayName");
                
                if (!string.IsNullOrEmpty(emailAddress))
                {
                    _loggingService.LogInfo($"? Fetched email from Rally User API: '{emailAddress}' for '{displayName}'");
                    
                    // Cache both by ref URL and by display name for future lookups
                    _userEmailCache[userRefUrl] = emailAddress;
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        _userEmailCache[displayName] = emailAddress;
                    }
                    
                    return emailAddress;
                }
                else
                {
                    _loggingService.LogWarning($"Rally User API returned no EmailAddress for '{displayName}'");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error fetching user email from Rally: {ex.Message}", ex);
                return null;
            }
        }
        
        /// <summary>
        /// Try to get cached email by display name (faster than API call)
        /// </summary>
        public string GetCachedEmailByDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return null;
                
            if (_userEmailCache.TryGetValue(displayName, out var email))
            {
                _loggingService.LogDebug($"Found cached email for '{displayName}': {email}");
                return email;
            }
            
            return null;
        }
    }
}
