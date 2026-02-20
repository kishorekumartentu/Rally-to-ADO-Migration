using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Rally_to_ADO_Migration.Models;
using System.Text;

namespace Rally_to_ADO_Migration.Services
{
    public class LoggingService
    {
        private readonly string _logDirectory;
        private readonly string _logFileName;
        private readonly object _lockObject = new object();

        public LoggingService()
        {
            _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rally to ADO Migration", "Logs");
            Directory.CreateDirectory(_logDirectory);
            _logFileName = Path.Combine(_logDirectory, $"Migration_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        }

        public void LogInfo(string message)
        {
            WriteLog("INFO", message);
        }

        public void LogWarning(string message)
        {
            WriteLog("WARNING", message);
        }

        public void LogError(string message, Exception exception = null)
        {
            var errorMessage = exception != null ? $"{message} - {exception.Message}\n{exception.StackTrace}" : message;
            WriteLog("ERROR", errorMessage);
        }

        public void LogDebug(string message)
        {
            WriteLog("DEBUG", message);
        }

        private void WriteLog(string level, string message)
        {
            lock (_lockObject)
            {
                try
                {
                    var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                    File.AppendAllText(_logFileName, logEntry + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    // If logging fails, write to event log or console as fallback
                    Console.WriteLine($"Logging failed: {ex.Message}");
                }
            }
        }

        public void ExportFailedItems(List<MigrationResult> failedResults, string outputPath = null)
        {
            if (outputPath == null)
            {
                outputPath = Path.Combine(_logDirectory, $"FailedMigrations_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            }

            try
            {
                var csv = new StringBuilder();
                csv.AppendLine("Rally ID,Rally Formatted ID,ADO ID,Error Message,Processed At,Skip Reason");

                foreach (var result in failedResults)
                {
                    csv.AppendLine($"\"{result.RallyId}\",\"{result.RallyFormattedId}\",\"{result.AdoId}\",\"{EscapeCsvValue(result.ErrorMessage)}\",\"{result.ProcessedAt:yyyy-MM-dd HH:mm:ss}\",\"{EscapeCsvValue(result.SkipReason)}\"");
                }

                File.WriteAllText(outputPath, csv.ToString());
                LogInfo($"Failed items exported to: {outputPath}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to export failed items to CSV", ex);
            }
        }

        public void ExportSkippedItems(List<MigrationResult> skippedResults, string outputPath = null)
        {
            if (outputPath == null)
            {
                outputPath = Path.Combine(_logDirectory, $"SkippedMigrations_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            }

            try
            {
                var csv = new StringBuilder();
                csv.AppendLine("Rally ID,Rally Formatted ID,Skip Reason,Processed At");

                foreach (var result in skippedResults)
                {
                    csv.AppendLine($"\"{result.RallyId}\",\"{result.RallyFormattedId}\",\"{EscapeCsvValue(result.SkipReason)}\",\"{result.ProcessedAt:yyyy-MM-dd HH:mm:ss}\"");
                }

                File.WriteAllText(outputPath, csv.ToString());
                LogInfo($"Skipped items exported to: {outputPath}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to export skipped items to CSV", ex);
            }
        }

        public void ExportMigrationSummary(MigrationProgress progress, string outputPath = null)
        {
            if (outputPath == null)
            {
                outputPath = Path.Combine(_logDirectory, $"MigrationSummary_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            }

            try
            {
                var summary = new StringBuilder();
                summary.AppendLine("RALLY TO ADO MIGRATION SUMMARY");
                summary.AppendLine("================================");
                summary.AppendLine($"Start Time: {progress.StartTime:yyyy-MM-dd HH:mm:ss}");
                summary.AppendLine($"End Time: {(progress.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "In Progress")}");
                summary.AppendLine($"Duration: {progress.ElapsedTime}");
                summary.AppendLine($"Status: {(progress.IsCompleted ? "Completed" : progress.IsPaused ? "Paused" : "In Progress")}");
                summary.AppendLine();
                summary.AppendLine("STATISTICS:");
                summary.AppendLine($"Total Items: {progress.TotalItems}");
                summary.AppendLine($"Processed Items: {progress.ProcessedItems}");
                summary.AppendLine($"Successful Items: {progress.SuccessfulItems}");
                summary.AppendLine($"Failed Items: {progress.FailedItems}");
                summary.AppendLine($"Skipped Items: {progress.SkippedItems}");
                summary.AppendLine($"Success Rate: {(progress.ProcessedItems > 0 ? (double)progress.SuccessfulItems / progress.ProcessedItems * 100 : 0):F2}%");
                summary.AppendLine();

                if (progress.Results.Any(r => !r.Success && !r.IsSkipped))
                {
                    summary.AppendLine("FAILED ITEMS:");
                    foreach (var failed in progress.Results.Where(r => !r.Success && !r.IsSkipped))
                    {
                        summary.AppendLine($"- {failed.RallyFormattedId}: {failed.ErrorMessage}");
                    }
                    summary.AppendLine();
                }

                if (progress.Results.Any(r => r.IsSkipped))
                {
                    summary.AppendLine("SKIPPED ITEMS:");
                    foreach (var skipped in progress.Results.Where(r => r.IsSkipped))
                    {
                        summary.AppendLine($"- {skipped.RallyFormattedId}: {skipped.SkipReason}");
                    }
                }

                File.WriteAllText(outputPath, summary.ToString());
                LogInfo($"Migration summary exported to: {outputPath}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to export migration summary", ex);
            }
        }

        private string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\"", "\"\"");
        }

        public string GetLogDirectory()
        {
            return _logDirectory;
        }

        public string GetLogFileName()
        {
            return _logFileName;
        }
    }
}