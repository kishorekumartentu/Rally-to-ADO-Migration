using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    public class RichContentAssembler
    {
        private static readonly Regex ColorTokenRegex = new Regex(@"\{color:([^}]+)\}(.*?)\{color\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex BulletRegex = new Regex(@"^(?:[*\-]|•)\s+(.*)$", RegexOptions.Multiline);
        private static readonly Regex NumberedRegex = new Regex(@"^\s*([0-9]+)[.)]\s+(.*)$", RegexOptions.Multiline);
        private static readonly Regex ExcessBrRegex = new Regex(@"(<br\s*/?>){2,}", RegexOptions.IgnoreCase);
        private static readonly Regex DangerousTagRegex = new Regex(@"<\s*(script|iframe|object)[^>]*>.*?<\s*/\s*\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex ImageRegex = new Regex(@"!\[([^\]]*)\]\(([^)]+)\)|<img[^>]+src=[""']([^""']+)[""'][^>]*>", RegexOptions.IgnoreCase);
        private static readonly Regex CodeBlockRegex = new Regex(@"```(?:(\w+)\n)?(.*?)```|{code(?:\:([^}]+))?\}(.*?){code}", RegexOptions.Singleline);

        public List<RallyAttachmentReference> AttachmentReferences { get; private set; }

        public RichContentAssembler()
        {
            AttachmentReferences = new List<RallyAttachmentReference>();
        }

        public string BuildUnifiedDescription(RallyWorkItem item)
        {
            if (item == null) return string.Empty;
            AttachmentReferences.Clear();

            var parts = new List<string>();
            
            // Title header - safe to encode
            var header = HtmlEncodeSafe($"{item.FormattedID} - {item.Name}");
            parts.Add($"<h2>{header}</h2>");
            
            // ALWAYS add Release information in bold at the top
            // If Release is empty/null, show "Unscheduled"
            var releaseValue = string.IsNullOrWhiteSpace(item.Release) ? "Unscheduled" : item.Release;
            var releaseInfo = HtmlEncodeSafe(releaseValue);
            parts.Add($"<p><strong>Release: {releaseInfo}</strong></p>");

            // Add Pre-Conditions for Test Cases in bold
            if (string.Equals(item.Type, "TestCase", StringComparison.OrdinalIgnoreCase) && 
                !string.IsNullOrWhiteSpace(item.PreConditions))
            {
                // Decode Unicode escapes first (Rally may encode HTML as \u003Cbr\u003E)
                var preConditionsDecoded = DecodeUnicodeEscapes(item.PreConditions);
                
                // Preserve HTML formatting but sanitize dangerous tags
                // PreConditions often contains <b>, <br>, etc. which should be preserved
                var preConditionsSafe = SanitizeExistingHtml(preConditionsDecoded);
                
                parts.Add($"<p><strong>PRE-CONDITIONS:</strong></p>");
                parts.Add($"<div class='preconditions-section'>{preConditionsSafe}</div>");
            }

            // MAIN DESCRIPTION CONTENT - This is the actual work item description
            var mainRaw = item.Description ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(mainRaw))
            {
                // Decode Unicode escapes like \u003Cbr\u003E to <br>
                mainRaw = DecodeUnicodeEscapes(mainRaw);
                
                // Add description content (preserve as-is, it's already HTML from Rally)
                parts.Add(ConvertPlainOrHtmlBlock(mainRaw, "Description"));
            }

            // Environment info for bugs
            var envRaw = TryGetCustom(item, "Environment") as string;
            if (!string.IsNullOrWhiteSpace(envRaw))
            {
                envRaw = DecodeUnicodeEscapes(envRaw);
                parts.Add(ConvertPlainOrHtmlBlock(envRaw, "Environment Details"));
            }

            // Notes section
            var notesRaw = item.Notes ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(notesRaw))
            {
                notesRaw = DecodeUnicodeEscapes(notesRaw);
                parts.Add(ConvertPlainOrHtmlBlock(notesRaw, "Discussion Notes"));
            }

            // Add Acceptance Criteria for Defects (Bugs) - placed BETWEEN Discussion Notes and Metadata
            if (string.Equals(item.Type, "Defect", StringComparison.OrdinalIgnoreCase) && 
                item.AcceptanceCriteria != null)
            {
                var acceptanceCriteriaText = item.AcceptanceCriteria.ToString();
                if (!string.IsNullOrWhiteSpace(acceptanceCriteriaText))
                {
                    // Decode Unicode escapes first
                    var acceptanceCriteriaDecoded = DecodeUnicodeEscapes(acceptanceCriteriaText);
                    
                    // Preserve HTML formatting but sanitize dangerous tags
                    var acceptanceCriteriaSafe = SanitizeExistingHtml(acceptanceCriteriaDecoded);
                    
                    parts.Add(ConvertPlainOrHtmlBlock(acceptanceCriteriaSafe, "Acceptance Criteria"));
                }
            }

            // Metadata table at the END (after all content sections)
            var meta = BuildMetadataTable(item);
            if (!string.IsNullOrWhiteSpace(meta)) parts.Add(meta);

            var combined = string.Join("\n", parts);
            // DON'T collapse breaks - preserve empty lines for readability
            return $"<div class='rally-content'>{combined}</div>";
        }

        /// <summary>
        /// Decode Unicode escape sequences like \u003C to actual characters
        /// </summary>
        private string DecodeUnicodeEscapes(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            return Regex.Replace(input, @"\\u([0-9A-Fa-f]{4})", match =>
            {
                var hexValue = match.Groups[1].Value;
                var charValue = (char)Convert.ToInt32(hexValue, 16);
                return charValue.ToString();
            });
        }

        private string ConvertPlainOrHtmlBlock(string input, string title)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var looksHtml = input.IndexOf('<') >= 0 && input.IndexOf('>') > input.IndexOf('<');
            string body = looksHtml ? SanitizeExistingHtml(input) : ConvertPlainTextToHtml(input);

            return $@"<div class='content-section'>
                <h3>{HtmlEncodeSafe(title)}</h3>
                <div class='content-body'>{body}</div>
            </div>";
        }

        private string SanitizeExistingHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;

            // Remove dangerous content
            html = DangerousTagRegex.Replace(html, string.Empty);

            // Handle code blocks (these need special encoding)
            html = CodeBlockRegex.Replace(html, m => {
                var lang = m.Groups[1].Success ? m.Groups[1].Value :
                          (m.Groups[3].Success ? m.Groups[3].Value : "");
                var code = m.Groups[2].Success ? m.Groups[2].Value :
                          (m.Groups[4].Success ? m.Groups[4].Value : "");
                return FormatCodeBlock(code, lang);
            });

            // Handle Rally color tokens but preserve the HTML inside
            html = ColorTokenRegex.Replace(html, m => 
                $"<span style='color:{m.Groups[1].Value}'>{m.Groups[2].Value}</span>");

            // Handle images (keep HTML structure but process attachment references)
            html = ImageRegex.Replace(html, m => {
                var altText = m.Groups[1].Success ? m.Groups[1].Value : "";
                var src = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
                
                return ProcessImage(src, altText);
            });

            // Normalize <br> tags but PRESERVE consecutive ones (they represent empty lines)
            html = html.Replace("<br>", "<br/>");
            
            // Convert double <br/> to paragraph breaks for better spacing
            html = html.Replace("<br/><br/>", "</p><p>");

            return html;
        }

        private string ConvertPlainTextToHtml(string text)
        {
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var processed = new List<string>();
            var bulletBuffer = new List<string>();
            var numberBuffer = new List<string>();
            var codeBuffer = new StringBuilder();
            var inCodeBlock = false;
            var codeLanguage = "";
            var emptyLineCount = 0;

            void flushBullets()
            {
                if (bulletBuffer.Count == 0) return;
                processed.Add("<ul>" + string.Join("", bulletBuffer.Select(b => $"<li>{b}</li>")) + "</ul>");
                bulletBuffer.Clear();
            }

            void flushNumbers()
            {
                if (numberBuffer.Count == 0) return;
                processed.Add("<ol>" + string.Join("", numberBuffer.Select(b => $"<li>{b}</li>")) + "</ol>");
                numberBuffer.Clear();
            }

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();

                // Handle code blocks
                if (line.StartsWith("```") || line.StartsWith("{code"))
                {
                    if (!inCodeBlock)
                    {
                        flushBullets();
                        flushNumbers();
                        inCodeBlock = true;
                        codeLanguage = line.StartsWith("```") ? 
                            (line.Length > 3 ? line.Substring(3) : "") :
                            (line.Contains(":") ? line.Split(':')[1].Replace("}", "") : "");
                        continue;
                    }
                    else if ((line == "```" && codeBuffer.Length > 0) || line == "{code}")
                    {
                        inCodeBlock = false;
                        processed.Add(FormatCodeBlock(codeBuffer.ToString(), codeLanguage));
                        codeBuffer.Clear();
                        continue;
                    }
                }

                if (inCodeBlock)
                {
                    codeBuffer.AppendLine(line);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    flushBullets();
                    flushNumbers();
                    emptyLineCount++;
                    
                    // Add paragraph break for empty line (better spacing in ADO)
                    processed.Add("</p><p>");
                    continue;
                }

                emptyLineCount = 0;
                
                var bulletMatch = BulletRegex.Match(line);
                if (bulletMatch.Success)
                {
                    flushNumbers();
                    bulletBuffer.Add(ProcessLine(bulletMatch.Groups[1].Value.Trim()));
                    continue;
                }

                var numMatch = NumberedRegex.Match(line);
                if (numMatch.Success)
                {
                    flushBullets();
                    numberBuffer.Add(ProcessLine(numMatch.Groups[2].Value.Trim()));
                    continue;
                }

                flushBullets();
                flushNumbers();
                processed.Add("<p>" + ProcessLine(line) + "</p>");
            }

            if (inCodeBlock)
            {
                processed.Add(FormatCodeBlock(codeBuffer.ToString(), codeLanguage));
            }
            flushBullets();
            flushNumbers();

            var result = string.Join("\n", processed);
            
            // Clean up any double paragraph tags
            result = Regex.Replace(result, @"</p>\s*<p>\s*</p>\s*<p>", "</p><p>");
            
            return result;
        }

        private string ProcessLine(string line)
        {
            // Check if line already contains HTML tags - if so, preserve them
            var containsHtml = line.IndexOf('<') >= 0 && line.IndexOf('>') > line.IndexOf('<');
            
            if (containsHtml)
            {
                // Handle Rally color tokens but preserve HTML
                line = ColorTokenRegex.Replace(line, m => 
                    $"<span style='color:{m.Groups[1].Value}'>{m.Groups[2].Value}</span>");

                // Handle images
                line = ImageRegex.Replace(line, m => {
                    var altText = m.Groups[1].Success ? m.Groups[1].Value : "";
                    var src = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
                    return ProcessImage(src, altText);
                });

                return line; // Return as-is without encoding
            }
            else
            {
                // Plain text - safe to encode special characters
                line = ColorTokenRegex.Replace(line, m => 
                    $"<span style='color:{HtmlEncodeSafe(m.Groups[1].Value)}'>{HtmlEncodeSafe(m.Groups[2].Value)}</span>");

                line = ImageRegex.Replace(line, m => {
                    var altText = m.Groups[1].Success ? m.Groups[1].Value : "";
                    var src = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
                    return ProcessImage(src, altText);
                });

                return HtmlEncodeSafe(line);
            }
        }

        private string ProcessImage(string src, string altText)
        {
            if (src.StartsWith("http://") || src.StartsWith("https://"))
            {
                return $"<img src='{HtmlEncodeSafe(src)}' alt='{HtmlEncodeSafe(altText)}' />";
            }

            var attachmentRef = new RallyAttachmentReference
            {
                OriginalPath = src,
                PlaceholderToken = $"__ATTACHMENT_{Guid.NewGuid()}__",
                AltText = altText,
                FileName = Path.GetFileName(src)
            };
            AttachmentReferences.Add(attachmentRef);
            return $"<img src='{attachmentRef.PlaceholderToken}' alt='{HtmlEncodeSafe(altText)}' />";
        }

        private string FormatCodeBlock(string code, string language)
        {
            var langClass = !string.IsNullOrEmpty(language) ? $" class='language-{HtmlEncodeSafe(language.Trim())}'" : "";
            return $"<pre><code{langClass}>{HtmlEncodeSafe(code.Trim())}</code></pre>";
        }

        private string BuildMetadataTable(RallyWorkItem item)
        {
            var rows = new List<(string key,string value)>();
            void add(string k, object v)
            {
                if(v == null) return;
                var s = v.ToString();
                if(string.IsNullOrWhiteSpace(s)) return;
                rows.Add((k,s));
            }

            // Core fields
            add("State", item.State);
            add("Priority", item.Priority);
            add("Severity", item.Severity);
            add("Blocked", item.Blocked.HasValue ? (item.Blocked.Value?"Yes":"No") : null);
            
            // Bug-specific fields
            add("Found In Build", TryGetCustom(item, "c_FoundInBuild"));
            add("Integrated In Build", TryGetCustom(item, "c_IntegratedInBuild"));
            add("Environment", TryGetCustom(item, "Environment"));
            add("Reported By", TryGetCustom(item, "ReportedBy"));
            add("Created", item.CreationDate?.ToString("yyyy-MM-dd HH:mm"));
            add("Last Updated", item.LastUpdateDate?.ToString("yyyy-MM-dd HH:mm"));

            foreach (var kv in ExtractSpecialMetadata(item)) add(kv.Key, kv.Value);
            
            if (!rows.Any()) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("<h3>Metadata</h3>");
            sb.AppendLine("<table border='1' cellpadding='4' cellspacing='0' style='border-collapse:collapse'>");
            foreach (var r in rows)
            {
                sb.AppendFormat(
                    "<tr><th style='text-align:left;background-color:#f5f5f5'>{0}</th><td>{1}</td></tr>",
                    HtmlEncodeSafe(r.key),
                    HtmlEncodeSafe(r.value)
                );
            }
            sb.AppendLine("</table>");
            return sb.ToString();
        }

        private Dictionary<string,string> ExtractSpecialMetadata(RallyWorkItem item)
        {
            var result = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> sources = new[] { item.Description ?? string.Empty, item.Notes ?? string.Empty };
            foreach (var src in sources)
            {
                if (string.IsNullOrWhiteSpace(src)) continue;
                var lines = src.Replace("\r\n","\n").Replace("\r","\n").Split('\n');
                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    var mClinical = Regex.Match(line, @"^Clinical Impact\s*-\s*(.+)$", RegexOptions.IgnoreCase);
                    if (mClinical.Success && !result.ContainsKey("Clinical Impact")) result["Clinical Impact"] = StripColorTokens(mClinical.Groups[1].Value.Trim());
                    var mOperational = Regex.Match(line, @"^Operational Impact\s*-\s*(.+)$", RegexOptions.IgnoreCase);
                    if (mOperational.Success && !result.ContainsKey("Operational Impact")) result["Operational Impact"] = StripColorTokens(mOperational.Groups[1].Value.Trim());
                    var mJiraBy = Regex.Match(line, @"^\[JIRA Item Created By:\s*(.+?)\]$", RegexOptions.IgnoreCase);
                    if (mJiraBy.Success && !result.ContainsKey("JIRA Created By")) result["JIRA Created By"] = mJiraBy.Groups[1].Value.Trim();
                    var mJiraAt = Regex.Match(line, @"^\[JIRA Item Created At:\s*(.+?)\]$", RegexOptions.IgnoreCase);
                    if (mJiraAt.Success && !result.ContainsKey("JIRA Created At")) result["JIRA Created At"] = mJiraAt.Groups[1].Value.Trim();
                }
            }
            return result;
        }

        private string StripColorTokens(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return ColorTokenRegex.Replace(input, m => m.Groups[2].Value);
        }

        private object TryGetCustom(RallyWorkItem item, string key)
        {
            if(item.CustomFields != null && item.CustomFields.TryGetValue(key, out var v))
                return v;
            return null;
        }

        private string CollapseBreaks(string html) => ExcessBrRegex.Replace(html, "<br/>");

        private string HtmlEncodeSafe(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }
    }
}
