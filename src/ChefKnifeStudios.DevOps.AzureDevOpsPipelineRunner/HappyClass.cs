using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ChefKnifeStudios.DevOps.AzureDevOpsPipelineRunner;

public class HappyClass
{
    readonly string _org = string.Empty;
    readonly string _project = string.Empty;
    readonly string _branch = string.Empty;
    readonly string _token = string.Empty;
    readonly HttpClient _httpClient;
    readonly string _approvalStatus = string.Empty;
    readonly string _comment = string.Empty;

    public HappyClass(string org, string project, string branch, string token, string comment, string approvalStatus)
    {
        _org = org;
        _project = project;
        _branch = branch;
        _token = token;
        _comment = comment;
        _approvalStatus = approvalStatus;

        _httpClient = new HttpClient();
        var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{token}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task HappyFunctionAsync(int pipelineId, string[] stagesToSkip)
    {
        int runId = await RunPipelineAsync(pipelineId, stagesToSkip);
        //int buildId = await GetLatestBuildAsync(pipelineId);

        int timeoutMs = 10 * 60 * 60 * 1000;
        bool isTimedOut = false;
        TimingFunctions.SetTimeout(() => { isTimedOut = true; }, timeoutMs);

        while (!isTimedOut)
        {
            string approvalId = await GetFirstBuildApprovalAsync(runId);
            if (string.IsNullOrWhiteSpace(approvalId)) continue;
            await ApproveReviewAsync(approvalId);
            isTimedOut = true;
        }
    }

    async Task<int> RunPipelineAsync(int pipelineId, string[] stagesToSkip)
    {
        Console.WriteLine("Start RunPipelineAsync");
        int result = -1; 

        var payload = new Dictionary<string, object>
        {
            ["resources"] = new Dictionary<string, object>
            {
                ["repositories"] = new Dictionary<string, object>
                {
                    ["self"] = new Dictionary<string, object>
                    {
                        ["refName"] = $"refs/heads/{_branch}"
                    }
                }
            },
            ["stagesToSkip"] = new string[] { "StageDeploy", "ProdDeploy" }
        };

        // API URL to run the pipeline
        var url = $"https://dev.azure.com/{_org}/{_project}/_apis/pipelines/{pipelineId}/runs?api-version=7.1";

        try
        {
            // Make the API request
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);

            // Handle the response
            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var res = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonResponse);

                Console.WriteLine($"Pipeline run triggered successfully! {res["id"].ToString()}");

                result = int.Parse(res["id"].ToString());
            }
            else
            {
                Console.WriteLine($"Error: {response.StatusCode}");
                Console.WriteLine(await response.Content.ReadAsStringAsync());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error triggering pipeline: {ex.Message}");
        }

        Console.WriteLine($"End RunPipelineAsync {result}");
        return result;
    }

    async Task<int> GetLatestBuildAsync(int pipelineId)
    {
        Console.WriteLine("Start GetLatestBuildAsync");
        int maxId = -1;

        string url = $"https://dev.azure.com/{_org}/{_project}/_apis/build/builds?api-version=7.1";

        try
        { 
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var resBody = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonResponse);

                foreach (JsonElement item in resBody["value"].EnumerateArray())
                {
                    if (int.Parse(item.GetProperty("definition").GetProperty("id").ToString()) != pipelineId) continue;

                    if (int.Parse(item.GetProperty("id").ToString()) > maxId)
                    {
                        maxId = int.Parse(item.GetProperty("id").ToString());
                    }
                }
            }
            else
            {
                Console.WriteLine($"Error: {response.StatusCode}");
                Console.WriteLine(await response.Content.ReadAsStringAsync());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting latest build: {ex.Message}");
        }

        Console.WriteLine($"End GetLatestBuildAsync {maxId}");
        return maxId;
    }

    async Task<string> GetFirstBuildApprovalAsync(int buildId)
    {
        Console.WriteLine("Start GetFirstBuildApprovalAsync");
        var result = string.Empty;

        string url = $"https://dev.azure.com/{_org}/{_project}/_apis/build/builds/{buildId}/timeline?api-version=7.1";

        try
        {
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var resBody = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonResponse);
                Console.WriteLine(jsonResponse);

                foreach (JsonElement item in resBody["records"].EnumerateArray())
                {
                    if (item.GetProperty("type").ToString() == "Checkpoint.Approval")
                    {
                        result = item.GetProperty("id").ToString();
                        break;
                    }
                }
            }
            else
            {
                Console.WriteLine($"Error: {response.StatusCode}");
                Console.WriteLine(await response.Content.ReadAsStringAsync());
            }
        }
        catch (Exception ex)
        {

            Console.WriteLine($"Error getting latest build approval: {ex.Message}");
        }

        Console.WriteLine($"End GetFirstBuildApprovalAsync {result}");
        return result;
    }

    async Task ApproveReviewAsync(string approvalId)
    {
        Console.WriteLine("Start ApproveReviewAsync");
        var url = $"https://dev.azure.com/{_org}/{_project}/_apis/pipelines/approvals?api-version=7.1";
        var payload = new[]
        {
            new
            {
                approvalId = approvalId,
                comment = _comment,
                status = _approvalStatus
            }
        };

        try
        {
            // Serialize payload with camelCase property names
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var jsonContent = JsonSerializer.Serialize(payload, options);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Send PATCH request
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var resBody = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonResponse);
            }
            else
            {
                Console.WriteLine($"Error: {response.StatusCode}");
                Console.WriteLine(await response.Content.ReadAsStringAsync());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error approving {approvalId}: {ex.Message}");
        }
        Console.WriteLine("End ApproveReviewAsync");
    }
}
