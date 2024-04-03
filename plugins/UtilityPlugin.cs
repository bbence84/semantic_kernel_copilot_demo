using Microsoft.SemanticKernel;
using System.ComponentModel;

/*
    UtilityPlugin: A plugin that provides utility functions for the user.
*/

namespace SemanticKernelConsoleCopilotDemo
{
    public sealed class UtilityPlugin
    {
        private Kernel kernel;

        public UtilityPlugin(Kernel kernel) {
            this.kernel = kernel;
        }

        [KernelFunction, Description("Returns the list of functions / plugins that are available for use, can show what the assistant can do.")]
        public string GetAvailableFunctions()
        {
            Utils.PrintFunctionsMetadata(kernel);
            return "The above are the list of functions that are available for use.";
        }

    }
}