using System.CommandLine;
using System.CommandLine.Parsing;
using AdoRead.Privacy;

namespace AdoRead.Commands.Shared;

/// <summary>
/// The --privacy and --withhold options that every read command accepts. They can only tighten the
/// stored policy for one call, so an agent can restrict content but never widen it.
/// </summary>
internal sealed class PrivacyRequestOptions
{
    private readonly Option<string?> _level = new("--privacy")
    {
        Description = "Tighten this call to a level: minimal, standard or full. It can never loosen the stored policy."
    };

    private readonly Option<string[]> _withhold = new("--withhold")
    {
        Description = "Withhold one category for this call: text, comments, people, customFields or images. Repeatable.",
        AllowMultipleArgumentsPerToken = true
    };

    public void AddTo(Command command)
    {
        command.Options.Add(_level);
        command.Options.Add(_withhold);
    }

    public PrivacyRequest Read(ParseResult parseResult) =>
        new(parseResult.GetValue(_level), parseResult.GetValue(_withhold) ?? []);
}
