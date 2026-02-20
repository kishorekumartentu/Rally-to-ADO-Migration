using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Service to append test steps as formatted HTML table to Description field
    /// This is a workaround when the native ADO test steps XML doesn't display in UI
    /// </summary>
    public class TestStepsToDescriptionService
    {
        private readonly LoggingService _loggingService;

        public TestStepsToDescriptionService(LoggingService loggingService)
        {
            _loggingService = loggingService;
        }

        /// <summary>
        /// Build HTML table of test steps to append to description
        /// </summary>
        public string BuildTestStepsHtmlTable(List<RallyTestCaseStep> steps)
        {
            if (steps == null || !steps.Any())
                return string.Empty;

            var html = new StringBuilder();
            
            // Add a visual separator
            html.AppendLine("<hr/>");
            html.AppendLine("<h3 style='color: #0078d4; margin-top: 20px;'>Test Steps (Migrated from Rally)</h3>");
            
            // Create a styled table
            html.AppendLine("<table style='width: 100%; border-collapse: collapse; border: 1px solid #ddd; margin-top: 10px;'>");
            
            // Table header
            html.AppendLine("  <thead>");
            html.AppendLine("    <tr style='background-color: #0078d4; color: white;'>");
            html.AppendLine("      <th style='border: 1px solid #ddd; padding: 12px; text-align: center; width: 60px;'>Step #</th>");
            html.AppendLine("      <th style='border: 1px solid #ddd; padding: 12px; text-align: left;'>Action / Test Step</th>");
            html.AppendLine("      <th style='border: 1px solid #ddd; padding: 12px; text-align: left;'>Expected Result</th>");
            html.AppendLine("    </tr>");
            html.AppendLine("  </thead>");
            html.AppendLine("  <tbody>");
            
            // Add each step as a table row
            var orderedSteps = steps.OrderBy(s => s.StepIndex).ToList();
            for (int i = 0; i < orderedSteps.Count; i++)
            {
                var step = orderedSteps[i];
                var rowColor = (i % 2 == 0) ? "#f9f9f9" : "#ffffff"; // Alternating row colors
                
                // Decode Unicode escapes and clean HTML
                var input = CleanHtmlContent(System.Text.RegularExpressions.Regex.Unescape(step.Input ?? string.Empty));
                var expected = CleanHtmlContent(System.Text.RegularExpressions.Regex.Unescape(step.ExpectedResult ?? string.Empty));
                
                html.AppendLine($"    <tr style='background-color: {rowColor};'>");
                html.AppendLine($"      <td style='border: 1px solid #ddd; padding: 10px; text-align: center; font-weight: bold;'>{i + 1}</td>");
                html.AppendLine($"      <td style='border: 1px solid #ddd; padding: 10px; vertical-align: top;'>{input}</td>");
                html.AppendLine($"      <td style='border: 1px solid #ddd; padding: 10px; vertical-align: top;'>{expected}</td>");
                html.AppendLine("    </tr>");
            }
            
            html.AppendLine("  </tbody>");
            html.AppendLine("</table>");
            
            // Add a note about the source
            html.AppendLine("<p style='margin-top: 10px; font-size: 0.9em; color: #666;'>");
            html.AppendLine($"  <em>{orderedSteps.Count} test steps migrated from Rally on {DateTime.Now:yyyy-MM-dd HH:mm}</em>");
            html.AppendLine("</p>");
            
            return html.ToString();
        }

        /// <summary>
        /// Clean HTML content - strip outer wrappers and fix malformed HTML
        /// </summary>
        private string CleanHtmlContent(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return "<em>No content</em>";
            
            html = html.Trim();
            
            // Strip outer <p> or <div> tags if present
            if (html.StartsWith("<p>", StringComparison.OrdinalIgnoreCase))
                html = html.Substring(3);
            else if (html.StartsWith("<div>", StringComparison.OrdinalIgnoreCase))
                html = html.Substring(5);
            
            if (html.EndsWith("</p>", StringComparison.OrdinalIgnoreCase))
                html = html.Substring(0, html.Length - 4);
            else if (html.EndsWith("</div>", StringComparison.OrdinalIgnoreCase))
                html = html.Substring(0, html.Length - 6);
            
            // Fix common HTML issues
            html = html.Replace("</p><p>", "<br/><br/>"); // Convert paragraph breaks to line breaks
            html = html.Replace("<p>", ""); // Remove remaining opening p tags
            html = html.Replace("</p>", "<br/>"); // Convert closing p tags to line breaks
            
            // Remove empty HTML
            html = html.Replace("<br/><br/><br/>", "<br/><br/>"); // Remove triple breaks
            html = html.Trim();
            
            if (string.IsNullOrWhiteSpace(html))
                return "<em>No content</em>";
            
            return html;
        }

        /// <summary>
        /// Append test steps HTML to existing description
        /// </summary>
        public string AppendTestStepsToDescription(string existingDescription, List<RallyTestCaseStep> steps)
        {
            if (steps == null || !steps.Any())
                return existingDescription ?? string.Empty;
            
            var stepsHtml = BuildTestStepsHtmlTable(steps);
            
            // If description is empty or null, just use the steps table
            if (string.IsNullOrWhiteSpace(existingDescription))
                return stepsHtml;
            
            // Append steps to existing description
            return existingDescription + "\n\n" + stepsHtml;
        }
    }
}
