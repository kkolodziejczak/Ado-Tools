using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoWrite.Commands;

var configurationStore = new ConfigurationStore();
var jsonOutput = new JsonOutput(Console.Out);
var application = new ToolComposition(
    configurationStore,
    jsonOutput,
    TimeProvider.System,
    tokenScope: TokenScope.Write).CreateSlice(
    "ado-write-work-item",
    WorkItemCommentModule.Definition);

return await application.InvokeAsync(args, CancellationToken.None);
