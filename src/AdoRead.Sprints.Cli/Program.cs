using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;

var configurationStore = new ConfigurationStore();
var jsonOutput = new JsonOutput(Console.Out);
var application = new ToolComposition(configurationStore, jsonOutput, TimeProvider.System).CreateSlice(
    "ado-read-sprints",
    SprintModule.Definition);

return await application.InvokeAsync(args, CancellationToken.None);
