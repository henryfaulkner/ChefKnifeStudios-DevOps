using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.Configuration;

namespace ChefKnifeStudios.DevOps.AzureDevOpsPipelineRunner
{
    class Program
    {
        const string APPROVAL_STATUS = "approved";
        const string APPROVAL_COMMENT = "Approved by automation";

        private static IConfiguration Configuration = null!;

        static async Task<int> Main(string[] args)
        {
            // Load configuration from appsettings.json
            var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";
            Configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
                .Build();

            var rootCommand = new RootCommand("Azure DevOps pipeline tools");

            // Run pipeline command
            var runCommand = new Command("run-pipeline", "Run an Azure DevOps pipeline");
            ConfigureRunPipelineCommand(runCommand);
            rootCommand.AddCommand(runCommand);

            // Approve pipeline command
            var approveCommand = new Command("approve", "Approve a pending pipeline approval");
            ConfigureApproveCommand(approveCommand);
            rootCommand.AddCommand(approveCommand);

            // List approvals command
            var listApprovalsCommand = new Command("list-approvals", "List all pending approvals");
            ConfigureListApprovalsCommand(listApprovalsCommand);
            rootCommand.AddCommand(listApprovalsCommand);

            // List and approve pending approvals command
            var listAndApproveCommand = new Command("approve-latest", "Approve latest pending DevOps approval request")
            {
                Description = "This command will list all pending approvals and approve each one automatically."
            };
            ConfigureListAndApproveApprovalsCommand(listAndApproveCommand);
            rootCommand.AddCommand(listAndApproveCommand);

            var doTheThingCommand = new Command("do-it", "Do the thing I am trying to do")
            {
                Description = "This command will do the thing I am trying to do."
            };
            ConfigureDoTheThingCommand(doTheThingCommand);
            rootCommand.AddCommand(doTheThingCommand);


            return await rootCommand.InvokeAsync(args);
        }

        static void ConfigureRunPipelineCommand(Command command)
        {
            var pipelineIdOption = new Option<int>("--pipeline-id", "ID of the pipeline to run") { IsRequired = true };
            var variablesOption = new Option<string>("--variables", "JSON string of variables to pass to the pipeline");

            command.AddOption(pipelineIdOption);
            command.AddOption(variablesOption);

            command.SetHandler(async (int pipelineId, string variablesJson) =>
            {
                string organization = Configuration["AzureDevOps:Organization"] ?? string.Empty;
                string project = Configuration["AzureDevOps:Project"] ?? string.Empty;
                string branch = Configuration["AzureDevOps:Branch"] ?? string.Empty;
                string token = Configuration["AzureDevOps:PersonalAccessToken"] ?? string.Empty;

                await RunPipeline(organization, project, pipelineId, branch, token, variablesJson);
            }, pipelineIdOption, variablesOption);
        }

        static void ConfigureApproveCommand(Command command)
        {
            var approvalIdOption = new Option<string>("--approval-id", "ID of the approval to process") { IsRequired = true };

            command.AddOption(approvalIdOption);

            command.SetHandler(async (string approvalId) =>
            {
                string organization = Configuration["AzureDevOps:Organization"] ?? string.Empty;
                string project = Configuration["AzureDevOps:Project"] ?? string.Empty;
                string token = Configuration["AzureDevOps:PersonalAccessToken"] ?? string.Empty;

                await ApproveStep(organization, project, approvalId, token, APPROVAL_COMMENT, APPROVAL_STATUS);
            }, approvalIdOption);
        }

        static void ConfigureListApprovalsCommand(Command command)
        {
            command.SetHandler(async () =>
            {
                string organization = Configuration["AzureDevOps:Organization"] ?? string.Empty;
                string project = Configuration["AzureDevOps:Project"] ?? string.Empty;
                string token = Configuration["AzureDevOps:PersonalAccessToken"] ?? string.Empty;

                await ListLatestPendingApprovals(organization, project, token);
            });
        }

        static void ConfigureListAndApproveApprovalsCommand(Command command)
        {
            command.SetHandler(async () =>
            {
                string organization = Configuration["AzureDevOps:Organization"] ?? string.Empty;
                string project = Configuration["AzureDevOps:Project"] ?? string.Empty;
                string token = Configuration["AzureDevOps:PersonalAccessToken"] ?? string.Empty;

                await ListAndApprovePendingApprovals(organization, project, token, APPROVAL_COMMENT, APPROVAL_STATUS);
            });
        }

        static void ConfigureDoTheThingCommand(Command command)
        {
            var pipelineOption = new Option<string>("--pipeline", "The pipeline config to run") { IsRequired = true };
            
            command.AddOption(pipelineOption);
            command.SetHandler(async (string pipeline) =>
            {
                string organization = Configuration["AzureDevOps:Organization"] ?? string.Empty;
                string project = Configuration["AzureDevOps:Project"] ?? string.Empty;
                string branch = Configuration["AzureDevOps:Branch"] ?? string.Empty;
                string token = Configuration["AzureDevOps:PersonalAccessToken"] ?? string.Empty;
                HappyClass hc = new HappyClass(organization, project, branch, token, APPROVAL_COMMENT, APPROVAL_STATUS);

                int pipelineId = int.Parse(Configuration[$"Pipelines:{pipeline}:PipelineId"]);
                string[] stagesToSkip = Configuration.GetSection($"Pipelines:{pipeline}:StagesToSkip").GetChildren().ToArray().Select(c => c.Value).ToArray();

                await DoTheThingAsync(hc, pipelineId, stagesToSkip);
            }, pipelineOption);
        }

