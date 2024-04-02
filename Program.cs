﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

using Microsoft.SemanticKernel.Plugins.Core;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Plugins.Web.Bing;

using Spectre.Console;

/*
    🤖 Features 🤖
        [✔] 

    👷 Backlog 👷
        [ ] Support non-Azure deployments for chat completion & embeddings
        [ ] Support non-english ASSISTANT_LANGUAGE

    🙁 Issues / bugs 🙁
        [!] Handlebars planner hallucinating helpers, e.g. "includes" or "indexOf", temperature setting not possible https://github.com/microsoft/semantic-kernel/issues/4775, https://github.com/microsoft/semantic-kernel/issues/4731 
        [!] Limit the list of functions for the SK Planner to be able to use -- not possible in the Handlebars planner, as ExcludedPlugins is not available, https://github.com/microsoft/semantic-kernel/issues/3830

*/

namespace SemanticKernelConsoleCopilotDemo
{
    internal class ProcessPlanningChat {

        // Settings and experimential features
        static bool REIMPORT_RAG_DOCUMENTS = true; // Set to true to reimport the RAG documents from the rag_docs folder (to update the vector store with the latest documents)
        static bool CONSULT_COOKBOOK_FOR_PLAN = true; // Set to true to have the logic consult the cookbook before plan creation
        static bool ASK_USER_NAME_AND_LANGUAGE = false; // Set to true to ask the user for his/her name and language when starting the chat
        static bool PRINT_FUNCTIONS_METADATA_ON_START = false; // Set to true to print the list of functions that are available for user at the beginning of the chat
        static bool AUTO_EXECUTE_PLAN_AFTER_CREATION = false; // Set to true to automatically execute the plan after it's created
        static bool ENABLE_PLAN_CHART_GENERATION = true; // Set to true to enable the generation of the Mermaid chart for the plan
        static string ASSISTANT_LANGUAGE = "English"; // TODO: Set the language of the assistant, use this in relevant places

        static async Task Main(string[] args)
        {
            // Init the kernel for SK
            Kernel kernel = InitKernel();

            // Init the plugins:
            await InitPlugins(kernel);

            // Introduction info to be dispalyed in the console
            WriteIntroToConsole();

            // Finally, run the chat
            await RunChat(kernel);

        }


        private static async Task InitPlugins(Kernel kernel)
        {
            // Plugin: Process actions, e.g. search internet, send an email, etc.
            kernel.Plugins.AddFromType<SemanticKernelConsoleCopilotDemo.CustomActionsPlugin>();
            
            // Plugin: for RAG
            var RAGPlugin = await DocuRAGPlugin.Create(reimportDocuments: REIMPORT_RAG_DOCUMENTS, assistantLanguage: ASSISTANT_LANGUAGE);
            kernel.Plugins.AddFromObject(RAGPlugin);

            // Plugin: Planner: can create a plan on how to perform a process and can execute the plan
            //  Optionally, it can "consult" the  cookbook via the RAG plugin before coming up with a plan
            //  It can also generate a Mermaid chart for the plan
            var Planner = new PlannerPlugin(kernel, RAGPlugin, 
                consultCookbookForPlan: CONSULT_COOKBOOK_FOR_PLAN,
                autoExecutePlanAfterCreation: AUTO_EXECUTE_PLAN_AFTER_CREATION,
                enableChartGeneration: ENABLE_PLAN_CHART_GENERATION,
                assistantLanguage: ASSISTANT_LANGUAGE);
            kernel.Plugins.AddFromObject(Planner);

            // Plugin: Utility plugin: can e.g. be used to get the list of functions that are available for use
            var utilityPlugin = new SemanticKernelConsoleCopilotDemo.UtilityPlugin(kernel);
            kernel.Plugins.AddFromObject(utilityPlugin); 

            // Plugin: File input / output plugin
            kernel.Plugins.AddFromType<FileIOPlugin>();

            // Plugin: Web search engine plugin
            var bingConnector = new BingConnector(ConfigurationSettings.BingAiSearchApiKey);
            var bing = new WebSearchEnginePlugin(bingConnector);
            kernel.ImportPluginFromObject(bing, "bing");          

        }

