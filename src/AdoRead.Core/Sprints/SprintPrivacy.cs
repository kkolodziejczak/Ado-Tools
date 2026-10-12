using AdoRead.Privacy;

namespace AdoRead.Sprints;

/// <summary>Aliases the assignee of each sprint work item. Sprint names and work-item titles stay visible.</summary>
public static class SprintPrivacy
{
    public static IReadOnlyList<PrivacyCategory> Categories { get; } = [PrivacyCategory.People];

    public static SprintDocument Apply(SprintDocument sprint, EffectivePrivacy privacy) => sprint with
    {
        WorkItems = sprint.WorkItems.Select(item => WorkItem(item, privacy)).ToArray()
    };

    private static SprintWorkItemDocument WorkItem(SprintWorkItemDocument item, EffectivePrivacy privacy) =>
        privacy.Allows(PrivacyCategory.People)
            ? item
            : item with { AssignedTo = privacy.Person(item.AssignedToUniqueName ?? item.AssignedTo) };
}
