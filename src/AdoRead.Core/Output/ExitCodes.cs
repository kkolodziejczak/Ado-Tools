namespace AdoRead.Output;

public static class ExitCodes
{
    public const int Success = 0;
    public const int InvalidInputOrConfiguration = 2;
    public const int NotFound = 3;
    public const int Partial = 4;
    public const int Error = 5;
    public const int Cancelled = 130;

    public static int FromStatus(string status) => status switch
    {
        "success" => Success,
        "notFound" => NotFound,
        "partial" => Partial,
        _ => Error
    };
}
