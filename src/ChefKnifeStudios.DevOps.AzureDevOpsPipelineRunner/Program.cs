using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace ChefKnifeStudios.DevOps.AzureDevOpsPipelineRunner
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand("Azure DevOps pipeline tools");

            // Run pipeline command
            var runCommand = new Command("run", "Run an Azure DevOps pipeline");
            ConfigureRunPipelineCommand(runCommand);
            rootCommand.AddCommand(runCommand);

            // Approve pipeline command
            var approveCommand = new Command("approve", "Approve a pending pipeline approval");
            ConfigureApproveCommand(approveCommand);
            rootCommand.AddCommand(approveCommand);

            return await rootCommand.InvokeAsync(args);
        }

        static void ConfigureRunPipelineCommand(Command command)
        {
            var orgOption = new Option<string>("--org", "Azure DevOps organization name") { IsRequired = true };
            var projectOption = new Option<string>("--project", "Azure DevOps project name") { IsRequired = true };
            var pipelineIdOption = new Option<int>("--pipeline-id", "ID of the pipeline to run") { IsRequired = true };
            var branchOption = new Option<string>("--branch", () => "main", "Git branch to run the pipeline on");
            var tokenOption = new Option<string>("--token", "Azure DevOps personal access token") { IsRequired = true };
            var variablesOption = new Option<string>("--variables", "JSON string of variables to pass to the pipeline");

            command.AddOption(orgOption);
            command.AddOption(projectOption);
            command.AddOption(pipelineIdOption);
            command.AddOption(branchOption);
            command.AddOption(tokenOption);
            command.AddOption(variablesOption);

            command.SetHandler(async (string organization, string project, int pipelineId, string branch, string token, string variablesJson) =>
            {
                await RunPipeline(organization, project, pipelineId, branch, token, variablesJson);
            }, orgOption, projectOption, pipelineIdOption, branchOption, tokenOption, variablesOption);
        }

        static void ConfigureApproveCommand(Command command)
        {
            var orgOption = new Option<string>("--org", "Azure DevOps organization name") { IsRequired = true };
            var projectOption = new Option<string>("--project", "Azure DevOps project name") { IsRequired = true };
            var approvalIdOption = new Option<int>("--approval-id", "ID of the approval to process") { IsRequired = true };
            var tokenOption = new Option<string>("--token", "Azure DevOps personal access token") { IsRequired = true };
            var commentOption = new Option<string>("--comment", () => "Approved programmatically", "Comment for the approval");
            var statusOption = new Option<string>("--status", () => "approved", "Status of the approval (approved or rejected)");

            command.AddOption(orgOption);
            command.AddOption(projectOption);
            command.AddOption(approvalIdOption);
            command.AddOption(tokenOption);
            command.AddOption(commentOption);
            command.AddOption(statusOption);

            command.SetHandler(async (string organization, string project, int approvalId, string token, string comment, string status) =>
            {
                await ApproveStep(organization, project, approvalId, token, comment, status);
            }, orgOption, projectOption, approvalIdOption, tokenOption, commentOption, statusOption);
        }

        static async Task RunPipeline(string organization, string project, int pipelineId, string branch, string token, string variablesJson)
        {
            Console.WriteLine($"Triggering pipeline {pipelineId} on branch {branch}...");

            using var client = new HttpClient();
            
            // Create authorization header with PAT
            var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{token}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Prepare request payload
            var payload = new Dictionary<string, object>
            {
                ["resources"] = new Dictionary<string, object>
                {
                    ["repositories"] = new Dictionary<string, object>
                    {
                        ["self"] = new Dictionary<string, object>
                        {
                            ["refName"] = $"refs/heads/{branch}"
                        }
                    }
                }
            };

            // Add variables if provided
            if (!string.IsNullOrEmpty(variablesJson))
            {
                try
                {
                    var variables = JsonSerializer.Deserialize<Dictionary<string, string>>(variablesJson);
                    var variablesPayload = new Dictionary<string, object>();
                    
                    foreach (var variable in variables)
                    {
                        variablesPayload[variable.Key] = new Dictionary<string, string>
                        {
                            ["value"] = variable.Value
                        };
                    }
                    
                    payload["variables"] = variablesPayload;
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Error parsing variables JSON: {ex.Message}");
                    return;
                }
            }

            // API URL to run the pipeline
            var url = $"https://dev.azure.com/{organization}/{project}/_apis/pipelines/{pipelineId}/runs?api-version=6.0";

            try
            {
                // Make the API request
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);
                
                // Handle the response
                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonResponse);
                    
                    Console.WriteLine("Pipeline run triggered successfully!");
                    Console.WriteLine($"Run ID: {result["id"]}");
                    Console.WriteLine($"Pipeline name: {result["name"]}");
                    Console.WriteLine($"State: {result["state"]}");
                    
                    if (result.ContainsKey("_links") && 
                        result["_links"].TryGetProperty("web", out var webLink) && 
                        webLink.TryGetProperty("href", out var href))
                    {
                        Console.WriteLine($"URL: {href}");
                    }
                    
                    Console.WriteLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
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
        }

        static async Task ApproveStep(string organization, string project, int approvalId, string token, string comment, string status)
        {
            Console.WriteLine($"Processing approval with ID {approvalId}...");

            using var client = new HttpClient();
            
            // Create authorization header with PAT
            var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{token}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Validate status input
            string approvalStatus = status.ToLower();
            if (approvalStatus != "approved" && approvalStatus != "rejected")
            {
                Console.WriteLine("Error: Status must be either 'approved' or 'rejected'");
                return;
            }

            // Prepare request payload
            var payload = new Dictionary<string, object>
            {
                ["status"] = approvalStatus,
                ["comments"] = comment
            };

            // API URL to update the approval
            var url = $"https://dev.azure.com/{organization}/{project}/_apis/pipelines/approvals/{approvalId}?api-version=6.0-preview.1";

            try
            {
                // Make the API request (PATCH)
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
                var response = await client.SendAsync(request);
                
                // Handle the response
                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonResponse);
                    
                    Console.WriteLine($"Approval {approvalId} processed successfully!");
                    Console.WriteLine($"Status: {result["status"]}");
                    Console.WriteLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }
                else
                {
                    Console.WriteLine($"Error: {response.StatusCode}");
                    Console.WriteLine(await response.Content.ReadAsStringAsync());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing approval: {ex.Message}");
            }
        }

        // Additional utility method to get pending approvals
        static async Task<List<Dictionary<string, object>>> GetPendingApprovals(string organization, string project, string token)
        {
            using var client = new HttpClient();
            
            // Create authorization header with PAT
            var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{token}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // API URL to get pending approvals
            var url = $"https://dev.azure.com/{organization}/{project}/_apis/pipelines/approvals?statusFilter=pending&api-version=6.0-preview.1";

            try
            {
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonResponse);
                    
                    // Convert to a more usable format
                    var approvals = new List<Dictionary<string, object>>();
                    if (result.TryGetValue("value", out var value) && value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var approval in value.EnumerateArray())
                        {
                            var approvalDict = new Dictionary<string, object>();
                            foreach (var property in approval.EnumerateObject())
                            {
                                approvalDict[property.Name] = ConvertJsonElement(property.Value);
                            }
                            approvals.Add(approvalDict);
                        }
                    }
                    
                    return approvals;
                }
                else
                {
                    Console.WriteLine($"Error: {response.StatusCode}");
                    Console.WriteLine(await response.Content.ReadAsStringAsync());
                    return new List<Dictionary<string, object>>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting pending approvals: {ex.Message}");
                return new List<Dictionary<string, object>>();
            }
        }

        // Helper method to convert JsonElement to appropriate .NET types
        static object ConvertJsonElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var obj = new Dictionary<string, object>();
                    foreach (var property in element.EnumerateObject())
                    {
                        obj[property.Name] = ConvertJsonElement(property.Value);
                    }
                    return obj;
                
                case JsonValueKind.Array:
                    var array = new List<object>();
                    foreach (var item in element.EnumerateArray())
                    {
                        array.Add(ConvertJsonElement(item));
                    }
                    return array;
                
                case JsonValueKind.String:
                    return element.GetString();
                
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out int intValue))
                        return intValue;
                    if (element.TryGetInt64(out long longValue))
                        return longValue;
                    return element.GetDouble();
                
                case JsonValueKind.True:
                    return true;
                
                case JsonValueKind.False:
                    return false;
                
                case JsonValueKind.Null:
                    return null;
                
                default:
                    return null;
            }
        }
    }
}