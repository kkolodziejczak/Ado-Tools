using AdoRead.Output;

namespace AdoRead.UnitTests.Output;

public sealed class ExitCodesTests
{
    [Fact]
    public void Values_MatchDocumentedContractAndRemainDistinct()
    {
        int[] values =
        [
            ExitCodes.Success,
            ExitCodes.InvalidInputOrConfiguration,
            ExitCodes.NotFound,
            ExitCodes.Partial,
            ExitCodes.Error,
            ExitCodes.Cancelled
        ];

        Assert.Equal([0, 2, 3, 4, 5, 130], values);
        Assert.Equal(values.Length, values.Distinct().Count());
    }
}
