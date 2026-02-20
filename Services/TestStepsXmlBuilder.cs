using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    /// <summary>
    /// Builds ADO Test Case Steps XML from Rally TestCaseStep objects
    /// CRITICAL: Match the EXACT format that ADO uses (discovered from successful ADO?Rally JS migration)
    /// ADO stores text content that xml2js reads as parameterizedString.DIV._ 
    /// So we need to store it in a way that produces that same structure
    /// </summary>
    public class TestStepsXmlBuilder
    {
        private static LoggingService _logger;

        public static void SetLogger(LoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Convert Rally test steps to ADO XML format for Microsoft.VSTS.TCM.Steps field
        /// Based on analysis of successful ADO?Rally JavaScript migration
        /// CRITICAL: The JavaScript uses xml2js which expects text content in specific format
        /// </summary>
        public static string BuildTestStepsXml(List<RallyTestCaseStep> rallySteps)
        {
            if (rallySteps == null || !rallySteps.Any())
                return string.Empty;

            var orderedSteps = rallySteps.OrderBy(s => s.StepIndex).ToList();
            
            _logger?.LogInfo($"[XML_BUILD] Starting XML generation for {orderedSteps.Count} steps");
            
            var xml = new StringBuilder();
            
            // Root element - MUST match ADO format exactly
            xml.Append($"<steps id=\"0\" last=\"{orderedSteps.Count}\">");
            
            for (int i = 0; i < orderedSteps.Count; i++)
            {
                var step = orderedSteps[i];
                var stepId = i + 1;
                
                _logger?.LogInfo($"[XML_BUILD] === Step {stepId} ===");
                _logger?.LogInfo($"   Rally Input (RAW): {SafeSubstring(step.Input, 100)}");
                _logger?.LogInfo($"   Rally Expected (RAW): {SafeSubstring(step.ExpectedResult, 100)}");
                
                // Decode Unicode escapes from Rally
                var rawInput = System.Text.RegularExpressions.Regex.Unescape(step.Input ?? string.Empty);
                var rawExpected = System.Text.RegularExpressions.Regex.Unescape(step.ExpectedResult ?? string.Empty);
                
                _logger?.LogInfo($"   After Unescape Input: {SafeSubstring(rawInput, 100)}");
                _logger?.LogInfo($"   After Unescape Expected: {SafeSubstring(rawExpected, 100)}");
                
                // Clean and prepare content - strip Rally HTML but keep line breaks
                var cleanInput = CleanRallyHtmlForAdo(rawInput);
                var cleanExpected = CleanRallyHtmlForAdo(rawExpected);
                
                _logger?.LogInfo($"   After Clean Input: '{SafeSubstring(cleanInput, 100)}'");
                _logger?.LogInfo($"   After Clean Expected: '{SafeSubstring(cleanExpected, 100)}'");
                _logger?.LogInfo($"   Input Length: {cleanInput.Length} chars");
                _logger?.LogInfo($"   Expected Length: {cleanExpected.Length} chars");
                
                // Ensure not empty
                if (string.IsNullOrWhiteSpace(cleanInput))
                {
                    _logger?.LogWarning($"   [WARNING] Input is empty/whitespace! Using single space.");
                    cleanInput = " ";
                }
                if (string.IsNullOrWhiteSpace(cleanExpected))
                {
                    _logger?.LogWarning($"   [WARNING] Expected is empty/whitespace! Using single space.");
                    cleanExpected = " ";
                }
                
                // Build step element
                xml.Append($"<step id=\"{stepId}\" type=\"ActionStep\">");
                
                // CRITICAL DISCOVERY: ADO expects the HTML structure to be XML-escaped!
                // Manual test case shows: &lt;DIV&gt;&lt;P&gt;text&lt;/P&gt;&lt;/DIV&gt;
                // NOT: <DIV><P>text</P></DIV>
                // The HTML tags themselves must be escaped as XML entities!
                //
                // ALSO CRITICAL: Only 2 parameterizedString elements per step (Action and Expected)
                // The 3rd one (description) should NOT be included!
                
                // First parameterizedString: Action
                xml.Append("<parameterizedString isformatted=\"true\">");
                xml.Append(System.Net.WebUtility.HtmlEncode("<DIV><P>" + cleanInput + "</P></DIV>"));
                xml.Append("</parameterizedString>");
                
                _logger?.LogInfo($"   Generated Action XML: {System.Net.WebUtility.HtmlEncode("<DIV><P>" + cleanInput.Substring(0, Math.Min(30, cleanInput.Length)) + "...</P></DIV>")}");
                
                // Second parameterizedString: Expected Result  
                xml.Append("<parameterizedString isformatted=\"true\">");
                xml.Append(System.Net.WebUtility.HtmlEncode("<DIV><P>" + cleanExpected + "</P></DIV>"));
                xml.Append("</parameterizedString>");
                
                _logger?.LogInfo($"   Generated Expected XML: {System.Net.WebUtility.HtmlEncode("<DIV><P>" + cleanExpected.Substring(0, Math.Min(30, cleanExpected.Length)) + "...</P></DIV>")}");
                
                // NO THIRD parameterizedString! ADO manual test cases only have 2.
                // The description element is sufficient.
                
                // Description element (required, empty)
                xml.Append("<description/>");
                
                xml.Append("</step>");
            }
            
            xml.Append("</steps>");
            
            var finalXml = xml.ToString();
            _logger?.LogInfo($"[XML_BUILD] FINAL XML: {finalXml.Length} characters");
            _logger?.LogInfo($"[XML_BUILD] First 500 chars of XML:");
            _logger?.LogInfo($"   {SafeSubstring(finalXml, 500)}");
            
            return finalXml;
        }

        private static string SafeSubstring(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return "(null or empty)";
            if (text.Length <= maxLength)
                return text;
            return text.Substring(0, maxLength) + "...";
        }

        /// <summary>
        /// Clean Rally HTML for ADO - convert to plain text but preserve line breaks
        /// ADO Steps UI expects plain text content, not HTML
        /// </summary>
        private static string CleanRallyHtmlForAdo(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;
            
            // Decode HTML entities
            html = System.Net.WebUtility.HtmlDecode(html);
            
            // Convert common HTML elements to plain text equivalents
            // Preserve line breaks
            html = System.Text.RegularExpressions.Regex.Replace(html, @"<br\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            html = System.Text.RegularExpressions.Regex.Replace(html, @"</p>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            html = System.Text.RegularExpressions.Regex.Replace(html, @"</div>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            html = System.Text.RegularExpressions.Regex.Replace(html, @"</li>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Remove all other HTML tags
            html = System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", string.Empty);
            
            // Clean up whitespace
            html = html.Trim();
            
            // Normalize line breaks (but keep them!)
            html = System.Text.RegularExpressions.Regex.Replace(html, @"\r\n|\r|\n", "\n");
            
            // Remove excessive blank lines (more than 2 consecutive)
            html = System.Text.RegularExpressions.Regex.Replace(html, @"\n{3,}", "\n\n");
            
            // Remove control characters except newlines
            html = System.Text.RegularExpressions.Regex.Replace(html, @"[\x00-\x09\x0B\x0C\x0E-\x1F\x7F]", string.Empty);
            
            return html;
        }

        /// <summary>
        /// XML escape for text content - CRITICAL for ADO parsing
        /// This escapes XML special characters but preserves line breaks
        /// </summary>
        private static string XmlEscapeContent(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            
            // CRITICAL: Escape the 5 XML predefined entities
            // Order matters! & must be first to avoid double-escaping
            text = text.Replace("&", "&amp;");
            text = text.Replace("<", "&lt;");
            text = text.Replace(">", "&gt;");
            text = text.Replace("\"", "&quot;");
            text = text.Replace("'", "&apos;");
            
            // Line breaks should be preserved as-is in XML text nodes
            // ADO will render them in the Steps UI
            
            return text;
        }

        /// <summary>
        /// Preserve HTML formatting from Rally but sanitize for ADO
        /// ADO expects content wrapped in <P> tags
        /// </summary>
        private static string PreserveAndSanitizeHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return "<P></P>";
            
            // Decode HTML entities first
            html = System.Net.WebUtility.HtmlDecode(html);
            
            // Remove dangerous tags (security)
            html = RemoveDangerousTags(html);
            
            // Clean up whitespace but preserve structure
            html = html.Trim();
            
            // If content doesn't have any paragraph tags, wrap it
            if (html.IndexOf("<p>", StringComparison.OrdinalIgnoreCase) < 0 && 
                html.IndexOf("<P>", StringComparison.Ordinal) < 0)
            {
                // Check if it has other block elements
                if (html.IndexOf("<div>", StringComparison.OrdinalIgnoreCase) < 0 &&
                    html.IndexOf("<ul>", StringComparison.OrdinalIgnoreCase) < 0 &&
                    html.IndexOf("<ol>", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    // Plain text or inline elements only - wrap in <P>
                    html = $"<P>{html}</P>";
                }
                else
                {
                    // Has block elements - convert DIVs to Ps if needed
                    html = System.Text.RegularExpressions.Regex.Replace(html, "<div>", "<P>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    html = System.Text.RegularExpressions.Regex.Replace(html, "</div>", "</P>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    html = html.Replace("<DIV>", "<P>");
                    html = html.Replace("</DIV>", "</P>");
                }
            }
            
            // Ensure P tags are uppercase (ADO convention)
            html = System.Text.RegularExpressions.Regex.Replace(html, "<p>", "<P>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            html = System.Text.RegularExpressions.Regex.Replace(html, "</p>", "</P>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Remove control characters but keep line breaks
            html = System.Text.RegularExpressions.Regex.Replace(html, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", string.Empty);
            
            return html;
        }

        /// <summary>
        /// Remove dangerous HTML tags for security
        /// </summary>
        private static string RemoveDangerousTags(string html)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;
            
            // Remove script tags and their content
            html = System.Text.RegularExpressions.Regex.Replace(html, @"<script[^>]*>.*?</script>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            
            // Remove dangerous tags
            var dangerousTags = new[] { "script", "iframe", "object", "embed", "applet", "meta", "link", "style" };
            foreach (var tag in dangerousTags)
            {
                html = System.Text.RegularExpressions.Regex.Replace(html, $@"<{tag}[^>]*>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                html = System.Text.RegularExpressions.Regex.Replace(html, $@"</{tag}>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            
            // Remove event handlers
            html = System.Text.RegularExpressions.Regex.Replace(html, @"\son\w+\s*=\s*[""'][^""']*[""']", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            return html;
        }

        /// <summary>
        /// Strip ALL HTML tags to get plain text (kept for backward compatibility if needed)
        /// </summary>
        private static string StripAllHtmlTags(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;
            
            // Remove all HTML tags
            var plainText = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
            
            // Decode HTML entities
            plainText = System.Net.WebUtility.HtmlDecode(plainText);
            
            // Clean up whitespace
            plainText = plainText.Trim();
            plainText = System.Text.RegularExpressions.Regex.Replace(plainText, @"\s+", " ");
            
            // Remove control characters
            plainText = System.Text.RegularExpressions.Regex.Replace(plainText, @"[\x00-\x1F\x7F]", string.Empty);
            
            return plainText;
        }

        /// <summary>
        /// XML escape - CRITICAL: Use proper XML escaping for text nodes
        /// This is different from HTML encoding!
        /// </summary>
        private static string XmlEscape(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            
            // CRITICAL: These are the 5 XML predefined entities
            // Must escape in this order to avoid double-escaping
            return text
                .Replace("&", "&amp;")   // MUST be first
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}




