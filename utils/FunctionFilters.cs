
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.SemanticKernel;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace SemanticKernelConsoleCopilotDemo
{
    public sealed class ProcessFunctionFilter : IFunctionFilter
    {

        public void OnFunctionInvoking(FunctionInvokingContext context)
        {   

            if (context.Function.Name == "PlanToMermaidConverter") {
                    // No need to display the result of this function
                    return;
            }

            AnsiConsole.WriteLine();
            var validParamsList = new List<string>();
            foreach (var arg in context.Arguments)
            {   
                foreach (var param in context.Function.Metadata.Parameters)
                {   
                    if (arg.Key.ToString() == param.Name)
                    {   
                        if (arg.Key.ToString() == "planToConvert") 
                        {
                            validParamsList.Add(string.Format("{0}={1}", arg.Key.ToString(), "<PLAN>"));
                        } else {
                            validParamsList.Add(string.Format("{0}={1}", arg.Key.ToString(), arg.Value?.ToString()));
                        }
                        
                    }
                }
            }
            var argumentsString =  string.Join(", ", validParamsList);

            var planPanel = new Panel(new Markup($"[dim]{context.Function.Name}({argumentsString.EscapeMarkup()})[/]"))
                        .Border(BoxBorder.Rounded)
                        .BorderColor(Spectre.Console.Color.Grey50)
                        .Header($"Function call");
            planPanel.Expand = true;                        
            AnsiConsole.Write(planPanel);

        }

        public void OnFunctionInvoked(FunctionInvokedContext context)
        {   

            if (context.Function.Name == "CreateProcessPlan" || context.Function.Name == "GenerateChartForPlan") {
                // No need to display the result of this function
                return;
            }

            if (context.Function.Name == "GetPlansList")
            {
                string[] plansJson = context.Result.GetValue<string[]>() ?? new string[0];
                if (plansJson == null)
                {
                    context.SetResultValue("No plans found!");
                    return;
                }
                string[]? plansList = plansJson.Select(o => (string?)o ?? "").ToArray() ?? new string[0];
                var PlanChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Please select the [green]saved plan[/]!")
                        .PageSize(20)
                        .AddChoices(plansList));
                context.SetResultValue($"The selected plan: {PlanChoice}.");

            } 
            else
            {   
                String panelText = "";
                if (context.Result?.ToString() == "System.String[]")
                {
                    panelText = string.Join(", ", context.Result?.GetValue<string[]>() ?? new string[0]);
                } else {
                    panelText = context.Result?.ToString() ?? "";
                }
                var resultPanel = new Panel(new Markup($"[dim grey30]{panelText.EscapeMarkup()}[/]"))
                        .Border(BoxBorder.Rounded)
                        .BorderColor(Spectre.Console.Color.Grey30)
                        .Header($"Function result - {context.Function.Name}");
                resultPanel.Expand = true;
                AnsiConsole.Write(resultPanel);
                AnsiConsole.WriteLine();
            }

        }

        public ProcessFunctionFilter()
        {
            
        }
    }
}