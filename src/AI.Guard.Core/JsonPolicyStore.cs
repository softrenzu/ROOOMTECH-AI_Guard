using System.Text.Json;

namespace Rooomtech.AIGuard.Core;

public static class JsonPolicyStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static GuardPolicy Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("AI Guard policy file was not found.", path);

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GuardPolicy>(json, Options)
               ?? throw new InvalidDataException("AI Guard policy file is empty or invalid.");
    }

    public static void Save(string path, GuardPolicy policy)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(policy, Options));
    }
}
