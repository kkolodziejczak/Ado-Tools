namespace AdoRead.Output;

public interface IJsonOutput
{
    Task WriteAsync<T>(T value, bool compact, CancellationToken cancellationToken);
}
