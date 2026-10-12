namespace AdoRead.Output;

public sealed record IdentityDocument(
    string? DisplayName,
    string? Id,
    string? UniqueName,
    string? Descriptor,
    Uri? Url,
    Uri? ImageUrl);
