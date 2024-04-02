using System.ComponentModel;
using DocumentFormat.OpenXml.Bibliography;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Planning.Handlebars;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Spectre.Console;

/*
    PlannerPlugin: can be used to create a plan for steps that are needed for a process or task
    Exposed functions:
    - CreateProcessPlan: Create the plan for the task based on provided details.
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

        // ----------------- Plugin functions -----------------

        [KernelFunction, Description("Create or adjust an existing process plan for a given task. ")]
        public async Task<string> CreateProcessPlan(
            [Description("The task to perform, that can involve multiple steps. Describe the plan based on details provided earlier if relevant. If a plan changed is requested, extend the previous plan. ")] string task,
            [Description("Set if the user has requested an adjustment of an existing plan")] bool planChangeRequested = false)
        {
            var enhancedTask = $"{task}. Don't use the GetProcessGuidance, ExecuteProcessPlan or the CreateProcessPlan function in the Handlebars template.";

            if (consultCookbookForPlan && planChangeRequested == false)
            {
                var guidance = await docuPlugin.GetProcessGuidance(task);
                var guidanceDebugOutput = guidance.ToString().Truncate(250);
                AnsiConsole.MarkupLineInterpolated($"[dim grey30][dim grey30 underline]Guidance:[/] {guidanceDebugOutput}[/]");
                var noHallucinatedHelpers = "Don't use Handlebars helpers that does not exist, e.g. split, substring, indexOf, includes.";
                enhancedTask = $"{enhancedTask}. For coming up with a plan, here is some guidance: {System.Environment.NewLine + guidance} {noHallucinatedHelpers}";
            }

            plan = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Creating plan...", async (context) =>
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
                return $"Plan was created. Please check and revise the plan as needed before executing it.{System.Environment.NewLine} {plan.ToString()}";
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
        public string GenerateChartForPlan(
            [Description("The plan from the planner function CreateProcessPlan in Mermaid flow chart format. Keeps original formatting and tabulators. Should be a TD chart ")] string planInMermaidFormat)
        {   
            if (!enableChartGeneration)
            {
                return "";
            }
            var link = Utils.GenMermaidLiveLink(planInMermaidFormat);
            AnsiConsole.MarkupLineInterpolated($"[link={link}]Display Flowchart for Plan[/]");
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