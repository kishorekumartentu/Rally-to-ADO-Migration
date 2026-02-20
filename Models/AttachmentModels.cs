using System;

namespace Rally_to_ADO_Migration.Models
{
    /// <summary>
    /// Represents a reference to an attachment found within a rich text field in Rally.
    /// Used to track inline attachments in descriptions and notes.
    /// </summary>
    public class RallyAttachmentReference
    {
        public string OriginalPath { get; set; }
        public string PlaceholderToken { get; set; }
        public string AltText { get; set; }
        public string MimeType { get; set; }
        public string FileName { get; set; }
        public long? Size { get; set; }
        public string AdoUrl { get; set; }
    }

    /// <summary>
    /// Represents a Rally attachment object retrieved from the API.
    /// Used during attachment migration from Rally to Azure DevOps.
    /// </summary>
    public class RallyAttachmentModel // Renamed to avoid duplicate definition
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string ContentType { get; set; }
        public long Size { get; set; }
        public string Content { get; set; } // URL to the attachment content
    }
}