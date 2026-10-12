using AdoRead.AzureDevOps;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Output;
using AdoRead.UnitTests.Privacy;

namespace AdoRead.UnitTests.Commands;

internal static class TestTool
{
    public static CommandApplication Umbrella(
        IConfigurationStore configurationStore,
        IJsonOutput jsonOutput,
        TimeProvider timeProvider,
        IPatProvider? patProvider = null,
        IAdoSessionFactory? sessionFactory = null,
        ICredentialVault? credentialVault = null) =>
        new ToolComposition(configurationStore, jsonOutput, timeProvider, patProvider, sessionFactory, credentialVault ?? TestPrivacy.ConsentedVault()).CreateUmbrella(
            "ado-read",
            [PullRequestModule.Definition, WorkItemModule.Definition, WikiModule.Definition, SprintModule.Definition, PipelineModule.Definition]);
}