        private static OpenAIPromptExecutionSettings ChatModelPromptExecutionSettings()
        {

            String chartUseInstruction = "";
            if (ENABLE_PLAN_CHART_GENERATION)
            {
                chartUseInstruction = """
                    When asked to create chart using GenerateChartForPlan, pass only the Mermaid code, without any additional text. 
                    Keep original formatting and tabulators. 
                    Use the output plan of CreateProcessPlan, use all the steps from the plan, but don't add any new steps.
                    The chart should not contain any specific details, e.g. dates, names, email addresses, etc.
                    """;
            }

            var ProcessExpertSystemPrompt = $$"""   
                You are a personal assistant who can help organizing events and arranging certain tasks.
                Suggest a plan to solve the task. Revise the plan based on feedback from user.

                Generic info about processes can be asked retrieved using a function call to RetrieveRagContent.

                If function paramteters for an event are not specified, e.g the date, particpants list, topic for the conference, is not provided, 
                ask the user for the parameters before creating the plan.
                Don't assume parameters of functions if not provided earlier.

                If the question is about general topics, use the Kernel Memory RetrieveRagContent function to retrieve the anwsers.

                If the context already contains the anwser, DON'T use Kernel Memory RetrieveRagContent or GetProcessGuidance functions to retrieve the anwsers.

                If end use would like automate a process, e.g. organizing a conference, use CreateProcessPlan, but only if the date, participant list and topic is known. 
                For e.g. organizing a conference, don't use any functions directly first, but use the CreateProcessPlan for coming up with a plan.
                
                After creating or adjusting the plan with CreateProcessPlan, don't trigger the plan execution. Let the user decide if the plan is good or not. If she / he says the plan is good, then execute the plan.
                
                {{ chartUseInstruction }}

                In case the user asks to load plans, get the file name first using function GetPlansList. Don't ask the filename, but call the function GetPlansList
                If the filename is known, you can load the plan via LoadPlanFromFile

                Don't output too many newlines in the response, only if it's necessary.
            """;
            return new()
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                Temperature = 0.0,
                ChatSystemPrompt = ProcessExpertSystemPrompt,
                MaxTokens = 1000,
            };
        }

        private static async Task RunChat(Kernel kernel)
        {
            
            IChatCompletionService chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
            OpenAIPromptExecutionSettings openAIPromptExecutionSettings = ChatModelPromptExecutionSettings();
            
            ChatHistory Messages = [];

            var name = "User";
            // Experimental: Get user name and language
            if (ASK_USER_NAME_AND_LANGUAGE == true) {
                name = AnsiConsole.Ask<string>("What's your [green]name[/]?", defaultValue: "User");
                var userLanguage = AnsiConsole.Ask<string>("Specify the [green]language[/]!", defaultValue: "English");
                Messages.AddUserMessage($"My name is { name }! I speak { userLanguage }! Please speak in { userLanguage }!");
                AnsiConsole.WriteLine();
            }

            // Experimental: Print the functions metadata (what are the plugins and functions available for the user to use)
            if (PRINT_FUNCTIONS_METADATA_ON_START == true) {
                Utils.PrintFunctionsMetadata(kernel);
            }

            // Start the chat
            while (true)
            {
                // Get user input
                var UserMessage = AnsiConsole.Ask<string>($"[bold darkgreen]{ name }[/] [bold darkgreen]>[/] ");
                Messages.AddUserMessage(UserMessage);

                // Get the response from the LLM base model, in streaming mode
                var result = chatCompletionService.GetStreamingChatMessageContentsAsync(Messages,
                    executionSettings: openAIPromptExecutionSettings,
                    kernel: kernel);

                string fullMessage = "";
                var first = true;
                
                // Emit the response from the LLM response stream
                await foreach (var content in result)
                {
                    if (content.Role.HasValue && first)
                    {
                        AnsiConsole.WriteLine();
                        AnsiConsole.Markup($"[bold green italic]AI Assistant[/] [bold green]>[/] ");
                        first = false;
                    }
                    AnsiConsole.MarkupInterpolated($"[italic]{content.Content ?? ""}[/]");
                    fullMessage += content.Content;
                }
                AnsiConsole.WriteLine();
                AnsiConsole.WriteLine();

                // Add the message from the agent to the chat history
                Messages.AddAssistantMessage(fullMessage);
            }
        }

        private static void WriteIntroToConsole()
        {
            AnsiConsole.Write(
                new FigletText("Personal Assistant Copilot Demo")
                    .LeftJustified()
                    .Color(Color.Green));
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[bold yellow]What is this?[/]").RuleStyle("red"));
            AnsiConsole.WriteLine();

            var toolDescription = """
            ✨ This is a POC for using using the [bold yellow]Microsoft Semantic Kernel SDK[/] to implement an LLM based AI [bold yellow]personal assistant[/], which can: ✨
            [bold yellow]1.[/] Ask questions from the assistant about [bold yellow]certain topics[/] (content made avaiable in a vector store, and retrieved via RAG)
            [bold yellow]2.[/] Have the assistant come up with a plan for a [bold yellow]task[/] (e.g. organize a conference)
            [bold yellow]3.[/] The assistant can also perform actual actions to realize the [bold yellow]task[/] (e.g. send an email, search the internet, etc.)
            """;
            AnsiConsole.MarkupLine(toolDescription);
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[bold yellow]Chat[/]").RuleStyle("green"));
            AnsiConsole.WriteLine();

        }

        private static Kernel InitKernel( )
        {
            var builder = Kernel.CreateBuilder();

            // Init the Kernel with Azure OpenAI chat completion
            builder.AddAzureOpenAIChatCompletion(
                     endpoint: ConfigurationSettings.Endpoint,
                     deploymentName: ConfigurationSettings.DeploymentId,
                     apiKey: ConfigurationSettings.ApiKey);
            builder.Services.AddLogging(c => c.AddConsole().SetMinimumLevel(ConfigurationSettings.LogLevelValue));

            // Add SK filters so that we can log the function calls and provide function specific logic, e.g. to display a choice list
            builder.Services.AddSingleton<IFunctionFilter, SemanticKernelConsoleCopilotDemo.ProcessFunctionFilter>();
            var kernel = builder.Build();
            return kernel;

        }        

    }
}