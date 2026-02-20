using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using Newtonsoft.Json;
using Rally_to_ADO_Migration.Models;
using Rally_to_ADO_Migration.Security;

namespace Rally_to_ADO_Migration.Services
{
    public class SettingsService
    {
        private readonly string _settingsDirectory;
        private readonly string _settingsFilePath;
        private List<SavedConnectionSettings> _savedSettings;
        private readonly string _logFilePath; // For debugging

        public SettingsService()
        {
            _settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rally to ADO Migration");
            Directory.CreateDirectory(_settingsDirectory);
            _settingsFilePath = Path.Combine(_settingsDirectory, "SavedConnections.json");
            _logFilePath = Path.Combine(_settingsDirectory, "SettingsService.log");
            
            Log($"[SettingsService] Constructor - Settings path: {_settingsFilePath}");
            
            LoadSettings();
        }
        
        private void Log(string message)
        {
            try
            {
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, logMessage);
                System.Diagnostics.Debug.WriteLine(message);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public void SaveConnectionSettings(string name, ConnectionSettings settings)
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Setting name cannot be empty", nameof(name));
            }
            
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            
            // Log incoming values for debugging
            System.Diagnostics.Debug.WriteLine($"[SaveConnectionSettings] Name: '{name}'");
            System.Diagnostics.Debug.WriteLine($"[SaveConnectionSettings] RallyServerUrl: '{settings.RallyServerUrl}'");
            System.Diagnostics.Debug.WriteLine($"[SaveConnectionSettings] RallyWorkspace: '{settings.RallyWorkspace}'");
            System.Diagnostics.Debug.WriteLine($"[SaveConnectionSettings] RallyProject: '{settings.RallyProject}'");
            System.Diagnostics.Debug.WriteLine($"[SaveConnectionSettings] AdoOrganization: '{settings.AdoOrganization}'");
            System.Diagnostics.Debug.WriteLine($"[SaveConnectionSettings] AdoProject: '{settings.AdoProject}'");
            System.Diagnostics.Debug.WriteLine($"[SaveConnectionSettings] AdoServerUrl: '{settings.AdoServerUrl}'");
            
            var savedSetting = new SavedConnectionSettings
            {
                Name = name.Trim(),
                RallyServerUrl = settings.RallyServerUrl ?? string.Empty,
                RallyWorkspace = settings.RallyWorkspace ?? string.Empty,
                RallyProject = settings.RallyProject ?? string.Empty,
                AdoOrganization = settings.AdoOrganization ?? string.Empty,
                AdoProject = settings.AdoProject ?? string.Empty,
                AdoServerUrl = settings.AdoServerUrl ?? string.Empty,
                LastUsed = DateTime.Now
            };
            
            System.Diagnostics.Debug.WriteLine($"[SaveConnectionSettings] Created SavedConnectionSettings:");
            System.Diagnostics.Debug.WriteLine($"  Name: '{savedSetting.Name}'");
            System.Diagnostics.Debug.WriteLine($"  RallyWorkspace: '{savedSetting.RallyWorkspace}'");
            System.Diagnostics.Debug.WriteLine($"  AdoOrganization: '{savedSetting.AdoOrganization}'");

            // Remove existing setting with same name
            _savedSettings.RemoveAll(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            
            // Add new setting
            _savedSettings.Add(savedSetting);
            
            System.Diagnostics.Debug.WriteLine($"[SaveConnectionSettings] Total settings in list: {_savedSettings.Count}");
            
            SaveSettings();
        }

        public List<SavedConnectionSettings> GetSavedSettings()
        {
            return new List<SavedConnectionSettings>(_savedSettings);
        }

