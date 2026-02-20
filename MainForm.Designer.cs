namespace Rally_to_ADO_Migration
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.groupBoxSelectiveMigration = new System.Windows.Forms.GroupBox();
            this.lblSelectiveInstructions = new System.Windows.Forms.Label();
            this.chkIncludeParents = new System.Windows.Forms.CheckBox();
            this.btnMigrateSelected = new System.Windows.Forms.Button();
            this.btnValidateIds = new System.Windows.Forms.Button();
            this.txtRallyIds = new System.Windows.Forms.TextBox();
            this.lblRallyIds = new System.Windows.Forms.Label();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.txtAdoProject = new System.Windows.Forms.TextBox();
            this.label8 = new System.Windows.Forms.Label();
            this.txtAdoOrganization = new System.Windows.Forms.TextBox();
            this.label7 = new System.Windows.Forms.Label();
            this.txtAdoServerUrl = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.txtAdoApiKey = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.txtRallyProject = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.txtRallyWorkspace = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.txtRallyServerUrl = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.txtRallyApiKey = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.btnGenerateMapping = new System.Windows.Forms.Button();
            this.btnSaveSettings = new System.Windows.Forms.Button();
            this.btnLoadSettings = new System.Windows.Forms.Button();
            this.cmbSavedSettings = new System.Windows.Forms.ComboBox();
            this.btnTestConnections = new System.Windows.Forms.Button();
            this.btnAdoFieldDiscovery = new System.Windows.Forms.Button();
            this.btnRallyFieldDiscovery = new System.Windows.Forms.Button();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.richTextBox1 = new System.Windows.Forms.RichTextBox();
            this.panel3 = new System.Windows.Forms.Panel();
            this.btnViewLogs = new System.Windows.Forms.Button();
            this.btnOpenReportsFolder = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnResume = new System.Windows.Forms.Button();
            this.btnPause = new System.Windows.Forms.Button();
            this.btnStartMigration = new System.Windows.Forms.Button();
            this.panel2 = new System.Windows.Forms.Panel();
            this.lblElapsedTime = new System.Windows.Forms.Label();
            this.lblProgressText = new System.Windows.Forms.Label();
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.lblSkipped = new System.Windows.Forms.Label();
            this.lblFailed = new System.Windows.Forms.Label();
            this.lblSuccessful = new System.Windows.Forms.Label();
            this.lblProcessed = new System.Windows.Forms.Label();
            this.lblTotal = new System.Windows.Forms.Label();
            this.label15 = new System.Windows.Forms.Label();
            this.label14 = new System.Windows.Forms.Label();
            this.label13 = new System.Windows.Forms.Label();
            this.label12 = new System.Windows.Forms.Label();
            this.label11 = new System.Windows.Forms.Label();
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.toolStripStatusLabel1 = new System.Windows.Forms.ToolStripStatusLabel();
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            this.groupBoxSelectiveMigration.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.panel1.SuspendLayout();
            this.tabPage2.SuspendLayout();
            this.panel3.SuspendLayout();
            this.panel2.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.statusStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.tabPage2);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(1024, 727);
            this.tabControl1.TabIndex = 0;
            // 
            // tabPage1
            // 
            this.tabPage1.Controls.Add(this.groupBoxSelectiveMigration);
            this.tabPage1.Controls.Add(this.groupBox2);
            this.tabPage1.Controls.Add(this.groupBox1);
            this.tabPage1.Controls.Add(this.panel1);
            this.tabPage1.Location = new System.Drawing.Point(4, 22);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage1.Size = new System.Drawing.Size(1016, 701);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "Configuration";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // groupBoxSelectiveMigration
            // 
            this.groupBoxSelectiveMigration.Controls.Add(this.lblSelectiveInstructions);
            this.groupBoxSelectiveMigration.Controls.Add(this.chkIncludeParents);
            this.groupBoxSelectiveMigration.Controls.Add(this.btnMigrateSelected);
            this.groupBoxSelectiveMigration.Controls.Add(this.btnValidateIds);
            this.groupBoxSelectiveMigration.Controls.Add(this.txtRallyIds);
            this.groupBoxSelectiveMigration.Controls.Add(this.lblRallyIds);
            this.groupBoxSelectiveMigration.Location = new System.Drawing.Point(20, 330);
            this.groupBoxSelectiveMigration.Name = "groupBoxSelectiveMigration";
            this.groupBoxSelectiveMigration.Size = new System.Drawing.Size(980, 249);
            this.groupBoxSelectiveMigration.TabIndex = 3;
            this.groupBoxSelectiveMigration.TabStop = false;
            this.groupBoxSelectiveMigration.Text = "Selective Migration";
            // 
            // lblSelectiveInstructions
            // 
            this.lblSelectiveInstructions.ForeColor = System.Drawing.Color.Blue;
            this.lblSelectiveInstructions.Location = new System.Drawing.Point(15, 28);
            this.lblSelectiveInstructions.Name = "lblSelectiveInstructions";
            this.lblSelectiveInstructions.Size = new System.Drawing.Size(950, 25);
            this.lblSelectiveInstructions.TabIndex = 0;
            this.lblSelectiveInstructions.Text = "Enter Rally IDs to migrate specific work items. Supports both FormattedID (US123," +
    " DE456) and ObjectID (12345678) formats. Use commas to separate multiple IDs.";
            // 
            // chkIncludeParents
            // 
            this.chkIncludeParents.AutoSize = true;
            this.chkIncludeParents.Checked = true;
            this.chkIncludeParents.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkIncludeParents.Location = new System.Drawing.Point(85, 186);
            this.chkIncludeParents.Name = "chkIncludeParents";
            this.chkIncludeParents.Size = new System.Drawing.Size(227, 17);
            this.chkIncludeParents.TabIndex = 3;
            this.chkIncludeParents.Text = "Include parent items (Features, Epics, etc.)";
            this.chkIncludeParents.UseVisualStyleBackColor = true;
            // 
            // btnMigrateSelected
            // 
            this.btnMigrateSelected.Enabled = false;
            this.btnMigrateSelected.Location = new System.Drawing.Point(840, 133);
            this.btnMigrateSelected.Name = "btnMigrateSelected";
            this.btnMigrateSelected.Size = new System.Drawing.Size(120, 30);
            this.btnMigrateSelected.TabIndex = 5;
            this.btnMigrateSelected.Text = "Migrate Selected";
            this.btnMigrateSelected.UseVisualStyleBackColor = true;
            this.btnMigrateSelected.Click += new System.EventHandler(this.btnMigrateSelected_Click);
            // 
            // btnValidateIds
            // 
            this.btnValidateIds.Location = new System.Drawing.Point(840, 97);
            this.btnValidateIds.Name = "btnValidateIds";
            this.btnValidateIds.Size = new System.Drawing.Size(120, 30);
            this.btnValidateIds.TabIndex = 4;
            this.btnValidateIds.Text = "Validate IDs";
            this.btnValidateIds.UseVisualStyleBackColor = true;
            this.btnValidateIds.Click += new System.EventHandler(this.btnValidateIds_Click);
            // 
            // txtRallyIds
            // 
            this.txtRallyIds.Location = new System.Drawing.Point(85, 78);
            this.txtRallyIds.Multiline = true;
            this.txtRallyIds.Name = "txtRallyIds";
            this.txtRallyIds.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtRallyIds.Size = new System.Drawing.Size(697, 102);
            this.txtRallyIds.TabIndex = 2;
            // 
            // lblRallyIds
            // 
            this.lblRallyIds.AutoSize = true;
            this.lblRallyIds.Location = new System.Drawing.Point(15, 78);
            this.lblRallyIds.Name = "lblRallyIds";
            this.lblRallyIds.Size = new System.Drawing.Size(52, 13);
            this.lblRallyIds.TabIndex = 1;
            this.lblRallyIds.Text = "Rally IDs:";
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.txtAdoProject);
            this.groupBox2.Controls.Add(this.label8);
            this.groupBox2.Controls.Add(this.txtAdoOrganization);
            this.groupBox2.Controls.Add(this.label7);
            this.groupBox2.Controls.Add(this.txtAdoServerUrl);
            this.groupBox2.Controls.Add(this.label6);
            this.groupBox2.Controls.Add(this.txtAdoApiKey);
            this.groupBox2.Controls.Add(this.label5);
            this.groupBox2.Location = new System.Drawing.Point(520, 110);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(480, 200);
            this.groupBox2.TabIndex = 2;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Azure DevOps Configuration";
            // 
            // txtAdoProject
            // 
            this.txtAdoProject.Location = new System.Drawing.Point(120, 160);
            this.txtAdoProject.Name = "txtAdoProject";
            this.txtAdoProject.Size = new System.Drawing.Size(340, 20);
            this.txtAdoProject.TabIndex = 7;
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(15, 163);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(43, 13);
            this.label8.TabIndex = 6;
            this.label8.Text = "Project:";
            // 
            // txtAdoOrganization
            // 
            this.txtAdoOrganization.Location = new System.Drawing.Point(120, 120);
            this.txtAdoOrganization.Name = "txtAdoOrganization";
            this.txtAdoOrganization.Size = new System.Drawing.Size(340, 20);
            this.txtAdoOrganization.TabIndex = 5;
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(15, 123);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(69, 13);
            this.label7.TabIndex = 4;
            this.label7.Text = "Organization:";
            // 
            // txtAdoServerUrl
            // 
            this.txtAdoServerUrl.Location = new System.Drawing.Point(120, 80);
            this.txtAdoServerUrl.Name = "txtAdoServerUrl";
            this.txtAdoServerUrl.Size = new System.Drawing.Size(340, 20);
            this.txtAdoServerUrl.TabIndex = 3;
            this.txtAdoServerUrl.Text = "https://dev.azure.com";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(15, 83);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(66, 13);
            this.label6.TabIndex = 2;
            this.label6.Text = "Server URL:";
            // 
            // txtAdoApiKey
            // 
            this.txtAdoApiKey.Location = new System.Drawing.Point(120, 40);
            this.txtAdoApiKey.Name = "txtAdoApiKey";
            this.txtAdoApiKey.PasswordChar = '*';
            this.txtAdoApiKey.Size = new System.Drawing.Size(340, 20);
            this.txtAdoApiKey.TabIndex = 1;
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(15, 43);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(48, 13);
            this.label5.TabIndex = 0;
            this.label5.Text = "API Key:";
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.txtRallyProject);
            this.groupBox1.Controls.Add(this.label4);
            this.groupBox1.Controls.Add(this.txtRallyWorkspace);
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.txtRallyServerUrl);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Controls.Add(this.txtRallyApiKey);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Location = new System.Drawing.Point(20, 110);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(480, 200);
            this.groupBox1.TabIndex = 1;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Rally Configuration";
            // 
            // txtRallyProject
            // 
            this.txtRallyProject.Location = new System.Drawing.Point(120, 160);
            this.txtRallyProject.Name = "txtRallyProject";
            this.txtRallyProject.Size = new System.Drawing.Size(340, 20);
            this.txtRallyProject.TabIndex = 7;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(15, 163);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(43, 13);
            this.label4.TabIndex = 6;
            this.label4.Text = "Project:";
            // 
            // txtRallyWorkspace
            // 
            this.txtRallyWorkspace.Location = new System.Drawing.Point(120, 120);
            this.txtRallyWorkspace.Name = "txtRallyWorkspace";
            this.txtRallyWorkspace.Size = new System.Drawing.Size(340, 20);
            this.txtRallyWorkspace.TabIndex = 5;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(15, 123);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(65, 13);
            this.label3.TabIndex = 4;
            this.label3.Text = "Workspace:";
            // 
            // txtRallyServerUrl
            // 
            this.txtRallyServerUrl.Location = new System.Drawing.Point(120, 80);
            this.txtRallyServerUrl.Name = "txtRallyServerUrl";
            this.txtRallyServerUrl.Size = new System.Drawing.Size(340, 20);
            this.txtRallyServerUrl.TabIndex = 3;
            this.txtRallyServerUrl.Text = "https://rally1.rallydev.com";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(15, 83);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(66, 13);
            this.label2.TabIndex = 2;
            this.label2.Text = "Server URL:";
            // 
            // txtRallyApiKey
            // 
            this.txtRallyApiKey.Location = new System.Drawing.Point(120, 40);
            this.txtRallyApiKey.Name = "txtRallyApiKey";
            this.txtRallyApiKey.PasswordChar = '*';
            this.txtRallyApiKey.Size = new System.Drawing.Size(340, 20);
            this.txtRallyApiKey.TabIndex = 1;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(15, 43);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(48, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "API Key:";
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.btnGenerateMapping);
            this.panel1.Controls.Add(this.btnSaveSettings);
            this.panel1.Controls.Add(this.btnLoadSettings);
            this.panel1.Controls.Add(this.cmbSavedSettings);
            this.panel1.Controls.Add(this.btnTestConnections);
            this.panel1.Controls.Add(this.btnAdoFieldDiscovery);
            this.panel1.Controls.Add(this.btnRallyFieldDiscovery);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
            this.panel1.Location = new System.Drawing.Point(3, 3);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(1010, 90);
            this.panel1.TabIndex = 0;
            // 
            // btnGenerateMapping
            // 
            this.btnGenerateMapping.Location = new System.Drawing.Point(827, 30);
            this.btnGenerateMapping.Name = "btnGenerateMapping";
            this.btnGenerateMapping.Size = new System.Drawing.Size(150, 30);
            this.btnGenerateMapping.TabIndex = 0;
            this.btnGenerateMapping.Text = "Generate Mapping";
            this.btnGenerateMapping.Click += new System.EventHandler(this.btnGenerateMapping_Click);
            // 
            // btnSaveSettings
            // 
            this.btnSaveSettings.Location = new System.Drawing.Point(372, 49);
            this.btnSaveSettings.Name = "btnSaveSettings";
            this.btnSaveSettings.Size = new System.Drawing.Size(150, 30);
            this.btnSaveSettings.TabIndex = 4;
            this.btnSaveSettings.Text = "Save Settings";
            this.btnSaveSettings.UseVisualStyleBackColor = true;
            this.btnSaveSettings.Click += new System.EventHandler(this.btnSaveSettings_Click);
            // 
            // btnLoadSettings
            // 
            this.btnLoadSettings.Location = new System.Drawing.Point(372, 10);
            this.btnLoadSettings.Name = "btnLoadSettings";
            this.btnLoadSettings.Size = new System.Drawing.Size(150, 30);
            this.btnLoadSettings.TabIndex = 3;
            this.btnLoadSettings.Text = "Load Settings";
            this.btnLoadSettings.UseVisualStyleBackColor = true;
            this.btnLoadSettings.Click += new System.EventHandler(this.btnLoadSettings_Click);
            // 
            // cmbSavedSettings
            // 
            this.cmbSavedSettings.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbSavedSettings.FormattingEnabled = true;
            this.cmbSavedSettings.Location = new System.Drawing.Point(17, 16);
            this.cmbSavedSettings.Name = "cmbSavedSettings";
            this.cmbSavedSettings.Size = new System.Drawing.Size(331, 21);
            this.cmbSavedSettings.TabIndex = 2;
            // 
            // btnTestConnections
            // 
            this.btnTestConnections.Location = new System.Drawing.Point(102, 46);
            this.btnTestConnections.Name = "btnTestConnections";
            this.btnTestConnections.Size = new System.Drawing.Size(150, 30);
            this.btnTestConnections.TabIndex = 0;
            this.btnTestConnections.Text = "Test Connections";
            this.btnTestConnections.UseVisualStyleBackColor = true;
            this.btnTestConnections.Click += new System.EventHandler(this.btnTestConnections_Click);
            // 
            // btnAdoFieldDiscovery
            // 
            this.btnAdoFieldDiscovery.Location = new System.Drawing.Point(574, 49);
            this.btnAdoFieldDiscovery.Name = "btnAdoFieldDiscovery";
            this.btnAdoFieldDiscovery.Size = new System.Drawing.Size(180, 30);
            this.btnAdoFieldDiscovery.TabIndex = 6;
            this.btnAdoFieldDiscovery.Text = "🔍 ADO Field Discovery";
            this.btnAdoFieldDiscovery.UseVisualStyleBackColor = true;
            this.btnAdoFieldDiscovery.Click += new System.EventHandler(this.btnAdoFieldDiscovery_Click);
            // 
            // btnRallyFieldDiscovery
            // 
            this.btnRallyFieldDiscovery.Location = new System.Drawing.Point(574, 10);
            this.btnRallyFieldDiscovery.Name = "btnRallyFieldDiscovery";
            this.btnRallyFieldDiscovery.Size = new System.Drawing.Size(180, 30);
            this.btnRallyFieldDiscovery.TabIndex = 10;
            this.btnRallyFieldDiscovery.Text = "🔍 Rally Field Discovery";
            this.btnRallyFieldDiscovery.UseVisualStyleBackColor = true;
            this.btnRallyFieldDiscovery.Click += new System.EventHandler(this.btnRallyFieldDiscovery_Click);
            // 
            // tabPage2
            // 
            this.tabPage2.Controls.Add(this.richTextBox1);
            this.tabPage2.Controls.Add(this.panel3);
            this.tabPage2.Controls.Add(this.panel2);
            this.tabPage2.Controls.Add(this.groupBox3);
            this.tabPage2.Location = new System.Drawing.Point(4, 22);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage2.Size = new System.Drawing.Size(1016, 701);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "Migration";
            this.tabPage2.UseVisualStyleBackColor = true;
            // 
            // richTextBox1
            // 
            this.richTextBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.richTextBox1.Location = new System.Drawing.Point(3, 223);
            this.richTextBox1.Name = "richTextBox1";
            this.richTextBox1.ReadOnly = true;
            this.richTextBox1.Size = new System.Drawing.Size(1010, 418);
            this.richTextBox1.TabIndex = 1;
            this.richTextBox1.Text = "";
            // 
            // panel3
            // 
            this.panel3.Controls.Add(this.btnViewLogs);
            this.panel3.Controls.Add(this.btnOpenReportsFolder);
            this.panel3.Controls.Add(this.btnCancel);
            this.panel3.Controls.Add(this.btnResume);
            this.panel3.Controls.Add(this.btnPause);
            this.panel3.Controls.Add(this.btnStartMigration);
            this.panel3.Dock = System.Windows.Forms.DockStyle.Top;
            this.panel3.Location = new System.Drawing.Point(3, 163);
            this.panel3.Name = "panel3";
            this.panel3.Size = new System.Drawing.Size(1010, 60);
            this.panel3.TabIndex = 3;
            // 
            // btnViewLogs
            // 
            this.btnViewLogs.Location = new System.Drawing.Point(680, 15);
            this.btnViewLogs.Name = "btnViewLogs";
            this.btnViewLogs.Size = new System.Drawing.Size(100, 30);
            this.btnViewLogs.TabIndex = 5;
            this.btnViewLogs.Text = "View Logs";
            this.btnViewLogs.UseVisualStyleBackColor = true;
            this.btnViewLogs.Click += new System.EventHandler(this.btnViewLogs_Click);
            // 
            // btnOpenReportsFolder
            // 
            this.btnOpenReportsFolder.Location = new System.Drawing.Point(560, 15);
            this.btnOpenReportsFolder.Name = "btnOpenReportsFolder";
            this.btnOpenReportsFolder.Size = new System.Drawing.Size(110, 30);
            this.btnOpenReportsFolder.TabIndex = 4;
            this.btnOpenReportsFolder.Text = "Open Reports";
            this.btnOpenReportsFolder.UseVisualStyleBackColor = true;
            this.btnOpenReportsFolder.Click += new System.EventHandler(this.btnOpenReportsFolder_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Location = new System.Drawing.Point(440, 15);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(100, 30);
            this.btnCancel.TabIndex = 3;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // btnResume
            // 
            this.btnResume.Location = new System.Drawing.Point(320, 15);
            this.btnResume.Name = "btnResume";
            this.btnResume.Size = new System.Drawing.Size(100, 30);
            this.btnResume.TabIndex = 2;
            this.btnResume.Text = "Resume";
            this.btnResume.UseVisualStyleBackColor = true;
            this.btnResume.Click += new System.EventHandler(this.btnResume_Click);
            // 
            // btnPause
            // 
            this.btnPause.Location = new System.Drawing.Point(200, 15);
            this.btnPause.Name = "btnPause";
            this.btnPause.Size = new System.Drawing.Size(100, 30);
            this.btnPause.TabIndex = 1;
            this.btnPause.Text = "Pause";
            this.btnPause.UseVisualStyleBackColor = true;
            this.btnPause.Click += new System.EventHandler(this.btnPause_Click);
            // 
            // btnStartMigration
            // 
            this.btnStartMigration.Location = new System.Drawing.Point(17, 15);
            this.btnStartMigration.Name = "btnStartMigration";
            this.btnStartMigration.Size = new System.Drawing.Size(150, 30);
            this.btnStartMigration.TabIndex = 0;
            this.btnStartMigration.Text = "Start Migration";
            this.btnStartMigration.UseVisualStyleBackColor = true;
            this.btnStartMigration.Click += new System.EventHandler(this.btnStartMigration_Click);
            // 
            // panel2
            // 
            this.panel2.Controls.Add(this.lblElapsedTime);
            this.panel2.Controls.Add(this.lblProgressText);
            this.panel2.Controls.Add(this.progressBar1);
            this.panel2.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel2.Location = new System.Drawing.Point(3, 641);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(1010, 57);
            this.panel2.TabIndex = 2;
            // 
            // lblElapsedTime
            // 
            this.lblElapsedTime.AutoSize = true;
            this.lblElapsedTime.Location = new System.Drawing.Point(800, 35);
            this.lblElapsedTime.Name = "lblElapsedTime";
            this.lblElapsedTime.Size = new System.Drawing.Size(78, 13);
            this.lblElapsedTime.TabIndex = 2;
            this.lblElapsedTime.Text = "Elapsed: 00:00";
            // 
            // lblProgressText
            // 
            this.lblProgressText.AutoSize = true;
            this.lblProgressText.Location = new System.Drawing.Point(17, 10);
            this.lblProgressText.Name = "lblProgressText";
            this.lblProgressText.Size = new System.Drawing.Size(38, 13);
            this.lblProgressText.TabIndex = 1;
            this.lblProgressText.Text = "Ready";
            // 
            // progressBar1
            // 
            this.progressBar1.Location = new System.Drawing.Point(17, 30);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(760, 20);
            this.progressBar1.TabIndex = 0;
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.lblSkipped);
            this.groupBox3.Controls.Add(this.lblFailed);
            this.groupBox3.Controls.Add(this.lblSuccessful);
            this.groupBox3.Controls.Add(this.lblProcessed);
            this.groupBox3.Controls.Add(this.lblTotal);
            this.groupBox3.Controls.Add(this.label15);
            this.groupBox3.Controls.Add(this.label14);
            this.groupBox3.Controls.Add(this.label13);
            this.groupBox3.Controls.Add(this.label12);
            this.groupBox3.Controls.Add(this.label11);
            this.groupBox3.Dock = System.Windows.Forms.DockStyle.Top;
            this.groupBox3.Location = new System.Drawing.Point(3, 3);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(1010, 160);
            this.groupBox3.TabIndex = 0;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "Migration Statistics";
            // 
            // lblSkipped
            // 
            this.lblSkipped.AutoSize = true;
            this.lblSkipped.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblSkipped.Location = new System.Drawing.Point(670, 80);
            this.lblSkipped.Name = "lblSkipped";
            this.lblSkipped.Size = new System.Drawing.Size(19, 20);
            this.lblSkipped.TabIndex = 9;
            this.lblSkipped.Text = "0";
            // 
            // lblFailed
            // 
            this.lblFailed.AutoSize = true;
            this.lblFailed.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblFailed.Location = new System.Drawing.Point(520, 80);
            this.lblFailed.Name = "lblFailed";
            this.lblFailed.Size = new System.Drawing.Size(19, 20);
            this.lblFailed.TabIndex = 8;
            this.lblFailed.Text = "0";
            // 
            // lblSuccessful
            // 
            this.lblSuccessful.AutoSize = true;
            this.lblSuccessful.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblSuccessful.Location = new System.Drawing.Point(370, 80);
            this.lblSuccessful.Name = "lblSuccessful";
            this.lblSuccessful.Size = new System.Drawing.Size(19, 20);
            this.lblSuccessful.TabIndex = 7;
            this.lblSuccessful.Text = "0";
            // 
            // lblProcessed
            // 
            this.lblProcessed.AutoSize = true;
            this.lblProcessed.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblProcessed.Location = new System.Drawing.Point(220, 80);
            this.lblProcessed.Name = "lblProcessed";
            this.lblProcessed.Size = new System.Drawing.Size(19, 20);
            this.lblProcessed.TabIndex = 6;
            this.lblProcessed.Text = "0";
            // 
            // lblTotal
            // 
            this.lblTotal.AutoSize = true;
            this.lblTotal.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblTotal.Location = new System.Drawing.Point(70, 80);
            this.lblTotal.Name = "lblTotal";
            this.lblTotal.Size = new System.Drawing.Size(19, 20);
            this.lblTotal.TabIndex = 5;
            this.lblTotal.Text = "0";
            // 
            // label15
            // 
            this.label15.AutoSize = true;
            this.label15.Location = new System.Drawing.Point(670, 40);
            this.label15.Name = "label15";
            this.label15.Size = new System.Drawing.Size(49, 13);
            this.label15.TabIndex = 4;
            this.label15.Text = "Skipped:";
            // 
            // label14
            // 
            this.label14.AutoSize = true;
            this.label14.Location = new System.Drawing.Point(520, 40);
            this.label14.Name = "label14";
            this.label14.Size = new System.Drawing.Size(38, 13);
            this.label14.TabIndex = 3;
            this.label14.Text = "Failed:";
            // 
            // label13
            // 
            this.label13.AutoSize = true;
            this.label13.Location = new System.Drawing.Point(370, 40);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(62, 13);
            this.label13.TabIndex = 2;
            this.label13.Text = "Successful:";
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Location = new System.Drawing.Point(220, 40);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(60, 13);
            this.label12.TabIndex = 1;
            this.label12.Text = "Processed:";
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Location = new System.Drawing.Point(70, 40);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(34, 13);
            this.label11.TabIndex = 0;
            this.label11.Text = "Total:";
            // 
            // statusStrip1
            // 
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripStatusLabel1});
            this.statusStrip1.Location = new System.Drawing.Point(0, 727);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.Size = new System.Drawing.Size(1024, 22);
            this.statusStrip1.TabIndex = 1;
            this.statusStrip1.Text = "statusStrip1";
            // 
            // toolStripStatusLabel1
            // 
            this.toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            this.toolStripStatusLabel1.Size = new System.Drawing.Size(39, 17);
            this.toolStripStatusLabel1.Text = "Ready";
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1024, 749);
            this.Controls.Add(this.tabControl1);
            this.Controls.Add(this.statusStrip1);
            this.MinimumSize = new System.Drawing.Size(1040, 718);
            this.Name = "MainForm";
            this.Text = "Rally to Azure DevOps Migration Tool";
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.groupBoxSelectiveMigration.ResumeLayout(false);
            this.groupBoxSelectiveMigration.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.panel1.ResumeLayout(false);
            this.tabPage2.ResumeLayout(false);
            this.panel3.ResumeLayout(false);
            this.panel2.ResumeLayout(false);
            this.panel2.PerformLayout();
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.TextBox txtRallyApiKey;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button btnTestConnections;
        private System.Windows.Forms.TextBox txtRallyProject;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox txtRallyWorkspace;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox txtRallyServerUrl;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.TextBox txtAdoProject;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.TextBox txtAdoOrganization;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.TextBox txtAdoServerUrl;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.TextBox txtAdoApiKey;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.ComboBox cmbSavedSettings;
        private System.Windows.Forms.Button btnSaveSettings;
        private System.Windows.Forms.Button btnLoadSettings;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.RichTextBox richTextBox1;
        private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.ProgressBar progressBar1;
        private System.Windows.Forms.Label lblProgressText;
        private System.Windows.Forms.Panel panel3;
        private System.Windows.Forms.Button btnStartMigration;
        private System.Windows.Forms.Label lblSkipped;
        private System.Windows.Forms.Label lblFailed;
        private System.Windows.Forms.Label lblSuccessful;
        private System.Windows.Forms.Label lblProcessed;
        private System.Windows.Forms.Label lblTotal;
        private System.Windows.Forms.Label label15;
        private System.Windows.Forms.Label label14;
        private System.Windows.Forms.Label label13;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Button btnResume;
        private System.Windows.Forms.Button btnPause;
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel1;
        private System.Windows.Forms.Button btnViewLogs;
        private System.Windows.Forms.Button btnOpenReportsFolder;
        private System.Windows.Forms.Label lblElapsedTime;
        private System.Windows.Forms.GroupBox groupBoxSelectiveMigration;
        private System.Windows.Forms.TextBox txtRallyIds;
        private System.Windows.Forms.Label lblRallyIds;
        private System.Windows.Forms.Button btnValidateIds;
        private System.Windows.Forms.Button btnMigrateSelected;
        private System.Windows.Forms.CheckBox chkIncludeParents;
        private System.Windows.Forms.Label lblSelectiveInstructions;
        private System.Windows.Forms.Button btnAdoFieldDiscovery;
        private System.Windows.Forms.Button btnGenerateMapping;
        private System.Windows.Forms.Button btnRallyFieldDiscovery;
    }
}