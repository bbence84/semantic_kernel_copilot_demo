using Microsoft.SemanticKernel;
using Spectre.Console;

public static class Utils
{
    public static string Truncate(this string value, int maxChars, bool addCutoffText = true)
    {
        return value.Length <= maxChars ? value : value.Substring(0, maxChars) + "..." + (addCutoffText ? "(cut off due to length)" : "");
    }

    public static string GenMermaidLiveLink(string graphMarkdown)
    {
        var jGraph = new
        {
            code = graphMarkdown,
            editorMode = "code",
            mermaid = new { theme = "dark" }
        };
        var jGraphString = Newtonsoft.Json.JsonConvert.SerializeObject(jGraph);
        var link = "http://mermaid.live/view#base64:" + System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(jGraphString));
        return link;
    }  

    public static void PrintFunctionsMetadata(Kernel kernel)
    {
        var functions = kernel.Plugins.GetFunctionsMetadata();

        var table = new Table();
        table.AddColumn(new TableColumn("Plugin").Centered());
        table.AddColumn(new TableColumn("Function Name"));
        table.AddColumn(new TableColumn("Description"));
        table.AddColumn(new TableColumn("Parameters"));
        foreach (KernelFunctionMetadata func in functions)
        {
            table.AddRow(func.PluginName ?? "", func.Name, func.Description.Truncate(70, false), string.Join(", ", func.Parameters.Select(p => p.Name)));
        }
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}