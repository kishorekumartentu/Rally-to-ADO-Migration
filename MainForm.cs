using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using Rally_to_ADO_Migration.Models;
using Rally_to_ADO_Migration.Services;

namespace Rally_to_ADO_Migration
{
    public partial class MainForm : Form
    {
        private readonly LoggingService _loggingService;
        private readonly string _organizationUrl;
        private readonly string _projectName;
        private readonly string _personalAccessToken;
        private readonly SettingsService _settingsService;
        private readonly MigrationService _migrationService;
        private System.Windows.Forms.Timer _progressTimer;
        private DateTime _migrationStartTime;
        private MigrationProgress _currentProgress;
        private readonly RallyApiService _rallyService;
        
        // Track the active Two-Phase migration service for pause/resume/cancel
        private TwoPhaseHierarchicalMigrationService _activeTwoPhaseService;

        public MainForm()
        {
            InitializeComponent();
            
            // Initialize services securely
            _loggingService = new LoggingService();
            _settingsService = new SettingsService();
            _migrationService = new MigrationService(_loggingService);
            _rallyService = new RallyApiService(_loggingService);

            InitializeServices();
            InitializeUI();
            LoadSavedSettings();
        }

        private void InitializeServices()
        {
            // Subscribe to migration events
            _migrationService.ProgressUpdated += OnMigrationProgressUpdated;
            _migrationService.StatusUpdated += OnMigrationStatusUpdated;

            // Initialize progress timer
            _progressTimer = new System.Windows.Forms.Timer();
            _progressTimer.Interval = 1000; // Update every second
            _progressTimer.Tick += OnProgressTimerTick;

            _loggingService.LogInfo("Application initialized");
        }

        private void InitializeUI()
        {
            // Initialize default values
            txtRallyServerUrl.Text = "https://rally1.rallydev.com";
            //txtAdoServerUrl.Text = "https://dev.azure.com";
            txtAdoServerUrl.Text = "https://emisgroup.visualstudio.com";

            // Make the Include Parents checkbox always checked and read-only
            // This communicates to users that parent items will always be migrated
            chkIncludeParents.Checked = true;
            chkIncludeParents.Enabled = false;

            // Initialize button states
            UpdateButtonStates(false);

            // Set initial status
            toolStripStatusLabel1.Text = "Ready - Configure connections to begin";

            // Configure progress bar
            progressBar1.Minimum = 0;
            progressBar1.Maximum = 100;
            progressBar1.Value = 0;

            // Configure rich text box for logs
            richTextBox1.ReadOnly = true;
            richTextBox1.ScrollBars = RichTextBoxScrollBars.Vertical;
        }

        private void LoadSavedSettings()
        {
            try
            {
                _loggingService.LogDebug("Loading saved settings list...");
                System.Diagnostics.Debug.WriteLine("[LoadSavedSettings] Starting...");
                
                var savedSettings = _settingsService.GetSavedSettings();
                System.Diagnostics.Debug.WriteLine($"[LoadSavedSettings] Got {savedSettings.Count} settings from service");
                
                cmbSavedSettings.Items.Clear();
                System.Diagnostics.Debug.WriteLine("[LoadSavedSettings] Cleared dropdown");
                
                // Filter out empty or invalid settings
                var validSettings = savedSettings
                    .Where(s => !string.IsNullOrWhiteSpace(s.Name))
                    .OrderByDescending(s => s.LastUsed)
                    .ToList();
                
                _loggingService.LogDebug($"Found {validSettings.Count} valid saved settings");
                System.Diagnostics.Debug.WriteLine($"[LoadSavedSettings] {validSettings.Count} valid settings after filtering");
                
                foreach (var setting in validSettings)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadSavedSettings] Adding '{setting.Name}' to dropdown");
                    cmbSavedSettings.Items.Add(setting.Name);
                    _loggingService.LogDebug($"  - {setting.Name} (Last used: {setting.LastUsed:yyyy-MM-dd HH:mm})");
                }

                System.Diagnostics.Debug.WriteLine($"[LoadSavedSettings] Dropdown now has {cmbSavedSettings.Items.Count} items");

