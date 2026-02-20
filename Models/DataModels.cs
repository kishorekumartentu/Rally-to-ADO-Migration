using System;
using System.Collections.Generic;

namespace Rally_to_ADO_Migration.Models
{
    public class ConnectionSettings
    {
        public string RallyApiKey { get; set; }
        public string RallyServerUrl { get; set; }
        public string RallyWorkspace { get; set; }
        public string RallyProject { get; set; }
        
        public string AdoApiKey { get; set; }
        public string AdoOrganization { get; set; }
        public string AdoProject { get; set; }
        public string AdoServerUrl { get; set; }
        // Migration tuning
        public int BatchSize { get; set; }
        public int MaxConcurrency { get; set; }
        public bool? PreferOptumFirst { get; set; }
        // Added control flags
        public bool BypassRules { get; set; } // allow historical field patching
        public bool DryRun { get; set; } // simulate without creating
        public bool EnableDifferencePatch { get; set; } = true; // patch existing diffs
    }

    public class SavedConnectionSettings
    {
        public string Name { get; set; }
        public string RallyServerUrl { get; set; }
        public string RallyWorkspace { get; set; }
        public string RallyProject { get; set; }
        public string AdoOrganization { get; set; }
        public string AdoProject { get; set; }
        public string AdoServerUrl { get; set; }
        public DateTime LastUsed { get; set; }
    }

    public class RallyWorkItem
    {
        public string ObjectID { get; set; }
        public string FormattedID { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Notes { get; set; }
        public string State { get; set; }
        public string Type { get; set; }
        public string Owner { get; set; }
        public string Project { get; set; }
        public DateTime? CreationDate { get; set; }
        public DateTime? LastUpdateDate { get; set; }
        public string Priority { get; set; }
        public string Severity { get; set; }
        public double? PlanEstimate { get; set; }
        public double? TaskEstimateTotal { get; set; }
        public double? TaskRemainingTotal { get; set; }
        
        // Task-specific time tracking fields (in hours)
        public double? Estimate { get; set; }      // Rally Task: Original estimate in hours
        public double? ToDo { get; set; }          // Rally Task: Remaining work in hours
        public double? Actuals { get; set; }       // Rally Task: Completed work in hours
        public string Parent { get; set; }
        public bool? Blocked { get; set; }
        public bool? Ready { get; set; }
        public string Release { get; set; }
        public string PreConditions { get; set; }  // Test Case Pre-Conditions
        public object AcceptanceCriteria { get; set; }
        public List<string> Children { get; set; }
        public List<string> TestCases { get; set; } // Linked Test Case ObjectIDs
        public List<RallyAttachment> Attachments { get; set; }
        public List<RallyComment> Comments { get; set; }
        public Dictionary<string, object> CustomFields { get; set; }
        // Added for Test Case migration (list of steps preserving input/expected raw HTML)
        public List<RallyTestCaseStep> Steps { get; set; }
        public List<string> RawFieldNames { get; set; } // Captured raw JSON field names for unmapped reporting

        public RallyWorkItem()
        {
            Children = new List<string>();
            TestCases = new List<string>();
            Attachments = new List<RallyAttachment>();
            Comments = new List<RallyComment>();
            CustomFields = new Dictionary<string, object>();
            Steps = new List<RallyTestCaseStep>();
            RawFieldNames = new List<string>();
        }
    }

    public class RallyTestCaseStep
    {
        public int StepIndex { get; set; }
        public string Input { get; set; }
        public string ExpectedResult { get; set; }
    }

    public class RallyAttachment
    {
        public string ObjectID { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public long Size { get; set; }
        public string ContentType { get; set; }
        public byte[] Content { get; set; }
        public DateTime CreationDate { get; set; }
        public string User { get; set; }
    }

    public class RallyComment
    {
        public string ObjectID { get; set; }
        public string Text { get; set; }
        public DateTime CreationDate { get; set; }
        public string User { get; set; }
    }

