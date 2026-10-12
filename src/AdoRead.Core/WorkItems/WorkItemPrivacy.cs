using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Output;
using AdoRead.Privacy;
using AdoRead.Text;

namespace AdoRead.WorkItems;

/// <summary>
/// Withholds work-item text, comment bodies, custom fields and images, and aliases people. Identity
/// values are aliased on the source before mapping (<see cref="AliasIdentities"/>), so the core
/// assignee and author fields arrive already aliased. Comment authors are aliased here.
/// </summary>
public static class WorkItemPrivacy
{
    private static readonly RichTextDocument WithheldText = new(Raw: null, Format: null, PlainText: null);

    public static IReadOnlyList<PrivacyCategory> Categories { get; } =
        [PrivacyCategory.Text, PrivacyCategory.Comments, PrivacyCategory.People, PrivacyCategory.CustomFields, PrivacyCategory.Images];

    public static SourceWorkItem AliasIdentities(SourceWorkItem workItem, EffectivePrivacy privacy)
    {
        if (privacy.Allows(PrivacyCategory.People))
        {
            return workItem;
        }

        return workItem with
        {
            Fields = workItem.Fields.ToDictionary(
                pair => pair.Key,
                pair => AliasValue(pair.Value, privacy),
                StringComparer.Ordinal),
            Relations = workItem.Relations
                .Select(relation => relation with
                {
                    Attributes = relation.Attributes.ToDictionary(
                        pair => pair.Key,
                        pair => AliasValue(pair.Value, privacy),
                        StringComparer.Ordinal)
                })
                .ToArray()
        };
    }

    public static WorkItemDocument Apply(WorkItemDocument workItem, EffectivePrivacy privacy) => workItem with
    {
        Description = privacy.Allows(PrivacyCategory.Text) ? workItem.Description : WithheldText,
        AcceptanceCriteria = privacy.Allows(PrivacyCategory.Text) ? workItem.AcceptanceCriteria : WithheldText,
        CustomFields = privacy.Allows(PrivacyCategory.CustomFields)
            ? workItem.CustomFields
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal),
        Comments = workItem.Comments.Select(comment => Comment(comment, privacy)).ToArray()
    };

    private static object? AliasValue(object? value, EffectivePrivacy privacy) =>
        value is IdentityDocument identity ? privacy.Person(identity) : value;

    private static WorkItemCommentDocument Comment(WorkItemCommentDocument comment, EffectivePrivacy privacy) => comment with
    {
        CreatedByUniqueName = privacy.Person(comment.CreatedByUniqueName),
        ModifiedByUniqueName = privacy.Person(comment.ModifiedByUniqueName),
        RawText = privacy.Allows(PrivacyCategory.Comments) ? comment.RawText : null,
        RenderedHtml = privacy.Allows(PrivacyCategory.Comments) ? comment.RenderedHtml : null,
        PlainText = privacy.Allows(PrivacyCategory.Comments) ? comment.PlainText : null,
        Mentions = comment.Mentions.Select(mention => Mention(mention, privacy)).ToArray()
    };

    private static CommentMentionDocument Mention(CommentMentionDocument mention, EffectivePrivacy privacy) => mention with
    {
        ArtifactId = privacy.Person(mention.ArtifactId),
        TargetId = privacy.Person(mention.TargetId),
        Url = privacy.Allows(PrivacyCategory.People) ? mention.Url : null
    };
}