        public SavedConnectionSettings GetSettingsByName(string name)
        {
            return _savedSettings.Find(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public void DeleteSettings(string name)
        {
            _savedSettings.RemoveAll(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            SaveSettings();
        }

        public void UpdateLastUsed(string name)
        {
            var setting = GetSettingsByName(name);
            if (setting != null)
            {
                setting.LastUsed = DateTime.Now;
                SaveSettings();
            }
        }

        private void LoadSettings()
        {
            _savedSettings = new List<SavedConnectionSettings>();
            
            try
            {
                Log($"[LoadSettings] Loading from: {_settingsFilePath}");
                Log($"[LoadSettings] File exists: {File.Exists(_settingsFilePath)}");
                
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    Log($"[LoadSettings] File content length: {json?.Length ?? 0}");
                    
                    if (!string.IsNullOrEmpty(json))
                    {
                        Log($"[LoadSettings] JSON content: {json.Substring(0, Math.Min(200, json.Length))}...");
                        
                        // Use Newtonsoft.Json for reliable parsing
                        _savedSettings = JsonConvert.DeserializeObject<List<SavedConnectionSettings>>(json) 
                                        ?? new List<SavedConnectionSettings>();
                        
                        Log($"[LoadSettings] Parsed {_savedSettings.Count} settings");
                        
                        // Log each setting before cleanup
                        for (int i = 0; i < _savedSettings.Count; i++)
                        {
                            var s = _savedSettings[i];
                            Log($"[LoadSettings]   Setting {i}: Name='{s.Name}', " +
                                $"RallyWorkspace='{s.RallyWorkspace}', " +
                                $"AdoOrganization='{s.AdoOrganization}', " +
                                $"AdoProject='{s.AdoProject}'");
                        }
                        
                        // Clean up any empty or invalid settings
                        var originalCount = _savedSettings.Count;
                        _savedSettings.RemoveAll(s =>
                        {
                            var isEmpty = string.IsNullOrWhiteSpace(s.Name) ||
                                (string.IsNullOrWhiteSpace(s.RallyWorkspace) &&
                                 string.IsNullOrWhiteSpace(s.AdoOrganization) &&
                                 string.IsNullOrWhiteSpace(s.AdoProject));
                            
                            if (isEmpty)
                            {
                                Log($"[LoadSettings]   REMOVING: Name='{s.Name}' (validation failed)");
                            }
                            
                            return isEmpty;
                        });
                        
                        Log($"[LoadSettings] After cleanup: {_savedSettings.Count} valid settings (removed {originalCount - _savedSettings.Count})");
                        
                        if (_savedSettings.Count < originalCount)
                        {
                            Log($"[LoadSettings] Saving cleaned settings...");
                            // Some invalid settings were removed, save the cleaned list
                            SaveSettings();
                        }
                    }
                    else
                    {
                        Log("[LoadSettings] JSON content is empty");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[LoadSettings] ERROR: {ex.Message}");
                Log($"[LoadSettings] StackTrace: {ex.StackTrace}");
                // If loading fails, start with empty list
                _savedSettings = new List<SavedConnectionSettings>();
            }
        }

        private void SaveSettings()
        {
            try
            {
                Log($"[SaveSettings] Saving {_savedSettings.Count} settings to file...");
                Log($"[SaveSettings] File path: {_settingsFilePath}");
                
                // Use Newtonsoft.Json for reliable serialization
                var json = JsonConvert.SerializeObject(_savedSettings, Formatting.Indented);
                
                Log($"[SaveSettings] Serialized JSON ({json.Length} chars)");
                
                // Ensure directory exists
                var directory = Path.GetDirectoryName(_settingsFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    Log($"[SaveSettings] Created directory: {directory}");
                }
                
                File.WriteAllText(_settingsFilePath, json, Encoding.UTF8);
                
                Log($"[SaveSettings] File written successfully: {_settingsFilePath}");
                
                // Verify file was written
                if (File.Exists(_settingsFilePath))
                {
                    var fileLength = new FileInfo(_settingsFilePath).Length;
                    Log($"[SaveSettings] File exists, size: {fileLength} bytes");
                }
                else
                {
                    Log($"[SaveSettings] WARNING: File does not exist after write!");
                }
            }
            catch (Exception ex)
            {
                Log($"[SaveSettings] ERROR: {ex.Message}");
                Log($"[SaveSettings] StackTrace: {ex.StackTrace}");
                throw new InvalidOperationException($"Failed to save settings: {ex.Message}", ex);
            }
        }

        // Simple JSON serialization for basic objects (avoiding Newtonsoft.Json dependency for now)
        private string SerializeSettingsToJson(List<SavedConnectionSettings> settings)
        {
            var json = new StringBuilder();
            json.AppendLine("[");
            
            for (int i = 0; i < settings.Count; i++)
            {
                var setting = settings[i];
                json.AppendLine("  {");
                json.AppendLine($"    \"Name\": \"{EscapeJsonString(setting.Name)}\",");
                json.AppendLine($"    \"RallyServerUrl\": \"{EscapeJsonString(setting.RallyServerUrl)}\",");
                json.AppendLine($"    \"RallyWorkspace\": \"{EscapeJsonString(setting.RallyWorkspace)}\",");
                json.AppendLine($"    \"RallyProject\": \"{EscapeJsonString(setting.RallyProject)}\",");
                json.AppendLine($"    \"AdoOrganization\": \"{EscapeJsonString(setting.AdoOrganization)}\",");
                json.AppendLine($"    \"AdoProject\": \"{EscapeJsonString(setting.AdoProject)}\",");
                json.AppendLine($"    \"AdoServerUrl\": \"{EscapeJsonString(setting.AdoServerUrl)}\",");
                json.AppendLine($"    \"LastUsed\": \"{setting.LastUsed:yyyy-MM-ddTHH:mm:ss.fffZ}\"");
                json.Append("  }");
                if (i < settings.Count - 1)
                    json.AppendLine(",");
                else
                    json.AppendLine();
            }
            
            json.AppendLine("]");
            return json.ToString();
        }

        private List<SavedConnectionSettings> ParseSettingsJson(string json)
        {
            var settings = new List<SavedConnectionSettings>();
            
            System.Diagnostics.Debug.WriteLine($"[ParseSettingsJson] Input JSON length: {json?.Length ?? 0}");
            
            // Simple JSON parsing - this is basic and should be replaced with proper JSON library
            // For now, using basic string manipulation
            json = json.Trim();
            
            System.Diagnostics.Debug.WriteLine($"[ParseSettingsJson] After trim: '{json.Substring(0, Math.Min(100, json.Length))}'");
            
            if (json.StartsWith("[") && json.EndsWith("]"))
            {
                json = json.Substring(1, json.Length - 2).Trim();
                System.Diagnostics.Debug.WriteLine($"[ParseSettingsJson] After removing array brackets, length: {json.Length}");
                
                if (string.IsNullOrWhiteSpace(json))
                {
                    System.Diagnostics.Debug.WriteLine($"[ParseSettingsJson] JSON is empty array");
                    return settings;
                }
                
                var objects = SplitJsonObjects(json);
                System.Diagnostics.Debug.WriteLine($"[ParseSettingsJson] Split into {objects.Count} objects");
                
                foreach (var obj in objects)
                {
                    System.Diagnostics.Debug.WriteLine($"[ParseSettingsJson] Parsing object: {obj.Substring(0, Math.Min(200, obj.Length))}");
                    var setting = ParseSingleSetting(obj);
                    if (setting != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ParseSettingsJson] Parsed setting: Name='{setting.Name}'");
                        settings.Add(setting);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[ParseSettingsJson] Failed to parse setting");
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ParseSettingsJson] JSON is not an array!");
            }
            
            System.Diagnostics.Debug.WriteLine($"[ParseSettingsJson] Returning {settings.Count} settings");
            return settings;
        }

        private List<string> SplitJsonObjects(string json)
        {
            var objects = new List<string>();
            var current = new StringBuilder();
            int braceCount = 0;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                
                if (escape)
                {
                    current.Append(c);
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escape = true;
                    current.Append(c);
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    current.Append(c);
                    continue;
                }

                if (!inString)
                {
                    if (c == '{')
                        braceCount++;
                    else if (c == '}')
                        braceCount--;
                }

                current.Append(c);

                if (!inString && braceCount == 0 && c == '}')
                {
                    objects.Add(current.ToString().Trim());
                    current.Clear();
                }
            }

            return objects;
        }

        private SavedConnectionSettings ParseSingleSetting(string json)
        {
            try
            {
                var setting = new SavedConnectionSettings();
                
                setting.Name = ExtractJsonValue(json, "Name");
                setting.RallyServerUrl = ExtractJsonValue(json, "RallyServerUrl");
                setting.RallyWorkspace = ExtractJsonValue(json, "RallyWorkspace");
                setting.RallyProject = ExtractJsonValue(json, "RallyProject");
                setting.AdoOrganization = ExtractJsonValue(json, "AdoOrganization");
                setting.AdoProject = ExtractJsonValue(json, "AdoProject");
                setting.AdoServerUrl = ExtractJsonValue(json, "AdoServerUrl");
                
                var lastUsedStr = ExtractJsonValue(json, "LastUsed");
                if (DateTime.TryParse(lastUsedStr, out DateTime lastUsed))
                {
                    setting.LastUsed = lastUsed;
                }
                
                return setting;
            }
            catch
            {
                return null;
            }
        }

        private string ExtractJsonValue(string json, string key)
        {
            var searchPattern = $"\"{key}\":\\s*\"";
            var startIndex = json.IndexOf(searchPattern);
            if (startIndex == -1) return string.Empty;
            
            startIndex += searchPattern.Length;
            var endIndex = json.IndexOf('"', startIndex);
            
            while (endIndex > 0 && json[endIndex - 1] == '\\')
            {
                endIndex = json.IndexOf('"', endIndex + 1);
            }
            
            if (endIndex == -1) return string.Empty;
            
            var value = json.Substring(startIndex, endIndex - startIndex);
            return UnescapeJsonString(value);
        }

        private string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return string.Empty;
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        private string UnescapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return string.Empty;
            return str.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
        }
    }
}