                if (cmbSavedSettings.Items.Count > 0)
                {
                    cmbSavedSettings.SelectedIndex = 0;
                    _loggingService.LogDebug($"Selected most recent: {cmbSavedSettings.Items[0]}");
                    System.Diagnostics.Debug.WriteLine($"[LoadSavedSettings] Selected index 0: {cmbSavedSettings.Items[0]}");
                }
                else
                {
                    _loggingService.LogInfo("No saved settings found");
                    System.Diagnostics.Debug.WriteLine("[LoadSavedSettings] No settings to display");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to load saved settings list", ex);
                System.Diagnostics.Debug.WriteLine($"[LoadSavedSettings] ERROR: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[LoadSavedSettings] StackTrace: {ex.StackTrace}");
                AppendToLog("⚠️ Warning: Failed to load saved settings", Color.Orange);
                MessageBox.Show(
                    $"Failed to load saved settings:\n\n{ex.Message}\n\n" +
                    "You can still enter settings manually.",
                    "Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private ConnectionSettings GetCurrentConnectionSettings()
        {
            return new ConnectionSettings
            {
                RallyApiKey = txtRallyApiKey.Text.Trim(),
                RallyServerUrl = txtRallyServerUrl.Text.Trim(),
                RallyWorkspace = txtRallyWorkspace.Text.Trim(),
                RallyProject = txtRallyProject.Text.Trim(),
                AdoApiKey = txtAdoApiKey.Text.Trim(),
                AdoOrganization = txtAdoOrganization.Text.Trim(),
                AdoProject = txtAdoProject.Text.Trim(),
                AdoServerUrl = txtAdoServerUrl.Text.Trim()
            };
        }

        private string ShowInputDialog(string prompt, string defaultValue = "")
        {
            try
            {
                using (var form = new Form())
                {
                    form.Width = 500;
                    form.Height = 140;
                    form.Text = "Input";
                    form.StartPosition = FormStartPosition.CenterParent;

                    var lbl = new Label() { Left = 10, Top = 10, Text = prompt, Width = 460 };
                    var textBox = new TextBox() { Left = 10, Top = 35, Width = 460, Text = defaultValue };
                    var buttonOk = new Button() { Text = "OK", Left = 300, Width = 80, Top = 65, DialogResult = DialogResult.OK };
                    var buttonCancel = new Button() { Text = "Cancel", Left = 390, Width = 80, Top = 65, DialogResult = DialogResult.Cancel };

                    form.Controls.Add(lbl);
                    form.Controls.Add(textBox);
                    form.Controls.Add(buttonOk);
                    form.Controls.Add(buttonCancel);
                    form.AcceptButton = buttonOk;
                    form.CancelButton = buttonCancel;

                    var dr = form.ShowDialog();
                    if (dr == DialogResult.OK) return textBox.Text.Trim();
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        // Ensure FieldMappingConfiguration.json exists. If missing, offer to auto-generate via CompleteDynamicMappingGenerator and allow review.
        private async Task<bool> EnsureMappingExistsAndAskAsync(ConnectionSettings settings)
        {
            try
            {
                var mappingPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FieldMappingConfiguration.json");
                if (File.Exists(mappingPath)) return true;

                var ask = MessageBox.Show("FieldMappingConfiguration.json not found. Auto-generate mapping now? You can review before continuing.", "Generate Mapping", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ask != DialogResult.Yes) return false;

                AppendToLog("Auto-generating mapping configuration...", Color.Blue);
                var generator = new CompleteDynamicMappingGenerator(_loggingService);
                var generated = await generator.GenerateCompleteMappingFromApisAsync(settings);
                if (!string.IsNullOrEmpty(generated) && File.Exists(generated))
                {
                    AppendToLog($"Mapping generated: {generated}", Color.Green);
                    var open = MessageBox.Show("Open mapping file for review now?", "Review Mapping", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (open == DialogResult.Yes)
                    {
                        try { Process.Start("notepad.exe", generated); }
                        catch { Process.Start("explorer.exe", Path.GetDirectoryName(generated)); }

                        var cont = MessageBox.Show("Continue using this mapping?", "Confirm Continue", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        return cont == DialogResult.Yes;
                    }

                    var cont2 = MessageBox.Show("Proceed with migration using the auto-generated mapping?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    return cont2 == DialogResult.Yes;
                }

                MessageBox.Show("Failed to generate mapping file. Please generate mapping manually.", "Mapping Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"EnsureMappingExistsAndAskAsync failed: {ex.Message}");
                return false;
            }
        }

        private bool ValidateConnectionSettings(ConnectionSettings settings)
        {
            var errors = new List<string>();

            if (string.IsNullOrEmpty(settings.RallyApiKey))
                errors.Add("Rally API Key is required");
            if (string.IsNullOrEmpty(settings.RallyWorkspace))
                errors.Add("Rally Workspace is required");
            if (string.IsNullOrEmpty(settings.AdoApiKey))
                errors.Add("ADO API Key is required");
            if (string.IsNullOrEmpty(settings.AdoOrganization))
                errors.Add("ADO Organization is required");
            if (string.IsNullOrEmpty(settings.AdoProject))
                errors.Add("ADO Project is required");

            if (errors.Any())
            {
                MessageBox.Show(string.Join("\n", errors), "Validation Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private async void btnTestConnections_Click(object sender, EventArgs e)
        {
            var settings = GetCurrentConnectionSettings();
            if (!ValidateConnectionSettings(settings))
                return;

            btnTestConnections.Enabled = false;
            toolStripStatusLabel1.Text = "Testing connections...";
            AppendToLog("Testing connections...", Color.Blue);

            try
            {
                var success = await _migrationService.TestConnectionsAsync(settings);
                
                if (success)
                {
                    AppendToLog("✅ Connection test successful!", Color.Green);
                    toolStripStatusLabel1.Text = "Connections verified - Ready to migrate";
                    MessageBox.Show("Connection test successful! Both Rally and ADO connections are working.", 
                        "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    AppendToLog("❌ Connection test failed. Check your settings.", Color.Red);
                    toolStripStatusLabel1.Text = "Connection test failed";
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Connection test failed", ex);
                AppendToLog($"Connection test error: {ex.Message}", Color.Red);
                toolStripStatusLabel1.Text = "Connection test error";
            }
            finally
            {
                btnTestConnections.Enabled = true;
            }
        }

        private async void btnAdoFieldDiscovery_Click(object sender, EventArgs e)
        {
            var settings = GetCurrentConnectionSettings();
            if (!ValidateConnectionSettings(settings))
                return;

            btnAdoFieldDiscovery.Enabled = false;
            btnTestConnections.Enabled = false;
            toolStripStatusLabel1.Text = "Running comprehensive ADO field discovery...";
            AppendToLog("Starting Comprehensive ADO Field Discovery using Work Item Tracking REST API...", Color.Blue);

            try
            {
                // Run comprehensive ADO field discovery
                await AdoFieldDiscoveryRunner.RunComprehensiveAdoFieldDiscoveryAsync(settings, _loggingService);
                
                AppendToLog("? Comprehensive ADO Field Discovery completed successfully!", Color.Green);
                toolStripStatusLabel1.Text = "ADO Field Discovery completed - Check generated files";
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Comprehensive ADO field discovery failed", ex);
                AppendToLog($"? ADO Field Discovery error: {ex.Message}", Color.Red);
                toolStripStatusLabel1.Text = "ADO Field Discovery failed";
                MessageBox.Show($"ADO Field Discovery failed: {ex.Message}", "ADO Field Discovery Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnAdoFieldDiscovery.Enabled = true;
                btnTestConnections.Enabled = true;
            }
        }

        private async void btnRallyFieldDiscovery_Click(object sender, EventArgs e)
        {
            try
            {
                // Validate Rally connection settings
                if (string.IsNullOrEmpty(txtRallyApiKey.Text) ||
                    string.IsNullOrEmpty(txtRallyWorkspace.Text))
                {
                    MessageBox.Show(
                        "Please configure Rally connection settings first:\n" +
                        "- Rally API Key\n" +
                        "- Rally Workspace",
                        "Configuration Required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                // Disable button during discovery
                btnRallyFieldDiscovery.Enabled = false;
                btnRallyFieldDiscovery.Text = "⏳ Discovering Rally Fields...";

                // Get connection settings
                var settings = new ConnectionSettings
                {
                    RallyApiKey = txtRallyApiKey.Text.Trim(),
                    RallyServerUrl = txtRallyServerUrl.Text.Trim(),
                    RallyWorkspace = txtRallyWorkspace.Text.Trim(),
                    RallyProject = txtRallyProject.Text.Trim()
                };

                // Run Rally field discovery
                await RallyFieldDiscoveryRunner.RunComprehensiveRallyFieldDiscoveryAsync(
                    settings,
                    _loggingService);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Rally Field Discovery failed:\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                _loggingService.LogError("Rally field discovery failed", ex);
            }
            finally
            {
                // Re-enable button
                btnRallyFieldDiscovery.Enabled = true;
                btnRallyFieldDiscovery.Text = "🔍 Rally Field Discovery";
            }
        }

        private async void btnGenerateMapping_Click(object sender, EventArgs e)
        {
            var settings = GetCurrentConnectionSettings();
            if (!ValidateConnectionSettings(settings))
                return;

            try
            {
                // Let the user choose which generator to use
                var useApis = MessageBox.Show(
                    "Generate mapping from Rally/ADO APIs?\nYes = use APIs (CompleteDynamicMappingGenerator)\nNo = use discovery-based generator (AutomatedFieldMappingGenerator)",
                    "Generate Mapping", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

                var outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FieldMappingConfiguration.json");

                if (useApis)
                {
                    AppendToLog("Generating mapping from Rally and ADO APIs...", Color.Blue);
                    var generator = new CompleteDynamicMappingGenerator(_loggingService);
                    var generatedPath = await generator.GenerateCompleteMappingFromApisAsync(settings);
                    if (string.IsNullOrEmpty(generatedPath) || !File.Exists(generatedPath))
                    {
                        MessageBox.Show("Failed to generate mapping from APIs.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        AppendToLog("Failed to generate mapping from APIs", Color.Red);
                        return;
                    }
                    outputPath = generatedPath;
                }
                else
                {
                    // discovery-based generation: search recursively and prefer ADO_Field_Discovery_*.json
                    AppendToLog("Generating mapping from discovery JSON files...", Color.Blue);

                    var appDir = AppDomain.CurrentDomain.BaseDirectory;
                    string rallyJson = null;
                    string adoJson = null;

                    try
                    {
                        // Search for both old and new filename formats
                        var rallyCandidates = new List<string>();
                        rallyCandidates.AddRange(Directory.GetFiles(appDir, "RallyFieldDiscovery.json", SearchOption.AllDirectories));  // NEW FORMAT
                        //rallyCandidates.AddRange(Directory.GetFiles(appDir, "Rally_Field_Discovery.json", SearchOption.AllDirectories));  // OLD FORMAT
                        
                        rallyJson = rallyCandidates
                            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                            .FirstOrDefault();
                    }
                    catch { rallyJson = null; }

                    try
                    {
                        var adoCandidates = new List<string>();
                        adoCandidates.AddRange(Directory.GetFiles(appDir, "ADOFieldDiscovery.json", SearchOption.AllDirectories));  // NEW FORMAT - preferred
                        //adoCandidates.AddRange(Directory.GetFiles(appDir, "ADO_Field_Discovery.json", SearchOption.AllDirectories));  // OLD FORMAT
                        //adoCandidates.AddRange(Directory.GetFiles(appDir, "ADO_Fast_Complete_Discovery_*.json", SearchOption.AllDirectories));
                        //adoCandidates.AddRange(Directory.GetFiles(appDir, "ADO_Fast_Discovery_*.json", SearchOption.AllDirectories));
                        //adoCandidates.AddRange(Directory.GetFiles(appDir, "ADO_*_Discovery_*.json", SearchOption.AllDirectories));

                        adoJson = adoCandidates.OrderByDescending(f => new FileInfo(f).LastWriteTime).FirstOrDefault();
                    }
                    catch { adoJson = null; }

                    var generator = new AutomatedFieldMappingGenerator(_loggingService);
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    outputPath = Path.Combine(appDir, $"FieldMappingConfiguration_{timestamp}.json");

                    // If both files were found, pass them; otherwise let the generator auto-detect recursively (it already supports that)
                    if (!string.IsNullOrEmpty(rallyJson) && !string.IsNullOrEmpty(adoJson))
                    {
                        AppendToLog($"Using Rally discovery file: {rallyJson}", Color.Blue);
                        AppendToLog($"Using ADO discovery file: {adoJson}", Color.Blue);
                        generator.GenerateComprehensiveMappingJson(rallyJson, adoJson, outputPath);
                    }
                    else
                    {
                        AppendToLog("Discovery JSONs not found in base folder — invoking generator auto-detect (searches subfolders)", Color.Orange);
                        generator.GenerateComprehensiveMappingJson(null, null, outputPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error generating mapping", ex);
                MessageBox.Show($"Error generating mapping: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                AppendToLog($"Error generating mapping: {ex.Message}", Color.Red);
            }
        }

        //private async void btnDebugAdoApi_Click(object sender, EventArgs e)
        //{
        //    var settings = GetCurrentConnectionSettings();
        //    if (!ValidateConnectionSettings(settings))
        //        return;

        //    btnDebugAdoApi.Enabled = false;
        //    btnTestConnections.Enabled = false;
        //    toolStripStatusLabel1.Text = "Running comprehensive ADO API debug analysis...";
        //    AppendToLog("Starting Comprehensive ADO API Debug Analysis...", Color.Blue);

        //    try
        //    {
        //        // Create a comprehensive debug analysis
        //        var debugService = new AdoFieldDiscoveryService(_loggingService);
                
        //        AppendToLog("=== STEP 1: Basic ADO Connection Test ===", Color.Blue);
        //        var basicTest = await debugService.DiagnoseAdoConnectionAsync(settings);
        //        AppendToLog(basicTest, Color.Black);

        //        AppendToLog("=== STEP 2: Multiple API Endpoint Tests ===", Color.Blue);
        //        await TestMultipleAdoEndpoints(settings);
                
        //        AppendToLog("=== STEP 3: Field Discovery with Multiple Methods ===", Color.Blue);
        //        await TestFieldDiscoveryMethods(settings);
                
        //        AppendToLog("Comprehensive ADO API Debug Analysis completed!", Color.Green);
        //        toolStripStatusLabel1.Text = "ADO API Debug completed - Check log for detailed results";
                
        //        MessageBox.Show("Comprehensive ADO API debug analysis completed!\n\nCheck the log output for detailed analysis including:\n- Multiple API endpoint tests\n- Different authentication methods\n- Field discovery diagnostics\n- Raw JSON responses", 
        //            "Debug Analysis Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);

        //        var wantTestEmail = MessageBox.Show("Would you like to test an email address against ADO Graph users?", "Test Email", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        //        if (wantTestEmail == DialogResult.Yes)
        //        {
        //            var email = ShowInputDialog("Enter email to test:", "test.user@contoso.com");
        //            if (!string.IsNullOrWhiteSpace(email))
        //            {
        //                var adoService = new AdoApiService(_loggingService, settings.AdoServerUrl, settings.AdoProject, settings.AdoApiKey);
        //                AppendToLog($"Testing email existence in ADO Graph: {email}", Color.Blue);
        //                var exists = await adoService.FindUserByEmailAsync(settings, email);
        //                if (exists)
        //                {
        //                    AppendToLog($"User found for {email}", Color.Green);
        //                    MessageBox.Show($"User found for {email}", "User Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
        //                }
        //                else
        //                {
        //                    AppendToLog($"User not found for {email}", Color.Orange);
        //                    MessageBox.Show($"User not found for {email}", "User Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        //                }
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _loggingService.LogError("ADO API debug analysis failed", ex);
        //        AppendToLog($"Debug analysis error: {ex.Message}", Color.Red);
        //        toolStripStatusLabel1.Text = "ADO API debug failed";
        //        MessageBox.Show($"Debug analysis failed: {ex.Message}", "Debug Error", 
        //            MessageBoxButtons.OK, MessageBoxIcon.Error);
        //    }
        //    finally
        //    {
        //        btnDebugAdoApi.Enabled = true;
        //        btnTestConnections.Enabled = true;
        //    }
        //}

        private async Task TestMultipleAdoEndpoints(ConnectionSettings settings)
        {
            var adoService = new AdoApiService(_loggingService, _organizationUrl, _projectName, _personalAccessToken);

            // Test different URL formats
            var testUrls = new[]
            {
                $"https://dev.azure.com/{settings.AdoOrganization}/_apis/projects/{Uri.EscapeDataString(settings.AdoProject)}?api-version=6.0",
                $"https://{settings.AdoOrganization}.visualstudio.com/_apis/projects/{Uri.EscapeDataString(settings.AdoProject)}?api-version=6.0",
                $"https://dev.azure.com/{settings.AdoOrganization}/{Uri.EscapeDataString(settings.AdoProject)}/_apis/wit/workitemtypes?api-version=6.0",
                $"https://{settings.AdoOrganization}.visualstudio.com/{Uri.EscapeDataString(settings.AdoProject)}/_apis/wit/workitemtypes?api-version=6.0"
            };

            foreach (var url in testUrls)
            {
                try
                {
                    AppendToLog($"Testing endpoint: {url}", Color.Blue);
                    var response = await adoService.TestEndpointAsync(url, settings.AdoApiKey);
                    AppendToLog($"Success: {response.Substring(0, Math.Min(200, response.Length))}...", Color.Green);
                }
                catch (Exception ex)
                {
                    AppendToLog($"Failed: {ex.Message}", Color.Red);
                }
            }
        }

        private async Task TestFieldDiscoveryMethods(ConnectionSettings settings)
        {
            try
            {
                var discoveryService = new AdoFieldDiscoveryService(_loggingService);
                
                AppendToLog("Method 1: Standard Work Item Types API", Color.Blue);
                var result1 = await discoveryService.DiscoverAllAdoFieldsAsync(settings);
                AppendToLog($"Result: {result1?.WorkItemTypeFields?.Count ?? 0} work item types found", Color.Black);

                AppendToLog("Method 2: Enhanced Field Discovery", Color.Blue);
                var enhancedService = new FastAdoFieldDiscoveryService(_loggingService);
                var result2 = await enhancedService.DiscoverAllAdoFieldsFastAsync(settings);
                AppendToLog($"Result: {result2?.WorkItemTypeFields?.Count ?? 0} work item types found", Color.Black);

                AppendToLog("Method 3: Direct API Test", Color.Blue);
                var debugResult = await discoveryService.DiagnoseAdoConnectionAsync(settings);
                AppendToLog($"Direct API test result length: {debugResult?.Length ?? 0} characters", Color.Black);
            }
            catch (Exception ex)
            {
                AppendToLog($"Field discovery methods test error: {ex.Message}", Color.Red);
            }
        }

        private void btnLoadSettings_Click(object sender, EventArgs e)
        {
            try
            {
                if (cmbSavedSettings.SelectedItem == null)
                {
                    MessageBox.Show(
                        "Please select a saved setting from the dropdown to load.",
                        "No Selection",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                var settingName = cmbSavedSettings.SelectedItem.ToString();
                
                _loggingService.LogInfo($"Loading settings: {settingName}");
                
                var setting = _settingsService.GetSettingsByName(settingName);
                
                if (setting != null)
                {
                    _loggingService.LogDebug($"Found setting: {settingName}");
                    _loggingService.LogDebug($"  Rally Workspace: {setting.RallyWorkspace}");
                    _loggingService.LogDebug($"  ADO Organization: {setting.AdoOrganization}");
                    _loggingService.LogDebug($"  ADO Project: {setting.AdoProject}");
                    
                    // Check if the setting has any actual data
                    if (string.IsNullOrWhiteSpace(setting.RallyWorkspace) &&
                        string.IsNullOrWhiteSpace(setting.AdoOrganization) &&
                        string.IsNullOrWhiteSpace(setting.AdoProject))
                    {
                        MessageBox.Show(
                            $"The saved setting '{settingName}' appears to be empty.\n\n" +
                            "This may be caused by a previous save error.\n" +
                            "Please delete this setting and create a new one.",
                            "Empty Setting",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }
                    
                    // Load the settings into the form
                    txtRallyServerUrl.Text = setting.RallyServerUrl ?? "https://rally1.rallydev.com";
                    txtRallyWorkspace.Text = setting.RallyWorkspace ?? "";
                    txtRallyProject.Text = setting.RallyProject ?? "";
                    txtAdoServerUrl.Text = setting.AdoServerUrl ?? "https://emisgroup.visualstudio.com";
                    txtAdoOrganization.Text = setting.AdoOrganization ?? "";
                    txtAdoProject.Text = setting.AdoProject ?? "";

                    // Clear API keys for security
                    txtRallyApiKey.Text = "";
                    txtAdoApiKey.Text = "";

                    _settingsService.UpdateLastUsed(settingName);
                    
                    AppendToLog($"✅ Loaded settings: {settingName}", Color.Green);
                    toolStripStatusLabel1.Text = $"Loaded: {settingName} - Please enter API keys";
                    
                    MessageBox.Show(
                        $"Settings '{settingName}' loaded successfully!\n\n" +
                        "⚠️ Please enter your API keys:\n" +
                        "  • Rally API Key\n" +
                        "  • Azure DevOps API Key\n\n" +
                        "(API keys are not saved for security reasons)",
                        "Settings Loaded",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    _loggingService.LogWarning($"Setting not found: {settingName}");
                    MessageBox.Show(
                        $"Could not find setting '{settingName}'.\n\n" +
                        "The settings file may be corrupted.",
                        "Setting Not Found",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error loading settings", ex);
                AppendToLog($"❌ Error loading settings: {ex.Message}", Color.Red);
                MessageBox.Show(
                    $"Error loading settings:\n\n{ex.Message}",
                    "Load Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void btnSaveSettings_Click(object sender, EventArgs e)
        {
            try
            {
                // Validate that there are settings to save
                var settings = GetCurrentConnectionSettings();
                
                // Check if at least some fields are filled
                if (string.IsNullOrWhiteSpace(settings.RallyWorkspace) &&
                    string.IsNullOrWhiteSpace(settings.AdoOrganization) &&
                    string.IsNullOrWhiteSpace(settings.AdoProject))
                {
                    MessageBox.Show(
                        "Please fill in at least the basic connection settings before saving:\n" +
                        "- Rally Workspace\n" +
                        "- ADO Organization\n" +
                        "- ADO Project",
                        "No Settings to Save",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                using (var form = new Form())
                {
                    form.Text = "Save Settings";
                    form.Size = new Size(400, 180);
                    form.FormBorderStyle = FormBorderStyle.FixedDialog;
                    form.MaximizeBox = false;
                    form.MinimizeBox = false;
                    form.StartPosition = FormStartPosition.CenterParent;

                    var label = new Label() 
                    { 
                        Left = 20, 
                        Top = 20, 
                        Width = 360,
                        Text = "Enter a name for these settings:" 
                    };
                    
                    var infoLabel = new Label()
                    {
                        Left = 20,
                        Top = 40,
                        Width = 360,
                        ForeColor = Color.Blue,
                        Text = "(API keys will NOT be saved for security)"
                    };
                    
                    var textBox = new TextBox() 
                    { 
                        Left = 20, 
                        Top = 70, 
                        Width = 360 
                    };
                    
                    var buttonOk = new Button() 
                    { 
                        Text = "Save", 
                        Left = 200, 
                        Width = 80, 
                        Top = 110, 
                        DialogResult = DialogResult.OK 
                    };
                    
                    var buttonCancel = new Button() 
                    { 
                        Text = "Cancel", 
                        Left = 290, 
                        Width = 80, 
                        Top = 110, 
                        DialogResult = DialogResult.Cancel 
                    };

                    form.Controls.Add(label);
                    form.Controls.Add(infoLabel);
                    form.Controls.Add(textBox);
                    form.Controls.Add(buttonOk);
                    form.Controls.Add(buttonCancel);
                    form.AcceptButton = buttonOk;
                    form.CancelButton = buttonCancel;

                    var dialogResult = form.ShowDialog();
                    
                    if (dialogResult == DialogResult.OK)
                    {
                        var settingName = textBox.Text.Trim();
                        
                        if (string.IsNullOrWhiteSpace(settingName))
                        {
                            MessageBox.Show(
                                "Please enter a valid name for the settings.",
                                "Name Required",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                            return;
                        }

                        try
                        {
                            _loggingService.LogInfo($"Saving connection settings as '{settingName}'");
                            _loggingService.LogDebug($"Rally Workspace: {settings.RallyWorkspace}");
                            _loggingService.LogDebug($"ADO Organization: {settings.AdoOrganization}");
                            _loggingService.LogDebug($"ADO Project: {settings.AdoProject}");
                            
                            _settingsService.SaveConnectionSettings(settingName, settings);
                            
                            LoadSavedSettings(); // Refresh the dropdown
                            
                            // Select the newly saved setting
                            for (int i = 0; i < cmbSavedSettings.Items.Count; i++)
                            {
                                if (cmbSavedSettings.Items[i].ToString() == settingName)
                                {
                                    cmbSavedSettings.SelectedIndex = i;
                                    break;
                                }
                            }
                            
                            AppendToLog($"✅ Settings saved: {settingName}", Color.Green);
                            MessageBox.Show(
                                $"Settings saved successfully as '{settingName}'\n\n" +
                                "Note: API keys are NOT saved for security reasons.\n" +
                                "You will need to re-enter them when loading these settings.",
                                "Settings Saved",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogError("Failed to save settings", ex);
                            AppendToLog($"❌ Failed to save settings: {ex.Message}", Color.Red);
                            MessageBox.Show(
                                $"Failed to save settings:\n\n{ex.Message}",
                                "Save Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Error in Save Settings", ex);
                MessageBox.Show(
                    $"Error saving settings:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private async void btnStartMigration_Click(object sender, EventArgs e)
        {
            var settings = GetCurrentConnectionSettings();
            if (!ValidateConnectionSettings(settings))
                return;

            // ensure mapping exists
            var mappingOk = await EnsureMappingExistsAndAskAsync(settings);
            if (!mappingOk) return;

            // Ask user which migration approach to use
            var dialogResult = MessageBox.Show(
                "🚀 TWO-PHASE HIERARCHICAL MIGRATION\n\n" +
                "Would you like to use the enhanced two-phase migration?\n\n" +
                "✅ YES - Recommended (Two-Phase Migration)\n" +
                "   • Complete hierarchy preservation (Epic→Feature→Story→Task)\n" +
                "   • All parent-child relationships maintained\n" +
                "   • Test Cases linked to User Stories/Defects\n" +
                "   • No duplicate work items\n" +
                "   • Full Rally→ADO traceability\n\n" +
                "❌ NO - Legacy Migration (Original Method)\n" +
                "   • Basic migration without full hierarchy\n" +
                "   • Some parent-child links may be missing\n\n" +
                "Recommended: Click YES for complete migration",
                "Choose Migration Approach",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (dialogResult == DialogResult.Cancel)
                return;

            bool useTwoPhase = (dialogResult == DialogResult.Yes);

            // Confirm migration start
            var result = MessageBox.Show(
                useTwoPhase
                    ? "Start TWO-PHASE migration?\n\nThis will:\n• Fetch ALL work items from Rally\n• Create them in ADO in hierarchy order\n• Establish ALL parent-child relationships\n• Link Test Cases to User Stories\n\nNo duplicates will be created (existing items will be updated)."
                    : "Start LEGACY migration?\n\nNote: This may miss some parent-child links.",
                "Confirm Migration",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            try
            {
                _migrationStartTime = DateTime.Now;
                UpdateButtonStates(true);
                ResetStatistics();
                
                AppendToLog(useTwoPhase ? "=== Starting Two-Phase Hierarchical Rally to ADO Migration ===" : "=== Starting Legacy Rally to ADO Migration ===", Color.Blue);
                AppendToLog($"Started at: {_migrationStartTime:yyyy-MM-dd HH:mm:ss}", Color.Blue);
                
                _progressTimer.Start();
                tabControl1.SelectedTab = tabPage2; // Switch to migration tab

                if (useTwoPhase)
                {
                    // Use two-phase hierarchical migration with full synchronization
                    var twoPhaseService = new TwoPhaseHierarchicalMigrationService(_loggingService, AppendToLog);
                    _activeTwoPhaseService = twoPhaseService; // Store reference for pause/resume/cancel
                    
                    try
                    {
                        // Subscribe to progress updates for real-time UI feedback
                        twoPhaseService.ProgressUpdated += OnMigrationProgressUpdated;
                        
                        // CRITICAL: Enable difference patch to update existing items with comments/attachments
                        twoPhaseService.EnableDifferencePatch = true;
                        AppendToLog("✅ Full synchronization enabled (will update existing items with comments/attachments)", Color.Blue);
                        AppendToLog("⏸️ Use Pause/Resume/Cancel buttons to control migration", Color.Cyan);
                        
                        _currentProgress = await twoPhaseService.MigrateWithFullHierarchyAsync(
                            settings,
                            rallyIds: null,
                            migrateEntireProject: true);
                    }
                    finally
                    {
                        // Unsubscribe from events
                        twoPhaseService.ProgressUpdated -= OnMigrationProgressUpdated;
                        _activeTwoPhaseService = null; // Clear reference
                        twoPhaseService.Dispose();
                    }
                }
                else
                {
                    // Use legacy migration service
                    _currentProgress = await _migrationService.StartMigrationAsync(settings);
                }
                
                AppendToLog("=== Migration Completed ===", Color.Green);
                AppendToLog($"Total items: {_currentProgress.TotalItems}", Color.Black);
                AppendToLog($"Successful: {_currentProgress.SuccessfulItems}", Color.Green);
                AppendToLog($"Failed: {_currentProgress.FailedItems}", Color.Red);
                AppendToLog($"Skipped: {_currentProgress.SkippedItems}", Color.Orange);
                
                MessageBox.Show($"Migration completed!\n\nTotal: {_currentProgress.TotalItems}\nSuccessful: {_currentProgress.SuccessfulItems}\nFailed: {_currentProgress.FailedItems}\nSkipped: {_currentProgress.SkippedItems}", 
                    "Migration Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Migration failed", ex);
                AppendToLog($"Migration failed: {ex.Message}", Color.Red);
                MessageBox.Show($"Migration failed: {ex.Message}", "Migration Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _progressTimer.Stop();
                UpdateButtonStates(false);
            }
        }

        private void btnPause_Click(object sender, EventArgs e)
        {
            // Use Two-Phase service if active, otherwise legacy service
            if (_activeTwoPhaseService != null)
            {
                _activeTwoPhaseService.PauseMigration();
                btnPause.Enabled = false;
                btnResume.Enabled = true;
            }
            else
            {
                _migrationService.PauseMigration();
                AppendToLog("Migration paused by user", Color.Orange);
                btnPause.Enabled = false;
                btnResume.Enabled = true;
            }
        }

        private void btnResume_Click(object sender, EventArgs e)
        {
            // Use Two-Phase service if active, otherwise legacy service
            if (_activeTwoPhaseService != null)
            {
                _activeTwoPhaseService.ResumeMigration();
                btnPause.Enabled = true;
                btnResume.Enabled = false;
            }
            else
            {
                _migrationService.ResumeMigration();
                AppendToLog("Migration resumed by user", Color.Blue);
                btnPause.Enabled = true;
                btnResume.Enabled = false;
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to cancel the migration?", 
                "Confirm Cancel", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                // Use Two-Phase service if active, otherwise legacy service
                if (_activeTwoPhaseService != null)
                {
                    _activeTwoPhaseService.CancelMigration();
                }
                else
                {
                    _migrationService.CancelMigration();
                    AppendToLog("Migration cancelled by user", Color.Red);
                }
            }
        }

        private void btnViewLogs_Click(object sender, EventArgs e)
        {
            try
            {
                var logFile = _loggingService.GetLogFileName();
                if (File.Exists(logFile))
                {
                    Process.Start("notepad.exe", logFile);
                }
                else
                {
                    MessageBox.Show("Log file not found.", "File Not Found", 
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open log file: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnOpenReportsFolder_Click(object sender, EventArgs e)
        {
            try
            {
                var reportsFolder = _loggingService.GetLogDirectory();
                if (Directory.Exists(reportsFolder))
                {
                    Process.Start("explorer.exe", reportsFolder);
                }
                else
                {
                    MessageBox.Show("Reports folder not found.", "Folder Not Found", 
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open reports folder: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnMigrationProgressUpdated(object sender, MigrationProgress progress)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<object, MigrationProgress>(OnMigrationProgressUpdated), sender, progress);
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[OnMigrationProgressUpdated] Called on UI thread. Total={progress.TotalItems}, Processed={progress.ProcessedItems}");
            
            _currentProgress = progress;
            UpdateStatistics(progress);
            UpdateProgressBar(progress);
        }

        private void OnMigrationStatusUpdated(object sender, string status)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<object, string>(OnMigrationStatusUpdated), sender, status);
                return;
            }

            AppendToLog(status, Color.Black);
            toolStripStatusLabel1.Text = status;
        }

        private void OnProgressTimerTick(object sender, EventArgs e)
        {
            if (_currentProgress != null)
            {
                var elapsed = DateTime.Now - _migrationStartTime;
                lblElapsedTime.Text = $"Elapsed: {elapsed:hh\\:mm\\:ss}";
            }
        }

        private void UpdateButtonStates(bool migrationRunning)
        {
            btnStartMigration.Enabled = !migrationRunning;
            btnMigrateSelected.Enabled = !migrationRunning;
            btnValidateIds.Enabled = !migrationRunning;
            btnPause.Enabled = migrationRunning;
            btnResume.Enabled = false; // Resume starts disabled
            btnCancel.Enabled = migrationRunning;
            btnTestConnections.Enabled = !migrationRunning;
        }

        private void ResetStatistics()
        {
            lblTotal.Text = "0";
            lblProcessed.Text = "0";
            lblSuccessful.Text = "0";
            lblFailed.Text = "0";
            lblSkipped.Text = "0";
            progressBar1.Value = 0;
            lblProgressText.Text = "Initializing...";
            lblElapsedTime.Text = "Elapsed: 00:00:00";
        }

        private void UpdateStatistics(MigrationProgress progress)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateStatistics] Total={progress.TotalItems}, Processed={progress.ProcessedItems}, Success={progress.SuccessfulItems}, Failed={progress.FailedItems}, Skipped={progress.SkippedItems}");
            
            lblTotal.Text = progress.TotalItems.ToString();
            lblProcessed.Text = progress.ProcessedItems.ToString();
            lblSuccessful.Text = progress.SuccessfulItems.ToString();
            lblFailed.Text = progress.FailedItems.ToString();
            lblSkipped.Text = progress.SkippedItems.ToString();
            
            // Force UI refresh
            lblTotal.Refresh();
            lblProcessed.Refresh();
            lblSuccessful.Refresh();
            lblFailed.Refresh();
            lblSkipped.Refresh();
        }

        private void UpdateProgressBar(MigrationProgress progress)
        {
            if (progress.TotalItems > 0)
            {
                var percentage = (int)progress.ProgressPercentage;
                progressBar1.Value = Math.Min(percentage, 100);
                lblProgressText.Text = $"Processing... {progress.ProcessedItems}/{progress.TotalItems} ({percentage}%)";
            }
        }

        private void AppendToLog(string message, Color color)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, Color>(AppendToLog), message, color);
                return;
            }

            richTextBox1.SelectionStart = richTextBox1.TextLength;
            richTextBox1.SelectionLength = 0;
            richTextBox1.SelectionColor = color;
            richTextBox1.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            richTextBox1.SelectionColor = richTextBox1.ForeColor;
            richTextBox1.ScrollToCaret();
        }

        private async void btnValidateIds_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtRallyIds.Text))
            {
                MessageBox.Show("Please enter Rally IDs to validate.", "No IDs Provided", 
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var settings = GetCurrentConnectionSettings();
            if (!ValidateConnectionSettings(settings))
                return;

            btnValidateIds.Enabled = false;
            btnMigrateSelected.Enabled = false;
            toolStripStatusLabel1.Text = "Validating Rally IDs...";
            
            try
            {
                var rallyIds = ParseRallyIds(txtRallyIds.Text);
                var validationResults = await _migrationService.ValidateRallyIdsAsync(settings, rallyIds, chkIncludeParents.Checked);
                
                DisplayValidationResults(validationResults);
                
                // Enable migration button only if we have valid IDs
                btnMigrateSelected.Enabled = validationResults.ValidIds.Any();
                
                if (validationResults.InvalidIds.Any())
                {
                    var invalidMessage = $"Invalid Rally IDs found:\n{string.Join(", ", validationResults.InvalidIds)}";
                    MessageBox.Show(invalidMessage, "Validation Warning", 
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show($"All {validationResults.ValidIds.Count} Rally IDs are valid and ready for migration!", 
                        "Validation Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Rally ID validation failed", ex);
                MessageBox.Show($"Validation failed: {ex.Message}", "Validation Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnValidateIds.Enabled = true;
                toolStripStatusLabel1.Text = "Ready";
            }
        }

        private async void btnMigrateSelected_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtRallyIds.Text))
            {
                MessageBox.Show("Please enter and validate Rally IDs first.", "No IDs Provided", 
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var settings = GetCurrentConnectionSettings();
            if (!ValidateConnectionSettings(settings))
                return;

            // ensure mapping exists
            var mappingOk = await EnsureMappingExistsAndAskAsync(settings);
            if (!mappingOk) return;

            var rallyIds = ParseRallyIds(txtRallyIds.Text);
            
            var confirmMessage = $"🚀 TWO-PHASE SELECTIVE MIGRATION\n\n" +
                                $"Migrate {rallyIds.Count} Rally work item(s) with FULL HIERARCHY?\n\n" +
                                $"✅ Features:\n" +
                                $"• Complete dependency tree (parents + children automatically included)\n" +
                                $"• All parent-child links preserved\n" +
                                $"• Test Cases linked to Stories/Defects\n" +
                                $"• No duplicates (existing items updated)\n" +
                                $"• Full Rally→ADO traceability\n\n" +
                                $"ℹ️ Note: Parent and test case dependencies are automatically discovered and migrated.\n\n" +
                                $"Rally IDs: {string.Join(", ", rallyIds.Take(5))}{(rallyIds.Count > 5 ? "..." : "")}";

            var result = MessageBox.Show(confirmMessage, "Confirm Selective Migration", 
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            try
            {
                _migrationStartTime = DateTime.Now;
                UpdateButtonStates(true);
                ResetStatistics();
                
                AppendToLog("=== Starting Two-Phase Selective Rally Migration ===", Color.Blue);
                AppendToLog($"Rally IDs: {string.Join(", ", rallyIds)}", Color.Blue);
                AppendToLog("ℹ️ Parent and test case dependencies will be automatically discovered", Color.Blue);
                AppendToLog($"Started at: {_migrationStartTime:yyyy-MM-dd HH:mm:ss}", Color.Blue);
                
                _progressTimer.Start();
                tabControl1.SelectedTab = tabPage2; // Switch to migration tab

                // Use two-phase hierarchical migration for selected IDs with full synchronization
                var twoPhaseService = new TwoPhaseHierarchicalMigrationService(_loggingService, AppendToLog);
                _activeTwoPhaseService = twoPhaseService; // Store reference for pause/resume/cancel
                
                try
                {
                    // Subscribe to progress updates for real-time UI feedback
                    twoPhaseService.ProgressUpdated += OnMigrationProgressUpdated;
                    
                    // CRITICAL: Enable difference patch to update existing items with comments/attachments
                    twoPhaseService.EnableDifferencePatch = true;
                    AppendToLog("✅ Full synchronization enabled (will update existing items with comments/attachments)", Color.Blue);
                    AppendToLog("⏸️ Use Pause/Resume/Cancel buttons to control migration", Color.Cyan);
                    
                    // TwoPhaseHierarchicalMigrationService automatically discovers and includes parent/test case dependencies
                    // No need for manual parent pre-fetching
                    _currentProgress = await twoPhaseService.MigrateWithFullHierarchyAsync(
                        settings,
                        rallyIds: rallyIds,
                        migrateEntireProject: false);
                }
                finally
                {
                    // Unsubscribe from events
                    twoPhaseService.ProgressUpdated -= OnMigrationProgressUpdated;
                    _activeTwoPhaseService = null; // Clear reference
                    twoPhaseService.Dispose();
                }
                
                AppendToLog("=== Selective Migration Completed ===", Color.Green);
                AppendToLog($"Total items: {_currentProgress.TotalItems}", Color.Black);
                AppendToLog($"Successful: {_currentProgress.SuccessfulItems}", Color.Green);
                AppendToLog($"Failed: {_currentProgress.FailedItems}", Color.Red);
                AppendToLog($"Skipped: {_currentProgress.SkippedItems}", Color.Orange);
                
                MessageBox.Show($"Selective migration completed!\n\nTotal: {_currentProgress.TotalItems}\nSuccessful: {_currentProgress.SuccessfulItems}\nFailed: {_currentProgress.FailedItems}\nSkipped: {_currentProgress.SkippedItems}", 
                    "Migration Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Selective migration failed", ex);
                AppendToLog($"Selective migration failed: {ex.Message}", Color.Red);
                MessageBox.Show($"Selective migration failed: {ex.Message}", "Migration Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _progressTimer.Stop();
                UpdateButtonStates(false);
            }
        }

        private List<string> ParseRallyIds(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new List<string>();

            return input.Split(new char[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(id => id.Trim())
                       .Where(id => !string.IsNullOrEmpty(id))
                       .Distinct()
                       .ToList();
        }

        private void DisplayValidationResults(RallyIdValidationResult validationResults)
        {
            AppendToLog("=== Rally ID Validation Results ===", Color.Blue);
            
            if (validationResults.ValidIds.Any())
            {
                AppendToLog($"? Valid IDs ({validationResults.ValidIds.Count}):", Color.Green);
                foreach (var id in validationResults.ValidIds)
                {
                    AppendToLog($"  - {id}", Color.Green);
                }
            }

            if (validationResults.InvalidIds.Any())
            {
                AppendToLog($"? Invalid IDs ({validationResults.InvalidIds.Count}):", Color.Red);
                foreach (var id in validationResults.InvalidIds)
                {
                    AppendToLog($"  - {id}", Color.Red);
                }
            }

            if (validationResults.AlreadyMigrated.Any())
            {
                AppendToLog($"? Already Migrated ({validationResults.AlreadyMigrated.Count}):", Color.Orange);
                foreach (var id in validationResults.AlreadyMigrated)
                {
                    AppendToLog($"  - {id}", Color.Orange);
                }
            }

            if (validationResults.ParentItemsFound.Any())
            {
                AppendToLog($"Parent Items to Include ({validationResults.ParentItemsFound.Count}):", Color.Blue);
                foreach (var parent in validationResults.ParentItemsFound)
                {
                    AppendToLog($"  - {parent}", Color.Blue);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_migrationService != null && !_migrationService.IsCancellationRequested)
            {
                var result = MessageBox.Show(
                    "A migration may be in progress. Are you sure you want to close the application?",
                    "Confirm Close", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                _migrationService.CancelMigration();
            }

            _progressTimer?.Stop();
            _progressTimer?.Dispose();
            _migrationService?.Dispose();
            _loggingService?.LogInfo("Application closing");

            base.OnFormClosing(e);
        }

        //private void btnAutoMapFields_Click(object sender, EventArgs e)
        //{

        //}
        ////private void btnGenerateFieldMapping_Click(object sender, EventArgs e)
        ////{
        ////    try
        ////    {
        ////        AppendToLog("=== Starting Comprehensive Field Mapping Generation ===", Color.Blue);

        ////        var appDir = AppDomain.CurrentDomain.BaseDirectory;

        ////        // Find most recent Rally Field Discovery JSON
        ////        var rallyJsonFiles = Directory.GetFiles(appDir, "Rally_Field_Discovery_*.json")
        ////            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
        ////            .ToList();

        ////        // Find most recent ADO Field Discovery JSON  
        ////        var adoJsonFiles = Directory.GetFiles(appDir, "ADO_Fast_Complete_Discovery_*.json")
        ////            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
        ////            .ToList();

        ////        string rallyJsonPath = null;
        ////        string adoJsonPath = null;

        ////        // Check for Rally discovery files
        ////        if (rallyJsonFiles.Any())
        ////        {
        ////            rallyJsonPath = rallyJsonFiles.First();
        ////            AppendToLog($"✅ Found Rally Field Discovery: {Path.GetFileName(rallyJsonPath)}", Color.Green);
        ////        }
        ////        else
        ////        {
        ////            AppendToLog("❌ No Rally Field Discovery JSON found. Please run Rally Field Discovery first.", Color.Red);
        ////            MessageBox.Show(
        ////                "Rally Field Discovery JSON not found!\n\n" +
        ////                "Please:\n" +
        ////                "1. Fill in Rally connection settings\n" +
        ////                "2. Click '🔍 Rally Field Discovery' button\n" +
        ////                "3. Wait for completion\n" +
        ////                "4. Then run Auto Map Fields again",
        ////                "Missing Rally Discovery Data",
        ////                MessageBoxButtons.OK,
        ////                MessageBoxIcon.Warning);
        ////            return;
        ////        }

        //        // Check for ADO discovery files
        //        if (adoJsonFiles.Any())
        //        {
        //            adoJsonPath = adoJsonFiles.First();
        //            AppendToLog($"✅ Found ADO Field Discovery: {Path.GetFileName(adoJsonPath)}", Color.Green);
        //        }
        //        else
        //        {
        //            AppendToLog("❌ No ADO Field Discovery JSON found. Please run ADO Field Discovery first.", Color.Red);
        //            MessageBox.Show(
        //                "ADO Field Discovery JSON not found!\n\n" +
        //                "Please:\n" +
        //                "1. Fill in ADO connection settings\n" +
        //                "2. Click '🔍 ADO Field Discovery' button\n" +
        //                "3. Wait for completion\n" +
        //                "4. Then run Auto Map Fields again",
        //                "Missing ADO Discovery Data",
        //                MessageBoxButtons.OK,
        //                MessageBoxIcon.Warning);
        //            return;
        //        }

        //        // Generate output path with timestamp
        //        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        //        var outputMappingPath = Path.Combine(appDir, $"FieldMappingConfiguration_{timestamp}.json");

        //        AppendToLog($"Rally JSON: {rallyJsonPath}", Color.Blue);
        //        AppendToLog($"ADO JSON: {adoJsonPath}", Color.Blue);
        //        AppendToLog($"Output: {outputMappingPath}", Color.Blue);
        //        AppendToLog("", Color.Black);
        //        AppendToLog("Analyzing field structures...", Color.Blue);

        //        // Create generator with logging
        //        var generator = new AutomatedFieldMappingGenerator(_loggingService);

        //        // Generate comprehensive mapping using the correct method
        //        generator.GenerateComprehensiveMappingJson(rallyJsonPath, adoJsonPath, outputMappingPath);

        //        AppendToLog("", Color.Black);
        //        AppendToLog("✅ Field Mapping Generation Complete!", Color.Green);
        //        AppendToLog($"Output file: {Path.GetFileName(outputMappingPath)}", Color.Green);

        //        // Show success message with details
        //        var message = $"✅ Comprehensive Field Mapping Generated!\n\n" +
        //                     $"Output File:\n{Path.GetFileName(outputMappingPath)}\n\n" +
        //                     $"📋 What was generated:\n" +
        //                     $"• Intelligent field mappings for all Rally → ADO types\n" +
        //                     $"• Confidence levels (High/Medium/Low/None)\n" +
        //                     $"• Transformation types (DIRECT/STATE_TRANSFORM/etc.)\n" +
        //                     $"• Review flags for uncertain mappings\n\n" +
        //                     $"📝 Next Steps:\n" +
        //                     $"1. Review the generated JSON file\n" +
        //                     $"2. Adjust mappings marked 'RequiresReview: true'\n" +
        //                     $"3. Customize transformations as needed\n" +
        //                     $"4. Use for migration\n\n" +
        //                     $"Would you like to open the file location?";

        //        var result = MessageBox.Show(message, "Field Mapping Complete", 
        //            MessageBoxButtons.YesNo, MessageBoxIcon.Information);

        //        if (result == DialogResult.Yes)
        //        {
        //            try
        //            {
        //                // Try to select the file in explorer
        //                Process.Start("explorer.exe", $"/select,\"{outputMappingPath}\"");
        //            }
        //            catch (Exception ex)
        //            {
        //                _loggingService.LogWarning($"Could not open file location: {ex.Message}");
        //                // Fallback: just open the directory
        //                Process.Start("explorer.exe", appDir);
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _loggingService.LogError("Field mapping generation failed", ex);
        //        AppendToLog($"❌ Error: {ex.Message}", Color.Red);
        //        MessageBox.Show(
        //            $"Field Mapping Generation failed:\n\n{ex.Message}\n\n" +
        //            $"Please ensure:\n" +
        //            $"1. Rally Field Discovery JSON exists\n" +
        //            $"2. ADO Field Discovery JSON exists\n" +
        //            $"3. JSON files are valid\n\n" +
        //            $"Check the log for details.",
        //            "Error",
        //            MessageBoxButtons.OK,
        //            MessageBoxIcon.Error);
        //    }
        //}
    }
}