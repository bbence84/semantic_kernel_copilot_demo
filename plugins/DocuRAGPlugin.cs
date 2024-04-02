using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.FileSystem.DevTools;
using Microsoft.KernelMemory.MemoryStorage.DevTools;
using Microsoft.Extensions.Logging;
using Spectre.Console;
/*
    DocuRAGPlugin: can be used to retrieve content for question answering and task guidance from documentation via RAG
    Exposed functions:
    - RetrieveRagContent: Retrieve content for question answering, e.g about an AI related topic. The question can be about about scientific or technology topics, e.g. AI or to get a summary of a topic for a synopsis.
    - GetProcessGuidance: Get guidance on how a certain process or task can be done.
*/
namespace SemanticKernelConsoleCopilotDemo
{
    public sealed class DocuRAGPlugin
    {
        private IKernelMemory? kernelMemory;

        private bool reimportDocuments = false;
        private string assistantLanguage = "English";
        private ConfigurationSettings configSettings = new ConfigurationSettings();

        // ----------------- Plugin functions -----------------

        [KernelFunction, Description("Retrieve content for question answering, e.g about an AI related topic. The question can be about about scientific or technology topics, e.g. AI or to get a summary of a topic for a synopsis.")]
        public async Task<string> RetrieveRagContent(
            [Description("The question about the topic. Rephrase the question which keeps the original meaning and can be used to retrieve content for question answering. If the question is not in English, translate it first.")] string question)
        {
            if (kernelMemory == null)
                throw new InvalidOperationException("Kernel memory is not initialized.");

            var askResult = await AnsiConsole.Status()
               .Spinner(Spinner.Known.Dots)
               .StartAsync("Getting info from documentation...", async (context) =>
               {
                   return (await kernelMemory.AskAsync(question, filter: new MemoryFilter().ByTag("topic", "documentation")).ConfigureAwait(continueOnCapturedContext: false)).Result;
               });

            return askResult;

        }

        [KernelFunction, Description("Get guidance on how a certain process or task can be done.")]
        public async Task<string> GetProcessGuidance(
            [Description("What is process or task on which the guidance is needed")] string question)
        {
            if (kernelMemory == null)
                throw new InvalidOperationException("Kernel memory is not initialized.");
                
            var askResult = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Getting plan guidance from cookbook...", async (context) =>
                {
                    return (await kernelMemory.AskAsync(question, filter: new MemoryFilter().ByTag("topic", "cookbook")).ConfigureAwait(continueOnCapturedContext: false)).Result;
                });

            return askResult;

        }
        
        // ----------------- Initialization -----------------

        public static Task<DocuRAGPlugin> Create(bool reimportDocuments = false, string assistantLanguage = "English")
        {
            var ret = new DocuRAGPlugin();
            ret.reimportDocuments = reimportDocuments;
            ret.assistantLanguage = assistantLanguage;
            return ret.InitializeAsync();
        }

        private async Task<DocuRAGPlugin> InitializeAsync()
        {
            await InitKernelMemoryForRAG();
            return this;
        }

        private AzureOpenAIConfig GetOpenAIConfig(bool isEmbedding = false)
        {
            return new AzureOpenAIConfig
            {
                APIKey = ConfigurationSettings.ApiKey,
                Deployment = isEmbedding ? ConfigurationSettings.EmbeddingDeploymentId : ConfigurationSettings.DeploymentId,
                Endpoint = ConfigurationSettings.Endpoint,
                APIType = AzureOpenAIConfig.APITypes.ChatCompletion,
                Auth = AzureOpenAIConfig.AuthTypes.APIKey
            };
        }

        private async Task InitKernelMemoryForRAG()
        {
            var kernelMemoryBuilder = new KernelMemoryBuilder()
                .WithAzureOpenAITextEmbeddingGeneration(GetOpenAIConfig(isEmbedding: true))
                .WithAzureOpenAITextGeneration(GetOpenAIConfig())
                .WithSimpleVectorDb(new SimpleVectorDbConfig { StorageType = FileSystemTypes.Disk, Directory = "vector_storage"});

            kernelMemoryBuilder.Services
                    .AddLogging(c => { c.AddConsole().SetMinimumLevel(LogLevel.Warning); });
 
            kernelMemory = kernelMemoryBuilder.Build();

            // Import documents to the kernel memory / vector store if does not exist yet or reimportDocuments is set to true
            if (reimportDocuments || IsDirectoryEmpty("vector_storage"))
            {
                await kernelMemory.ImportDocumentAsync(new Document().AddFile("rag_docs/rag_doc.txt").AddTag("topic", "documentation"));
                await kernelMemory.ImportDocumentAsync(new Document().AddFile("rag_docs/process_cookbook.txt").AddTag("topic", "cookbook"));
            }

        }

        public bool IsDirectoryEmpty(string path)
        {
            return !Directory.EnumerateFileSystemEntries(path).Any();
        }        

    }
}