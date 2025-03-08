using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace AzureDevOpsPipelineRunner
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand("Run Azure DevOps pipelines programmatically");

            var orgOption = new Option<string>(
                "--org",
                "Azure DevOps organization name")
            { IsRequired = true };

            var projectOption = new Option<string>(
                "--project",
                "Azure DevOps project name")
            { IsRequired = true };

            var pipelineIdOption = new Option<int>(
                "--pipeline-id",
                "ID of the pipeline to run")
            { IsRequired = true };

            var branchOption = new Option<string>(
                "--branch",
                () => "main",
                "Git branch to run the pipeline on");

            var tokenOption = new Option<string>(
                "--token",
                "Azure DevOps personal access token")
            { IsRequired = true };

            var variablesOption = new Option<string>(
                "--variables",
                "JSON string of variables to pass to the pipeline");

            rootCommand.AddOption(orgOption);
            rootCommand.AddOption(projectOption);
            rootCommand.AddOption(pipelineIdOption);
            rootCommand.AddOption(branchOption);
            rootCommand.AddOption(tokenOption);
            rootCommand.AddOption(variablesOption);

            rootCommand.SetHandler(async (string organization, string project, int pipelineId, string branch, string token, string variablesJson) =>
            {
                await RunPipeline(organization, project, pipelineId, branch, token, variablesJson);
            }, orgOption, projectOption, pipelineIdOption, branchOption, tokenOption, variablesOption);

            return await rootCommand.InvokeAsync(args);
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
    }
}