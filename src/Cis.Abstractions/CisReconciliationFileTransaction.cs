using System.Text.Json;

namespace Cis.Abstractions;

/// <summary>Preserves recoverable originals and attempts every restoration after a bounded write failure.</summary>
public sealed class CisReconciliationFileTransaction(Action<string, byte[]>? replace = null)
{
    private readonly Action<string, byte[]> _replace = replace ?? ReplaceFile;

    public IReadOnlyList<string> Apply(string backupDirectory, IReadOnlyDictionary<string, byte[]> originals,
        IReadOnlyDictionary<string, byte[]> proposed, Func<bool> validate, string validationFailure = "Reconciliation did not validate against unchanged source inputs.",
        Func<bool>? inputsCurrent = null)
    {
        var attempted = new List<string>();
        var errors = new List<string>();
        try
        {
            Directory.CreateDirectory(backupDirectory);
            var inventory = proposed.Keys.Select((path, index) => new { path, backup = $"{index}.original" }).ToArray();
            foreach (var item in inventory) File.WriteAllBytes(Path.Combine(backupDirectory, item.backup), originals[item.path]);
            File.WriteAllText(Path.Combine(backupDirectory, "inventory.json"), JsonSerializer.Serialize(inventory));
            if (inputsCurrent?.Invoke() == false || originals.Any(item => !File.ReadAllBytes(item.Key).SequenceEqual(item.Value)))
                return ["Canonical inputs changed during reconciliation; nothing was applied."];
            foreach (var (path, bytes) in proposed)
            {
                attempted.Add(path);
                _replace(path, bytes);
            }
            if (validate()) return [];
            errors.Add(validationFailure);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            errors.Add("Reconciliation failed: " + exception.Message);
        }
        foreach (var path in attempted)
        {
            try
            {
                if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(originals[path])) _replace(path, originals[path]);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                errors.Add($"Restoration incomplete for {path}: {exception.Message}");
            }
        }
        errors.Add(errors.Any(error => error.StartsWith("Restoration incomplete", StringComparison.Ordinal))
            ? $"Recover original files from {backupDirectory}/inventory.json before continuing."
            : "All attempted original files were restored.");
        return errors;
    }

    private static void ReplaceFile(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
