using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;

var configurationStore = new ConfigurationStore();
var jsonOutput = new JsonOutput(Console.Out);
var application = new ToolComposition(configurationStore, jsonOutput, TimeProvider.System).CreateUmbrella(
    "ado-read",
    [PullRequestModule.Definition, WorkItemModule.Definition, WikiModule.Definition, SprintModule.Definition]);

return await application.InvokeAsync(args, CancellationToken.None);
