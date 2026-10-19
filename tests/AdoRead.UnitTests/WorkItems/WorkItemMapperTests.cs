using System.Text.Json;
using AdoRead.AzureDevOps;
using AdoRead.Output;
using AdoRead.Text;
using AdoRead.WorkItems;
using Newtonsoft.Json.Linq;

namespace AdoRead.UnitTests.WorkItems;

public sealed class WorkItemMapperTests
{
    private readonly WorkItemMapper _mapper = new(new RichTextNormalizer());
    private readonly Uri _organizationUrl = new("https://dev.azure.com/example/");

    [Fact]
    public void Map_NormalizesCoreFieldsIdentitiesRelationsCommentsAndCustomFields()
    {
        var source = new SourceWorkItem(
            123,
            4,
            new Uri("https://dev.azure.com/example/_apis/wit/workItems/123"),
            new Dictionary<string, object?>
            {
                ["System.WorkItemType"] = "User Story",
                ["System.Title"] = "Capture payment",
                ["System.State"] = "Active",
                ["System.Reason"] = "Implementation started",
                ["System.TeamProject"] = "Payments",
                ["System.AreaPath"] = "Payments\\Checkout",
                ["System.IterationPath"] = "Payments\\Sprint 12",
                ["System.Description"] = "<p>Take <b>payment</b>.</p>",
                ["Microsoft.VSTS.Common.AcceptanceCriteria"] = "<ul><li>Approved</li><li>Declined</li></ul>",
                ["System.AssignedTo"] = new IdentityDocument("Ada Lovelace", "1", "ada@example.test", "aad.1", null, null),
                ["System.CreatedBy"] = "Grace Hopper <grace@example.test>",
                ["System.ChangedBy"] = new Dictionary<string, object?> { ["displayName"] = "Katherine Johnson", ["id"] = "3" },
                ["System.CreatedDate"] = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc),
                ["System.ChangedDate"] = "2026-07-02T09:30:00Z",
                ["System.Tags"] = "Backend; api;Backend ; ",
                ["Microsoft.VSTS.Common.Priority"] = 2,
                ["Microsoft.VSTS.Common.Severity"] = "2 - High",
                ["Custom.Risk"] = JObject.Parse("""{"level":"high","score":8,"signals":["new"]}""")
            },
            [
                new SourceWorkItemRelation(
                    "System.LinkTypes.Hierarchy-Reverse",
                    new Uri("https://dev.azure.com/example/_apis/wit/workItems/100"),
                    new Dictionary<string, object?> { ["name"] = "Parent" }),
                new SourceWorkItemRelation(
                    "Custom.Unknown",
                    new Uri("https://example.test/resource/42"),
                    new Dictionary<string, object?>())
            ]);
        WorkItemCommentDocument[] comments =
        [
            new(
                7,
                2,
                "reviewer@example.test",
                null,
                new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 3, 11, 0, 0, TimeSpan.Zero),
                true,
                "markdown",
                "Please **clarify** [the rule](https://example.invalid/rule).",
                "<p>Please <strong>clarify</strong> <a href=\"https://example.invalid/rule\">the rule</a>.</p>",
                PlainText: null,
                [new CommentMentionDocument("123", "workItem", "123", new Uri("https://dev.azure.com/example/_apis/wit/workItems/123"))],
                [new CommentReactionDocument("like", 2, true)],
                new Uri("https://dev.azure.com/example/_apis/wit/workItems/123/comments/7"))
        ];

        WorkItemDocument result = _mapper.Map(source, comments, _organizationUrl);

        Assert.Equal(123, result.Id);
        Assert.Equal("Capture payment", result.Title);
        Assert.Equal("Take payment.", result.Description.PlainText);
        Assert.Equal("Approved\nDeclined", result.AcceptanceCriteria.PlainText);
        Assert.Equal("ada@example.test", result.AssignedToUniqueName);
        Assert.Null(result.CreatedByUniqueName);
        Assert.Null(result.ChangedByUniqueName);
        Assert.Equal("reviewer@example.test", result.Comments[0].CreatedByUniqueName);
        Assert.Null(result.Comments[0].ModifiedByUniqueName);
        Assert.Equal(["api", "Backend"], result.Tags);
        Assert.Equal("parent", result.Relations[0].Category);
        Assert.Equal(100, result.Relations[0].TargetId);
        Assert.Equal("unknown", result.Relations[1].Category);
        Assert.True(result.Comments[0].IsDeleted);
        Assert.Equal("Please clarify the rule.", result.Comments[0].PlainText);
        Assert.Equal(2, result.Comments[0].Reactions[0].Count);
        Assert.Equal("high", result.CustomFields["Custom.Risk"].GetProperty("level").GetString());
        Assert.Equal(8, result.CustomFields["Custom.Risk"].GetProperty("score").GetInt32());
        Assert.Equal("new", result.CustomFields["Custom.Risk"].GetProperty("signals")[0].GetString());
        Assert.Equal(new Uri("https://dev.azure.com/example/Payments/_workitems/edit/123"), result.Links.Browser);
    }

    [Fact]
    public void Map_MissingOptionalFieldsProducesStableNullsAndEmptyCollections()
    {
        var source = new SourceWorkItem(
            5,
            1,
            RestUrl: null,
            new Dictionary<string, object?> { ["System.TeamProject"] = "Project" },
            Relations: []);

        WorkItemDocument result = _mapper.Map(source, [], _organizationUrl);

        Assert.Null(result.Title);
        Assert.Null(result.AssignedToUniqueName);
        Assert.Null(result.CreatedAtUtc);
        Assert.Empty(result.Tags);
        Assert.Empty(result.Relations);
        Assert.Empty(result.Comments);
        Assert.Empty(result.CustomFields);
        Assert.Null(result.Links.Rest);
    }

    [Fact]
    public void Map_NormalizesRichTextCustomFieldsLikeTheDescription()
    {
        var source = new SourceWorkItem(
            9,
            1,
            null,
            new Dictionary<string, object?>
            {
                ["System.TeamProject"] = "Project",
                ["Custom.Notes"] = "<div>First&nbsp;line<br>Second <b>line</b></div>",
                ["Custom.Plain"] = "<kept as typed>"
            },
            [])
        {
            RichTextFields = new HashSet<string>(StringComparer.Ordinal) { "Custom.Notes" }
        };

        WorkItemDocument result = _mapper.Map(source, [], _organizationUrl);

        JsonElement notes = result.CustomFields["Custom.Notes"];
        Assert.Equal("html", notes.GetProperty("format").GetString());
        Assert.Equal("First line\nSecond line", notes.GetProperty("plainText").GetString());
        Assert.Equal("<div>First&nbsp;line<br>Second <b>line</b></div>", notes.GetProperty("raw").GetString());
        Assert.Equal("<kept as typed>", result.CustomFields["Custom.Plain"].GetString());
    }

    [Fact]
    public void Map_UnsupportedCustomValueFallsBackToStableStringInsteadOfSdkShape()
    {
        var source = new SourceWorkItem(
            8,
            1,
            null,
            new Dictionary<string, object?>
            {
                ["System.TeamProject"] = "Project",
                ["Custom.Value"] = new UnsupportedValue("safe")
            },
            []);

        WorkItemDocument result = _mapper.Map(source, [], _organizationUrl);

        JsonElement value = result.CustomFields["Custom.Value"];
        Assert.Equal(JsonValueKind.Object, value.ValueKind);
        Assert.Equal(typeof(UnsupportedValue).FullName, value.GetProperty("sourceType").GetString());
        Assert.Equal("safe", value.GetProperty("value").GetString());
    }

    private sealed record UnsupportedValue(string Value)
    {
        public override string ToString() => Value;
    }
}
