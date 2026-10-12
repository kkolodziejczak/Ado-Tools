using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdoRead.Output;

public sealed class JsonOutput(TextWriter writer) : IJsonOutput
{
    public async Task WriteAsync<T>(T value, bool compact, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(value, CreateOptions(compact));
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private static JsonSerializerOptions CreateOptions(bool compact)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = !compact
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new UtcDateTimeOffsetConverter());
        return options;
    }

    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) => reader.GetDateTimeOffset();

        public override void Write(
            Utf8JsonWriter writer,
            DateTimeOffset value,
            JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
    }
}
