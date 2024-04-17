using System.ComponentModel;
using DocumentFormat.OpenXml.Bibliography;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Planning.Handlebars;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Spectre.Console;

/*
    PlannerPlugin: can be used to create a plan for steps that are needed for a process or task
    Exposed functions:
    - CreateProcessPlan: Create or adjust the plan for the task based on provided details.
    - ExecuteProcessPlan: Execute the plan that was created earlier.
    - LoadPlanFromFile: Load a plan from a file.
    - SavePlanToFile: Save the plan to a file.
    - GetPlansList: Gets a list of plans that have been saved.
    - GenerateChartForPlan: Displays the plan in a flow chart (using Mermaid chart format and Mermaid.live)
*/

namespace SemanticKernelConsoleCopilotDemo
{
    public sealed class PlannerPlugin
    {
        private HandlebarsPlanner planner;
        private Kernel kernel;
        private HandlebarsPlan? plan;
        private DocuRAGPlugin docuPlugin;
        private bool consultCookbookForPlan;
        private bool autoExecutePlanAfterCreation;
        private bool enableChartGeneration;
        private string assistantLanguage;
        private KernelFunction mermaidConverterFunction;

        // ----------------- Plugin functions -----------------

        [KernelFunction, Description("Create or adjust an existing process plan for a given task. ")]
        public async Task<string> CreateProcessPlan(
            [Description("The task to perform, that can involve multiple steps. Describe the plan based on details provided earlier if relevant. If a plan changed is requested, extend the previous plan. ")] string task,
            [Description("Set if the user has requested an adjustment of an existing plan")] bool planChangeRequested = false)
        {
            var enhancedTask = $"{task}. Don't use the GetProcessGuidance, ExecuteProcessPlan or the CreateProcessPlan function in the Handlebars template.";

            var loadText = "Creating a plan...";
            if (consultCookbookForPlan && planChangeRequested == false)
            {
                var guidance = await docuPlugin.GetProcessGuidance(task);
                var guidanceDebugOutput = guidance.ToString().Truncate(250);
                AnsiConsole.MarkupLineInterpolated($"[dim grey30][dim grey30 underline]Guidance:[/] {guidanceDebugOutput}[/]");
                var noHallucinatedHelpers = "Don't use Handlebars helpers that does not exist, e.g. split, substring, indexOf, includes.";
                enhancedTask = $"{enhancedTask}. For coming up with a plan, here is some guidance: {System.Environment.NewLine + guidance} {noHallucinatedHelpers}";
            }

            if (planChangeRequested && plan != null)
            {
                enhancedTask = $"{enhancedTask}. The user has requested a change in the plan. The previous plan was: {System.Environment.NewLine + plan.ToString()}";
                loadText = "Adjusting the plan...";
            }      

            plan = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(loadText, async (context) =>
                {
                    return await planner.CreatePlanAsync(kernel, enhancedTask);
                });
            DisplayPlan();
            if (autoExecutePlanAfterCreation)
            {
                return await ExecutePlan();
            }
            else
            {
                return $"Plan was created. Please check and revise the plan as needed before executing it.";
            }

        }

        [KernelFunction, Description("Execute the plan that was created earlier.")]
        public async Task<string> ExecuteProcessPlan()
        {
            return await ExecutePlan();
        }

        [KernelFunction, Description("Load a plan from a file. ")]
        public async Task<string> LoadPlanFromFile(
            [Description("The file path to the plan file. ")] string filePath)
        {   
            //var filePath = "output/plan.hbp";

            var planJson = await System.IO.File.ReadAllTextAsync(filePath);
            plan = new HandlebarsPlan(planJson);
            DisplayPlan();
            return "Plan has been loaded from the file: " + filePath;
        }

        [KernelFunction, Description("Save the plan to a file. ")]
        public async Task<string> SavePlanToFile(
            [Description("The filename with a .hbp file extension. The filename should reflect the task for the plan in a few words with underscores. ")] string fileName)
        {   
            var dateYYYMMDDHHMM = System.DateTime.Now.ToString("yyyyMMddHHmm");
            var filePath = "output/" + dateYYYMMDDHHMM + '-' + fileName;
            if (plan == null)
            {
                return "No plan has been created yet. Please provide instructions for a plan first.";
            };

            var planString = plan.ToString();
            await System.IO.File.WriteAllTextAsync(filePath, planString);
            return "Plan has been saved to the file: " + filePath;
        }

        [KernelFunction, Description("Gets a list of plans that have been saved ")]
        public string[] GetPlansList() 
        {
            var folderPath = "output";
            var files = System.IO.Directory.GetFiles(folderPath, "*.hbp");
            return files;
        }        

