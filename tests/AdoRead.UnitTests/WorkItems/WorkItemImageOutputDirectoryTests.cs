using AdoRead.WorkItems;

namespace AdoRead.UnitTests.WorkItems;

public sealed class WorkItemImageOutputDirectoryTests
{
    [Fact]
    public void TryValidateExisting_InvalidPathReturnsFalseInsteadOfThrowing()
    {
        Assert.False(WorkItemImageOutputDirectory.TryValidateExisting("invalid\0path"));
    }
}
