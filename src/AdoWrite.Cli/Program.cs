using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;
using AdoWrite.Commands;

var configurationStore = new ConfigurationStore();
var jsonOutput = new JsonOutput(Console.Out);
var application = new ToolComposition(
    configurationStore,
    jsonOutput,
    TimeProvider.System,
    toolKind: ToolKind.Write).CreateUmbrella(
    "ado-write",
    [PullRequestWriteModule.Definition, WorkItemCommentModule.Definition]);

return await application.InvokeAsync(args, CancellationToken.None);
