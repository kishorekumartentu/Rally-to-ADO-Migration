using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Transforms field values between Rally and Azure DevOps formats (no System.Web dependency).
    /// </summary>
    public class FieldTransformationService
    {
        private readonly LoggingService _loggingService;
        private readonly FieldMappingConfiguration _mappingConfig;
        private const string DEFAULT_AREA_PATH = "Acute Meds Management\\Emerson\\Rally Migration";
        private static readonly string[] EMAIL_DOMAINS = { "emishealth.com", "optum.com" };
        private readonly Dictionary<string, string> _userEmailMappings;
        private readonly Dictionary<string, string> _domainMappings;
        private readonly List<string> _rallyUsers; // Track ALL Rally users for tag addition
        private readonly string _migrationUserEmail; // Fallback user for unmapped Rally users

        public FieldTransformationService(LoggingService loggingService, FieldMappingConfiguration mappingConfig = null, string migrationUserEmail = null)
        {
            _loggingService = loggingService;
            _mappingConfig = mappingConfig ?? CreateDefaultMapping();
            _userEmailMappings = InitializeUserMappings();
            _domainMappings = InitializeDomainMappings();
            _rallyUsers = new List<string>();
            
            // Set migration user email - fallback to common patterns if not specified
            _migrationUserEmail = migrationUserEmail ?? GetDefaultMigrationUserEmail();
            _loggingService.LogInfo($"FieldTransformationService initialized with migration user: {_migrationUserEmail}");
        }

        private Dictionary<string, string> InitializeDomainMappings() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { { "EMIS", "emishealth.com" }, { "Optum", "optum.com" }, { "emishealth", "emishealth.com" }, { "UHG", "optum.com" } };

        private FieldMappingConfiguration CreateDefaultMapping() => new FieldMappingConfiguration
        {
            Version = "1.0",
            GeneratedDate = DateTime.Now,
            Description = "Default mapping configuration",
            DefaultAdoProject = "Acute Meds Management",
            WorkItemTypeMappings = new List<WorkItemTypeMapping>(),
            AreaPathMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { { "EMIS | Emerson", DEFAULT_AREA_PATH }, { "EMIS", DEFAULT_AREA_PATH }, { "Emerson", DEFAULT_AREA_PATH } }
        };

        private Dictionary<string, string> InitializeUserMappings() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { 
            // Leave empty - we will dynamically get emails from Rally API
            // No hardcoded mappings needed - domain transformation happens automatically
        };

        /// <summary>
        /// Get default migration user email using common patterns
        /// </summary>
        private string GetDefaultMigrationUserEmail()
        {
            var candidates = new[] { 
                "KishoreKumar.T@emishealth.com",  // From logs - actual migration account
                "kishore.kumar@emishealth.com",
                "migration@emishealth.com",
                "admin@emishealth.com"
            };
            
            // For now return the first candidate - in a real implementation you could verify against ADO
            return candidates[0];
        }

        /// <summary>
        /// Get list of ALL Rally usernames processed (for tag addition)
        /// Call this after field transformation to add these users to tags
        /// </summary>
        public List<string> GetRallyUsers()
        {
            return new List<string>(_rallyUsers);
        }

        /// <summary>
        /// Clear the list of Rally users (call after processing work item)
        /// </summary>
        public void ClearRallyUsers()
        {
            _rallyUsers.Clear();
        }

        /// <summary>
        /// Explicitly track Rally users from work item data for tagging (regardless of state or assignment transformation)
        /// Call this for ALL work items to ensure Rally username tags are added regardless of item state
        /// </summary>
        public void TrackRallyUsersForTagging(object rallyWorkItem)
        {
            if (rallyWorkItem == null) return;
            
            try
            {
                // Extract users from common Rally user fields
                var userFields = new[] { "Owner", "SubmittedBy", "CreatedBy", "LastUpdateBy" };
                
                if (rallyWorkItem is JObject jObj)
                {
                    foreach (var fieldName in userFields)
                    {
                        var userField = jObj[fieldName];
                        if (userField != null)
                        {
                            var username = ExtractUsername(userField);
                            if (!string.IsNullOrWhiteSpace(username))
                            {
                                username = username.Trim().Replace("\"", "");
                                if (!_rallyUsers.Contains(username))
                                {
                                    _rallyUsers.Add(username);
                                    _loggingService.LogInfo($"[TRACK] Rally user '{username}' tracked for tagging from {fieldName} field");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"TrackRallyUsersForTagging error: {ex.Message}");
            }
        }

        public object TransformState(object value, string rallyType)
        {
            if (value == null) return "New";
            
            // LOG BEFORE TRANSFORMATION
            _loggingService.LogInfo($"[TRANSFORM_STATE] Transforming State for {rallyType}:");
            _loggingService.LogInfo($"   Input Rally State: '{value}'");
            
            // Determine which mapping to use based on Rally type
            Dictionary<string, string> map;
            if (string.Equals(rallyType, "Defect", StringComparison.OrdinalIgnoreCase))
            {
                map = GetDefectStateMappings();
            }
            else if (string.Equals(rallyType, "Task", StringComparison.OrdinalIgnoreCase))
            {
                map = GetTaskStateMappings();
            }
            else if (string.Equals(rallyType, "HierarchicalRequirement", StringComparison.OrdinalIgnoreCase))
            {
                // User Stories use ScheduleState with specific mappings
                map = GetUserStoryStateMappings();
            }
            else if (string.Equals(rallyType, "TestCase", StringComparison.OrdinalIgnoreCase))
            {
                map = GetTestCaseStateMappings();
            }
            else if (string.Equals(rallyType, "PortfolioItem/Feature", StringComparison.OrdinalIgnoreCase) || 
                     string.Equals(rallyType, "Feature", StringComparison.OrdinalIgnoreCase))
            {
                map = GetFeatureStateMappings();
            }
            else if (string.Equals(rallyType, "PortfolioItem/Epic", StringComparison.OrdinalIgnoreCase) || 
                     string.Equals(rallyType, "Epic", StringComparison.OrdinalIgnoreCase))
            {
                map = GetEpicStateMappings();
            }
            else
            {
                map = GetDefaultStateMappings();
            }
            
            var s = value.ToString();
            var result = map.TryGetValue(s, out var ado) ? ado : (string.Equals(rallyType, "TestCase", StringComparison.OrdinalIgnoreCase) ? "Ready" : "New"); // Default to Ready for Test Cases
            
            // LOG AFTER TRANSFORMATION
            _loggingService.LogInfo($"   Output ADO State: '{result}'");
            _loggingService.LogInfo($"   Mapping used: {rallyType} mappings");
            
            return result;
        }

        /// <summary>
        /// User Story specific ScheduleState to ADO State mappings
        /// Rally ScheduleState values: Refining, Defined, In-Progress, Completed, Accepted
        /// </summary>
        private Dictionary<string, string> GetUserStoryStateMappings() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Rally ScheduleState ? ADO State (as per user requirements)
            ["Refining"] = "New",
            ["Defined"] = "New",
            ["In-Progress"] = "Active",
            ["Completed"] = "Resolved",
            ["Accepted"] = "Closed"
        };

        private Dictionary<string, string> GetDefectStateMappings() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { 
            ["Submitted"]="New", 
            ["Open"]="Active", 
            ["In-Progress"]="Active", 
            ["Fixed"]="Resolved", 
            ["Completed"]="Resolved",  // Rally ScheduleState "Completed" ? ADO "Resolved"
            ["Verified"]="Closed", 
            ["Closed"]="Closed", 
            ["Reopened"]="Active", 
            ["Ready"]="New", 
            ["Defined"]="New", 
            ["Blocked"]="Active" 
        };
        
        private Dictionary<string, string> GetTaskStateMappings() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Rally Task State values ? ADO Task State
            // Based on user requirements: Defined?New, In-Progress?Active, Completed?Closed
            
            // Standard Rally Task states
            ["Defined"] = "New",
            ["In-Progress"] = "Active",
            ["Completed"] = "Closed",
            
            // Additional Rally Task states (for compatibility)
            ["Open"] = "Active",          // Open tasks are active
            ["New"] = "New",
            ["Ready"] = "New",
            ["In Progress"] = "Active",   // Space variation
            ["Active"] = "Active",
            ["Done"] = "Closed",
            ["Closed"] = "Closed",
            ["Removed"] = "Removed"
        };
        
        private Dictionary<string, string> GetTestCaseStateMappings() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Rally Test Case states: Design, In Progress, Completed
            // ADO Test Case states: Design, Ready, Closed
            // DEFAULT: All states map to Ready unless explicitly Closed/Completed
            ["Design"] = "Ready",
            ["Defined"] = "Ready",
            ["New"] = "Ready",
            ["In-Progress"] = "Ready",
            ["In Progress"] = "Ready",
            ["Ready"] = "Ready",
            ["Active"] = "Ready",
            ["Completed"] = "Closed",
            ["Closed"] = "Closed",
            ["Done"] = "Closed"
        };
        
        private Dictionary<string, string> GetFeatureStateMappings() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Rally Feature states: Open, In-Progress, Done
            // ADO Feature states: New, Active, Resolved, Closed
            ["Open"] = "New",
            ["Defined"] = "New",
            ["In-Progress"] = "Active",
            ["Active"] = "Active",
            ["Done"] = "Closed",
            ["Completed"] = "Closed",
            ["Closed"] = "Closed"
        };
        
        private Dictionary<string, string> GetEpicStateMappings() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Rally Epic states are similar to Features
            ["Open"] = "New",
            ["Defined"] = "New",
            ["In-Progress"] = "Active",
            ["Active"] = "Active",
            ["Done"] = "Closed",
            ["Completed"] = "Closed",
            ["Closed"] = "Closed"
        };
        
        private Dictionary<string, string> GetDefaultStateMappings() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { ["Defined"]="New", ["In-Progress"]="Active", ["Completed"]="Resolved", ["Accepted"]="Closed", ["Ready"]="New", ["Blocked"]="Active" };

        public object TransformIterationToPath(object value, FieldMapping fieldMapping)
        {
            try
            {
                if (value == null) return null;
                var name = ExtractIterationName(value);
                if (string.IsNullOrWhiteSpace(name)) return null;
                var root = _mappingConfig?.DefaultAdoProject ?? "Acute Meds Management";
                name = name.Trim().Replace('/', ' ').Replace('\\', ' ').Trim();
                var path = root + "\\" + name;
                _loggingService.LogDebug($"Transformed Iteration '{name}' -> '{path}'");
                return path;
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"TransformIterationToPath failed: {ex.Message}");
                return null;
            }
        }

        private string ExtractIterationName(object value)
        {
            try
            {
                if (value is JObject j) return j["Name"]?.ToString() ?? j["_refObjectName"]?.ToString();
                if (value is string s)
                {
                    if (s.StartsWith("{")) { var jo = JObject.Parse(s); return jo["Name"]?.ToString() ?? jo["_refObjectName"]?.ToString(); }
                    return s;
                }
                return value.ToString();
            }
            catch { return null; }
        }

        public object TransformDate(object value)
        {
            if (value == null) return null;
            if (value is DateTime dt) return dt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            return DateTime.TryParse(value.ToString(), out var parsed) ? parsed.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") : value;
        }

        public object TransformUser(object value)
        {
            if (value == null)
            {
                // No user specified in Rally - keep unassigned in ADO, no tags
                _loggingService.LogInfo($"[USER] Rally unassigned → ADO unassigned (no tags)");
                return null;
            }
            try
            {
                var username = ExtractUsername(value);
                if (string.IsNullOrWhiteSpace(username)) return null;
                username = username.Trim().Replace("\"", "");
                
                // Track Rally user for tag addition (user's requirement)
                if (!_rallyUsers.Contains(username))
                {
                    _rallyUsers.Add(username);
                }
                
                _loggingService.LogInfo($"[USER] Processing Rally user: '{username}'");
                
                // 1. Check manual user mappings first (handles cases like vikram → vikram1)
                if (_userEmailMappings.TryGetValue(username, out var mappedUser))
                {
                    _loggingService.LogInfo($"   [MAPPED] Rally '{username}' → ADO '{mappedUser}' (manual mapping)");
                    return mappedUser;
                }
                
                // 2. Handle domain transformation for emails (optum.com → emishealth.com)
                if (IsValidEmail(username))
                {
                    var domain = ExtractDomain(username);
                    if (string.Equals(domain, "optum.com", StringComparison.OrdinalIgnoreCase))
                    {
                        var localPart = username.Substring(0, username.LastIndexOf('@'));
                        var transformedEmail = $"{localPart}@emishealth.com";
                        _loggingService.LogInfo($"   [TRANSFORM] Rally '{username}' → ADO '{transformedEmail}' (domain transformation)");
                        return transformedEmail;
                    }
                    else
                    {
                        // Email with different domain - return as-is
                        _loggingService.LogInfo($"   [DIRECT] Rally '{username}' → ADO '{username}' (same email)");
                        return username;
                    }
                }
                
                // 3. Try simple username match (like kishore → kishore@emishealth.com)
                // BUT for unmapped users like "oleg", assign to migration user instead
                _loggingService.LogWarning($"   [UNMAPPED] Rally user '{username}' not in manual mappings - assigning to migration user");
                return _migrationUserEmail;
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"TransformUser error: {ex.Message}, assigning to migration user");
                if (value != null)
                {
                    var fallbackUsername = value.ToString().Trim().Replace("\"", "");
                    if (!string.IsNullOrWhiteSpace(fallbackUsername) && !_rallyUsers.Contains(fallbackUsername))
                    {
                        _rallyUsers.Add(fallbackUsername);
                    }
                }
                return _migrationUserEmail;
            }
        }

        private string DetermineDomainFromContext(string input)
        { foreach (var m in _domainMappings) if (input.IndexOf(m.Key, StringComparison.OrdinalIgnoreCase) >= 0) return m.Value; return EMAIL_DOMAINS[0]; }
        private string ExtractDomain(string email) { var at = email.LastIndexOf('@'); return at >= 0 ? email.Substring(at + 1) : string.Empty; }
        private bool IsValidDomain(string domain) => Array.Exists(EMAIL_DOMAINS, d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase));
        private string ExtractUsername(object value)
        { if (value is JToken jt) return ExtractUsernameFromJToken(jt); if (value is string s) return ExtractUsernameFromString(s); return value.ToString(); }
        private string ExtractUsernameFromJToken(JToken token)
        {
            // Priority order for Rally user objects:
            // 1. Email/email (actual email address) - HIGHEST PRIORITY
            // 2. EmailAddress (alternative email field)
            // 3. _refObjectName (display name, often contains email)
            // 4. Name/DisplayName (fallback to display name)
            // 5. UserName/username (Rally username)
            
            string[] emailProps = { "Email", "email", "EmailAddress", "_refObjectName" };
            foreach (var p in emailProps)
            {
                var v = token.Value<string>(p);
                if (!string.IsNullOrWhiteSpace(v))
                {
                    // If it looks like an email, use it directly
                    if (IsValidEmail(v.Trim()))
                    {
                        _loggingService.LogDebug($"Extracted email from Rally user field '{p}': {v}");
                        return v.Trim();
                    }
                    // _refObjectName might contain "Name (email@domain.com)" format
                    if (p == "_refObjectName" && v.Contains("@"))
                    {
                        var emailMatch = Regex.Match(v, @"[\w\.\-+]+@[\w\.\-]+\.\w+");
                        if (emailMatch.Success)
                        {
                            _loggingService.LogDebug($"Extracted email from _refObjectName: {emailMatch.Value}");
                            return emailMatch.Value;
                        }
                    }
                }
            }
            
            // Fallback to display name fields
            string[] nameProps = { "Name", "DisplayName", "UserName", "username" };
            foreach (var p in nameProps)
            {
                var v = token.Value<string>(p);
                if (!string.IsNullOrWhiteSpace(v))
                {
                    _loggingService.LogDebug($"No email found, using name field '{p}': {v}");
                    return v;
                }
            }
            
            _loggingService.LogWarning($"Could not extract user info from token: {token}");
            return token.ToString();
        }
        private string ExtractUsernameFromString(string input)
        {
            var trimmed = input == null ? string.Empty : input.Trim();
            if (string.IsNullOrEmpty(trimmed)) return string.Empty;
            if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            {
                try { return ExtractUsernameFromJToken(JObject.Parse(trimmed)); } catch { }
            }
            var emailMatch = Regex.Match(trimmed, @"[\w\.\-+]+@[\w\.\-]+\.\w+");
            if (emailMatch.Success) return emailMatch.Value;
            // Extract quoted content manually instead of regex escaping
            int firstQuote = trimmed.IndexOf('"');
            if (firstQuote >= 0)
            {
                int secondQuote = trimmed.IndexOf('"', firstQuote + 1);
                if (secondQuote > firstQuote) return trimmed.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
            }
            return trimmed;
        }
        private bool IsValidEmail(string email) => Regex.IsMatch(email, @"^[\w\.\-+]+@[\w\.\-]+\.\w+$");
        private string NormalizeUsername(string username) => Regex.Replace(Regex.Replace(username, @"[^\w\s]", ""), @"\s+", " ").Trim();

        public object TransformEnum(object value, string fieldName)
        {
            if (value == null) return null;
            var v = value.ToString();
            
            // For Priority, only transform if there's actually a value
            if (fieldName?.IndexOf("priority", StringComparison.OrdinalIgnoreCase) >= 0) 
            {
                if (string.IsNullOrWhiteSpace(v))
                {
                    _loggingService.LogDebug($"Priority field is empty, skipping");
                    return null;
                }
                return TransformPriority(v);
            }
            
            if (fieldName?.IndexOf("severity", StringComparison.OrdinalIgnoreCase) >= 0) return TransformSeverity(v);
            return v;
        }

        private object TransformPriority(string priorityValue)
        {
            // Rally typically doesn't have a Priority field - only map if explicitly provided
            if (string.IsNullOrWhiteSpace(priorityValue))
            {
                return null;  // Don't set Priority if Rally doesn't have it
            }
            
            var map = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase)
            { ["Resolve Immediately"] = 1,["High Attention"] = 2,["Normal"] = 3,["Low"] = 4,["Critical"] = 1,["High"] = 2,["Medium"] = 3,["1"] = 1,["2"] = 2,["3"] = 3,["4"] = 4,["P1"] = 1,["P2"] = 2,["P3"] = 3,["P4"] = 4 };
            if (map.TryGetValue(priorityValue, out var p)) return p;
            if (int.TryParse(priorityValue, out var parsed)) return parsed;
            var s = priorityValue.ToLower();
            if (s.Contains("urgent") || s.Contains("critical")) return 1;
            if (s.Contains("high")) return 2;
            if (s.Contains("normal") || s.Contains("medium")) return 3;
            if (s.Contains("low")) return 4;
            
            // If we can't determine a valid priority, don't set it
            _loggingService.LogDebug($"Could not map Priority value '{priorityValue}', skipping field");
            return null;
        }

        private object TransformSeverity(string severityValue)
        {
            // ADO valid severity values: "1 - Critical", "2 - High", "3 - Medium", "4 - Low"
            // This method normalizes Rally severity values to match ADO's expected format
            
            var map = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
            { 
                // Rally standard severity values
                ["Crash/Data Loss"] = "1 - Critical",
                ["Major Problem"] = "2 - High",
                ["Minor Problem"] = "3 - Medium",
                ["Cosmetic"] = "4 - Low",
                
                // Common alternative values
                ["Critical"] = "1 - Critical",
                ["High"] = "2 - High",
                ["Medium"] = "3 - Medium",
                ["Low"] = "4 - Low",
                
                // Numeric values
                ["1"] = "1 - Critical",
                ["2"] = "2 - High",
                ["3"] = "3 - Medium",
                ["4"] = "4 - Low",
                
                // Additional common values
                ["Blocker"] = "1 - Critical",
                ["Major"] = "2 - High",
                ["Minor"] = "3 - Medium",
                ["Trivial"] = "4 - Low",
                
                // Rally may send values like "2 - Major Problem" - normalize these
                ["1 - Crash/Data Loss"] = "1 - Critical",
                ["2 - Major Problem"] = "2 - High",
                ["3 - Minor Problem"] = "3 - Medium",
                ["4 - Cosmetic"] = "4 - Low"
            };
            
            // First try exact match
            if (map.TryGetValue(severityValue, out var sev)) 
            {
                _loggingService.LogDebug($"[SEVERITY] Exact match: '{severityValue}' -> '{sev}'");
                return sev;
            }
            
            // If value is in format "number - text", extract number and normalize
            var match = Regex.Match(severityValue, @"^\s*([1-4])\s*[-\s]");
            if (match.Success) 
            {
                var number = match.Groups[1].Value;
                string normalized;
                if (number == "1")
                    normalized = "1 - Critical";
                else if (number == "2")
                    normalized = "2 - High";
                else if (number == "3")
                    normalized = "3 - Medium";
                else if (number == "4")
                    normalized = "4 - Low";
                else
                    normalized = "3 - Medium"; // Default fallback
                    
                _loggingService.LogInfo($"[SEVERITY] Normalized '{severityValue}' -> '{normalized}'");
                return normalized;
            }
            
            // Fallback: keyword matching
            var s = severityValue.ToLower();
            if (s.Contains("crash") || s.Contains("critical") || s.Contains("blocker")) 
            {
                _loggingService.LogInfo($"[SEVERITY] Keyword match: '{severityValue}' -> '1 - Critical'");
                return "1 - Critical";
            }
            if (s.Contains("major") || s.Contains("high")) 
            {
                _loggingService.LogInfo($"[SEVERITY] Keyword match: '{severityValue}' -> '2 - High'");
                return "2 - High";
            }
            if (s.Contains("minor") || s.Contains("medium")) 
            {
                _loggingService.LogInfo($"[SEVERITY] Keyword match: '{severityValue}' -> '3 - Medium'");
                return "3 - Medium";
            }
            if (s.Contains("cosmetic") || s.Contains("trivial") || s.Contains("low")) 
            {
                _loggingService.LogInfo($"[SEVERITY] Keyword match: '{severityValue}' -> '4 - Low'");
                return "4 - Low";
            }
            
            // Ultimate fallback
            _loggingService.LogWarning($"[SEVERITY] Could not map '{severityValue}', using default '3 - Medium'");
            return "3 - Medium";
        }

        public object TransformCollection(object value)
        {
            if (value == null) return null;
            if (value is System.Collections.IEnumerable e && !(value is string))
            { var items = new List<string>(); foreach (var i in e) if (i != null) items.Add(i.ToString()); return string.Join(", ", items); }
            return value.ToString();
        }

        public object TransformRallyId(RallyWorkItem rallyItem) => !string.IsNullOrEmpty(rallyItem.FormattedID) ? $"[{rallyItem.FormattedID}] {rallyItem.Name}" : rallyItem.Name;

        public object TransformProjectToAreaPath(object value, FieldMapping fieldMapping)
        {
            try
            {
                string def = fieldMapping?.DefaultValue ?? _mappingConfig?.DefaultAdoProject + "\\Emerson" ?? DEFAULT_AREA_PATH;
                if (value == null) return def;
                string projectName = ExtractProjectName(value);
                if (string.IsNullOrWhiteSpace(projectName)) return def;
                if (_mappingConfig?.AreaPathMappings != null)
                {
                    if (_mappingConfig.AreaPathMappings.TryGetValue(projectName, out var mapped)) return mapped;
                    if (projectName.Contains("|"))
                    { var shortName = projectName.Split('|')[0].Trim(); if (_mappingConfig.AreaPathMappings.TryGetValue(shortName, out mapped)) return mapped; }
                }
                return def;
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"TransformProjectToAreaPath error: {ex.Message}");
                return fieldMapping?.DefaultValue ?? _mappingConfig?.DefaultAdoProject + "\\Emerson" ?? DEFAULT_AREA_PATH;
            }
        }

        private string ExtractProjectName(object value)
        {
            try
            {
                if (value is JObject jo) return jo["_refObjectName"]?.ToString() ?? jo["Name"]?.ToString();
                if (value is string s && s.StartsWith("{")) { var parsed = JObject.Parse(s); return parsed["_refObjectName"]?.ToString() ?? parsed["Name"]?.ToString(); }
                return value.ToString();
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"ExtractProjectName error: {ex.Message}");
                return null;
            }
        }

        private static string SimpleHtmlEncode(string input)
        { if (string.IsNullOrEmpty(input)) return string.Empty; return input.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;"); }

        /// <summary>
        /// Decodes Unicode escape sequences like \u003C to their actual characters
        /// </summary>
        private static string DecodeUnicodeEscapes(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            // Use Regex.Unescape to decode all Unicode escape sequences (\uXXXX)
            try
            {
                return Regex.Unescape(input);
            }
            catch (Exception)
            {
                // If Regex.Unescape fails, return original
                return input;
            }
        }

        public object TransformHtmlPreserve(object value)
        {
            if (value == null) return string.Empty;
            var raw = value.ToString(); 
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            
            // Decode Unicode escapes first (e.g., \u003C to <)
            raw = DecodeUnicodeEscapes(raw);
            
            raw = Regex.Replace(raw, "\\{color:([^}]+)\\}(.*?)\\{color\\}", m => $"<span style='color:{SimpleHtmlEncode(m.Groups[1].Value)}'>{SimpleHtmlEncode(m.Groups[2].Value)}</span>", RegexOptions.Singleline);
            raw = SimpleHtmlEncode(raw).Replace("\r\n", "<br/>").Replace("\n", "<br/>");
            return "<div>" + raw + "</div>";
        }

        public object TransformHtmlAppend(object existingDescription, object appendValue)
        {
            var baseHtml = existingDescription?.ToString(); var toAppend = appendValue?.ToString();
            if (string.IsNullOrWhiteSpace(toAppend)) return baseHtml ?? string.Empty;
            var preserved = TransformHtmlPreserve(toAppend).ToString();
            if (string.IsNullOrWhiteSpace(baseHtml)) return preserved;
            return baseHtml + "<hr/>" + preserved;
        }

        /// <summary>
        /// Gets the configured migration user email.
        /// This is the fallback user assigned when Rally users are not found in ADO.
        /// </summary>
        /// <returns>The migration user email or null if not configured</returns>
        public string GetMigrationUserEmail()
        {
            return _migrationUserEmail;
        }
    }
}