        // stagesToSkip
        // https://stackoverflow.com/questions/71549915/using-rest-api-to-trigger-a-specific-stage-within-a-yaml-pipeline
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
                },
                ["stagesToSkip"] = new string[] { "StageDeploy", "ProdDeploy" }
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
            var url = $"https://dev.azure.com/{organization}/{project}/_apis/pipelines/{pipelineId}/runs?api-version=7.1";

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

        static async Task ApproveStep(string organization, string project, string approvalId, string token, string comment, string status)
        {
            Console.WriteLine($"Processing approval with ID {approvalId}... {status} {comment}");

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

            // Prepare request payload as an array
            var payload = new[]
            {
                new
                {
                    approvalId = approvalId,
                    comment = comment,
                    status = approvalStatus
                }
            };

            // API endpoint
            var url = $"https://dev.azure.com/{organization}/{project}/_apis/pipelines/approvals?api-version=7.1";

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
                var response = await client.SendAsync(request);

                // Handle the response
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Approval {approvalId} processed successfully!");
                    var responseBody = await response.Content.ReadAsStringAsync();
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

        static async Task<List<JsonElement>> ListLatestPendingApprovals(string organization, string project, string token)
        {
            List<JsonElement> result = new List<JsonElement>();

            Console.WriteLine("Retrieving the latest pending approvals for each pipeline in the QA environment (using API 7.1)...");

            using var client = new HttpClient();

            // Create authorization header with PAT
            var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{token}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // API URL to get all pending approvals
            var url = $"https://dev.azure.com/{organization}/{project}/_apis/pipelines/approvals?statusFilter=pending&api-version=7.1";

            try
            {
                Console.WriteLine($"Fetching pending approvals for project '{project}' in organization '{organization}'...");

                var response = await client.GetAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("API call successful. Processing response...");
                    var responseData = JsonSerializer.Deserialize<JsonElement>(responseBody);

                    if (responseData.TryGetProperty("count", out var countElement))
                    {
                        int count = countElement.GetInt32();
                        Console.WriteLine($"Found {count} approvals.");

                        if (count > 0 && responseData.TryGetProperty("value", out var approvalsArray))
                        {
                            // Dictionary to store the latest approval per pipeline
                            var latestApprovalsByPipeline = new Dictionary<string, JsonElement>();

                            foreach (var approval in approvalsArray.EnumerateArray())
                            {
                                Console.WriteLine(approval.ToString());
                                string pipelineName = approval.GetProperty("pipeline").GetProperty("name").GetString();
                                string createdOn = approval.GetProperty("createdOn").GetString(); 

                                // Keep the latest approval based on createdOn timestamp
                                if (!latestApprovalsByPipeline.TryGetValue(pipelineName, out var existingApproval) ||
                                    DateTimeOffset.Parse(createdOn) > DateTimeOffset.Parse(existingApproval.GetProperty("createdOn").GetString()))
                                {
                                    latestApprovalsByPipeline[pipelineName] = approval;
                                    result.Add(approval);
                                }
                            }

                            // Display results
                            Console.WriteLine("\nPipeline Name\t\tApproval ID\t\t\t\t\t\tStart Time\t\tApprover\tDetails URL");
                            Console.WriteLine("-----------------------------------------------------------------------------------------------------");

                            foreach (var kvp in latestApprovalsByPipeline)
                            {
                                var approval = kvp.Value;

                                string id = approval.GetProperty("id").GetString();
                                string pipelineName = kvp.Key;
                                string startTime = approval.GetProperty("createdOn").GetString();
                                string approver = approval.GetProperty("minRequiredApprovers").GetInt32().ToString();
                                string detailUrl = approval.GetProperty("_links").GetProperty("self").GetProperty("href").GetString();

                                Console.WriteLine($"{pipelineName,-20}\t{id,-36}\t{startTime,-20}\t{approver,-10}\t{detailUrl}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("No pending approvals found in the QA environment.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Unexpected response format: 'count' property not found.");
                        Console.WriteLine(responseBody);
                    }
                }
                else
                {
                    Console.WriteLine($"Error: {response.StatusCode} - {response.ReasonPhrase}");
                    Console.WriteLine("Response Body:");
                    Console.WriteLine(responseBody);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving approvals: {ex.Message}");
            }
            return result;
        }

        static async Task ListAndApprovePendingApprovals(string organization, string project, string token, string comment, string status)
        {
            // First, list all the latest pending approvals
            Console.WriteLine("Listing latest pending approvals...");

            // Call ListLatestPendingApprovals to list the pending approvals
            var approvals = await ListLatestPendingApprovals(organization, project, token);

            if (approvals.Count == 0)
            {
                Console.WriteLine("No pending approvals found.");
                return;
            }

            // After listing the approvals, approve them one by one
            foreach (var approval in approvals)
            {
                string approvalId = approval.GetProperty("id").GetString();
                string pipelineName = approval.GetProperty("pipeline").GetProperty("name").GetString();

                // Display information about the approval
                Console.WriteLine($"Approving approval for pipeline: {pipelineName} (ID: {approvalId})");

                // Call ApproveStep to approve the approval
                await ApproveStep(organization, project, approvalId, token, comment, status);
            }
        }

        static async Task DoTheThingAsync(HappyClass hc, int pipelineId, string[] stagesToSkip)
        {
            await hc.HappyFunctionAsync(pipelineId, stagesToSkip);
        }
    }
}