        [KernelFunction, Description("Displays the plan in a flow chart")]
        public async Task<string> GenerateChartForPlan()
        {   
            if (!enableChartGeneration)
            {
                return "";
            }
            if (plan == null)
            {
                return "No plan has been created yet. Please provide instructions for a plan first.";
            };            
            var planString = plan.ToString();

            var funcResult = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Converting plan to Mermaid Chart...", async (context) =>
                {
                    return await kernel.InvokeAsync(mermaidConverterFunction, new() { ["planToConvert"] = planString });
                });

            var planInMermaidFormat = funcResult.GetValue<string>();

            var link = Utils.GenMermaidLiveLink(planInMermaidFormat??"");
            AnsiConsole.MarkupLineInterpolated($"   [bold][link={link}]Display Flowchart for Plan[/][/] {System.Environment.NewLine}");
            return "The chart has been generated. Click the above link to view the chart.";
        }          

        // ----------------- Helper methods & initialization -----------------

        private async Task<string> ExecutePlan()
        {
            if (plan == null)
            {
                return "No plan has been created yet. Please provide instructions for a plan first.";
            };

            var result = await plan.InvokeAsync(kernel);

            var resultDebugOutput = result.ToString().Truncate(250);

            AnsiConsole.MarkupLineInterpolated($"[dim]Plan result:  {System.Environment.NewLine + resultDebugOutput}[/]");
            AnsiConsole.WriteLine();
            return result.ToString();
        }


        private void DisplayPlan()
        {
            if (plan == null)
            {
                return;
            };
            AnsiConsole.WriteLine();
            var planPanel = new Panel(new Markup($"[dim]{Markup.Escape(plan.ToString())}[/]"))
                        .Border(BoxBorder.Rounded)
                        .BorderColor(Spectre.Console.Color.Grey50)
                        .Header("Plan", Justify.Center);
            planPanel.Expand = true;  
            AnsiConsole.Write(planPanel);
        }


        public PlannerPlugin(Kernel kernel, DocuRAGPlugin docuPlugin, 
            bool consultCookbookForPlan = false, 
            bool autoExecutePlanAfterCreation = false, 
            bool enableChartGeneration = true,
            string assistantLanguage = "English")
        {
            planner = InitPlanner();
            
            this.kernel = kernel;
            this.docuPlugin = docuPlugin;
            this.consultCookbookForPlan = consultCookbookForPlan;
            this.autoExecutePlanAfterCreation = autoExecutePlanAfterCreation;
            this.enableChartGeneration = enableChartGeneration;
            this.assistantLanguage = assistantLanguage;

            this.mermaidConverterFunction = CreateMermaidConverterFunction();


        }

        private KernelFunction CreateMermaidConverterFunction()
        {
            string converterPrompt = @"
                Convert the plan that uses the Handlebars template syntax to Mermaid flow chart format.
                Don't keep any specific details, e.g. names, email addresses, personal details in the Mermaid chart, generalize the plan.
                Use proper formatting and tabulators for the Mermaid chart. Should be a TD chart.
                You can represent iterations in the plan, e.g. the #each function in the Handlebars template, for example:
                flowchart TD
                    step1-->step2-->step3-->|Loop on step2| step2
                    step3-->step4    
                Conditions however should be represented as separate paths in the chart.
                The steps shouldn't just be like step1, step2, but should contain a short description of the action, e.g. 'Send email'.
                RETURN JUST THE MERMAID CHART STRING, NO OTHER TEXT OR EXPLANATION. DON'T USE quotes or code blocks in the returned Mermaid chart
                Don't use ```mermaid or ``` in the returned Mermaid chart, just the string.
                Again, just the string, no other text or explanation or no code blocks.
                Process plan to convert: 
                {{$planToConvert}}
            ";            
            return kernel.CreateFunctionFromPrompt(promptTemplate: converterPrompt, functionName: "PlanToMermaidConverter", executionSettings: new OpenAIPromptExecutionSettings() { Temperature = 0.0, TopP = 0.2 });
        }


        private static HandlebarsPlanner InitPlanner()
        {
            HandlebarsPlanner planner = new(new HandlebarsPlannerOptions()
            {
                ExecutionSettings = new OpenAIPromptExecutionSettings()
                {
                    Temperature = 0.0,
                    TopP = 0.1,
                },                  
                AllowLoops = true,
                //ExcludedFunctions =  new HashSet<string> { "GetProcessGuidance", "ExecuteProcessPlan", "CreateProcessPlan", "GenerateChartForPlan" },

            });
            return planner;
        }

    }
}