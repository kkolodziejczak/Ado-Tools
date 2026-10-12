using AdoRead.AzureDevOps;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;

namespace AdoRead.UnitTests.Commands;

internal static class TestTool
{
    public static CommandApplication Umbrella(
        IConfigurationStore configurationStore,
        IJsonOutput jsonOutput,
        TimeProvider timeProvider,
        IPatProvider? patProvider = null,
        IAdoSessionFactory? sessionFactory = null) =>
        new ToolComposition(configurationStore, jsonOutput, timeProvider, patProvider, sessionFactory).CreateUmbrella(
            "ado-read",
            [PullRequestModule.Definition, WorkItemModule.Definition, WikiModule.Definition, SprintModule.Definition]);
}
