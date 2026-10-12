using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;

var configurationStore = new ConfigurationStore();
var jsonOutput = new JsonOutput(Console.Out);
var application = new CommandFactory(configurationStore, jsonOutput, TimeProvider.System).Create();

return await application.InvokeAsync(args, CancellationToken.None);
