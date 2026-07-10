using System.Text.Json;
using System.Text.Json.Serialization;
using Project27.Core.Persistence;

namespace Project27.Storage;

/// <summary>
/// The one wire format for <see cref="ProjectDocument"/> JSON, shared by `.p27`
/// files, the server's snapshot storage, and the CLI's remote mode.
/// </summary>
public static class ProjectDocumentSerializer
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, Options);
    }

    public static ProjectDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            return JsonSerializer.Deserialize<ProjectDocument>(json, Options)
                ?? throw new InvalidDataException("The project snapshot is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The project snapshot is not valid JSON: {exception.Message}", exception);
        }
    }
}
