using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadHarness.State;

public sealed class AtomicStateStore : ICadStateStore
{
    public const int MaximumBytes = 1048576;
    private readonly string path;
    private static readonly JsonSerializerOptions Options = CreateOptions();
    public AtomicStateStore(string path) => this.path = Path.GetFullPath(path);

    public void Commit(CadState state)
    {
        StateValidation.Validate(state);
        var data = JsonSerializer.SerializeToUtf8Bytes(state, Options);
        if (data.Length > MaximumBytes) throw new StateException("STATE_SCHEMA_INVALID", "State file exceeds the byte limit.");
        var directory = Path.GetDirectoryName(path)!;
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(data); stream.Flush(true); }
            // Same-directory native replacement makes readers see a whole old
            // or whole new file. Never delete the committed file first.
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            temporary = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new StateException("STATE_COMMIT_FAILED", "Atomic state commit failed; the prior committed file was not deleted.", error); }
        finally { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
    }

    public CadState Load()
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > MaximumBytes) throw new StateException("STATE_SCHEMA_INVALID", "State file exceeds the byte limit.");
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
            RejectDuplicates(document.RootElement);
            var state = document.RootElement.Deserialize<CadState>(Options)
                ?? throw new StateException("STATE_SCHEMA_INVALID", "State must be an object.");
            StateValidation.Validate(state);
            return state;
        }
        catch (JsonException error) { throw new StateException("STATE_SCHEMA_INVALID", "State JSON violates the strict v0.2 schema.", error); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new StateException("STATE_LOAD_FAILED", "Cannot read the committed state file.", error); }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32 };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return options;
    }
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new StateException("STATE_SCHEMA_INVALID", "Duplicate state JSON field: " + property.Name);
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
}
