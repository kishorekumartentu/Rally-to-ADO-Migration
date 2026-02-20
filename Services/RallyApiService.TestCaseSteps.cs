using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using Rally_to_ADO_Migration.Models;

namespace Rally_to_ADO_Migration.Services
{
    // Partial extension for TestCase steps enrichment
    public partial class RallyApiService
    {
        public async Task EnrichTestCaseStepsAsync(RallyWorkItem workItem, ConnectionSettings settings)
        {
            try
            {
                if (workItem == null || !string.Equals(workItem.Type, "TestCase", StringComparison.OrdinalIgnoreCase)) return;
                if (string.IsNullOrEmpty(workItem.ObjectID)) return;
                ConfigureConnection(settings); // ensure connection vars
                var stepsUrl = $"{_serverUrl}/slm/webservice/v2.0/testcasestep?workspace=/workspace/{_workspace}&query=(TestCase.ObjectID%20=%20{workItem.ObjectID})&fetch=StepIndex,Input,ExpectedResult";
                var request = new HttpRequestMessage(HttpMethod.Get, stepsUrl);
                AddAuthenticationHeader(request);
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _loggingService.LogWarning($"TestCase steps fetch failed for {workItem.FormattedID}: {response.StatusCode}");
                    return;
                }
                var content = await response.Content.ReadAsStringAsync();
                var resultsStart = content.IndexOf("\"Results\":");
                if (resultsStart < 0) return;
                var arrayStart = content.IndexOf('[', resultsStart);
                var arrayEnd = FindMatchingBracket(content, arrayStart);
                if (arrayStart < 0 || arrayEnd < 0) return;
                var arrayJson = content.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
                var objects = SplitJsonObjects(arrayJson);
                foreach (var obj in objects)
                {
                    var stepIndexStr = ExtractJsonValue(obj, "StepIndex");
                    int stepIndex = 0; int.TryParse(stepIndexStr, out stepIndex);
                    var input = ExtractJsonValue(obj, "Input");
                    var expected = ExtractJsonValue(obj, "ExpectedResult");
                    if (!string.IsNullOrEmpty(input) || !string.IsNullOrEmpty(expected))
                    {
                        workItem.Steps.Add(new RallyTestCaseStep { StepIndex = stepIndex, Input = input, ExpectedResult = expected });
                    }
                }
                workItem.Steps = workItem.Steps.OrderBy(s => s.StepIndex).ToList();
                _loggingService.LogInfo($"Fetched {workItem.Steps.Count} test case steps for {workItem.FormattedID}");
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning($"EnrichTestCaseStepsAsync exception for {workItem?.FormattedID}: {ex.Message}");
            }
        }
    }
}