    public class AdoWorkItem
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string State { get; set; }
        public string AssignedTo { get; set; }
        public string Priority { get; set; }
        public string Severity { get; set; }
        public double? StoryPoints { get; set; }
        public double? OriginalEstimate { get; set; }
        public double? RemainingWork { get; set; }
        public double? CompletedWork { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ChangedDate { get; set; }
        public Dictionary<string, object> Fields { get; set; }
        
        public AdoWorkItem()
        {
            Fields = new Dictionary<string, object>();
        }
    }

    public class MigrationResult
    {
        public string RallyId { get; set; }
        public string RallyFormattedId { get; set; }
        public int? AdoId { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime ProcessedAt { get; set; }
        public bool IsSkipped { get; set; }
        public string SkipReason { get; set; }
        // New: difference patch info
        public bool WasPatched { get; set; }
        public List<string> PatchedFields { get; set; }
    }

    public class MigrationProgress
    {
        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public int SuccessfulItems { get; set; }
        public int FailedItems { get; set; }
        public int SkippedItems { get; set; }
        public bool IsPaused { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public List<MigrationResult> Results { get; set; }
        // New: unmapped field collection per run
        public Dictionary<string, List<string>> UnmappedFieldsByType { get; set; }
        // New: checkpoint index
        public int LastCheckpointIndex { get; set; }
        public Dictionary<string,int> RallyToAdoIdMap { get; set; } // Rally ObjectID -> ADO ID mapping
        
        public MigrationProgress()
        {
            Results = new List<MigrationResult>();
            StartTime = DateTime.Now;
            UnmappedFieldsByType = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            RallyToAdoIdMap = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        }
        
        public TimeSpan ElapsedTime => (EndTime ?? DateTime.Now) - StartTime;
        public double ProgressPercentage => TotalItems > 0 ? (double)ProcessedItems / TotalItems * 100 : 0;
    }

    public class RallyIdValidationResult
    {
        public List<string> ValidIds { get; set; }
        public List<string> InvalidIds { get; set; }
        public List<string> AlreadyMigrated { get; set; }
        public List<string> ParentItemsFound { get; set; }
        public Dictionary<string, RallyWorkItem> ValidatedItems { get; set; }

        public RallyIdValidationResult()
        {
            ValidIds = new List<string>();
            InvalidIds = new List<string>();
            AlreadyMigrated = new List<string>();
            ParentItemsFound = new List<string>();
            ValidatedItems = new Dictionary<string, RallyWorkItem>();
        }
    }

    public class SelectiveMigrationRequest
    {
        public List<string> RallyIds { get; set; }
        public bool IncludeParents { get; set; }
        public ConnectionSettings Settings { get; set; }

        public SelectiveMigrationRequest()
        {
            RallyIds = new List<string>();
        }
    }

    public enum WorkItemType
    {
        UserStory,
        Defect,
        Task,
        TestCase,
        Feature,
        Epic,
        Initiative,
        Theme
    }

    public class FieldMapping
    {
        public string RallyField { get; set; }
        public string AdoField { get; set; }
        public string DataType { get; set; }
        public bool IsRequired { get; set; }
        public string DefaultValue { get; set; }
        public Func<object, object> ValueTransformer { get; set; }
        public string RallyFieldName { get; set; }
        public string AdoFieldReference { get; set; }
        public string RallyFieldType { get; set; }
        public bool RallyRequired { get; set; }
        public string RallyDescription { get; set; }
        public string AdoFieldDisplayName { get; set; }
        public string AdoFieldType { get; set; }
        public string MappingConfidence { get; set; }
        public string MappingNotes { get; set; }
        public string CustomTransformation { get; set; }
        public bool Skip { get; set; }
    }

    public class RallyFieldDefinition
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
        public bool ReadOnly { get; set; }
        public List<string> AllowedValues { get; set; }

        public RallyFieldDefinition()
        {
            AllowedValues = new List<string>();
        }
    }
}