namespace Cis.Abstractions;

public sealed record CisGitPathChange(string Status, string Path);

/// <summary>Projects NUL-delimited Git name-status records into an owned scope before reading file contents.</summary>
public static class CisGitChangeScope
{
    public static IEnumerable<CisGitPathChange> Read(string output, Func<string, bool> includes)
    {
        var fields = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 1 < fields.Length;)
        {
            var status = fields[index++];
            var source = fields[index++];
            if (status.StartsWith('R') || status.StartsWith('C'))
            {
                if (index >= fields.Length) yield break;
                var destination = fields[index++];
                var sourceOwned = includes(source);
                var destinationOwned = includes(destination);
                if (sourceOwned && destinationOwned) yield return new(status, destination);
                else if (destinationOwned) yield return new("A", destination);
                else if (sourceOwned && status.StartsWith('R')) yield return new("D", source);
            }
            else if (includes(source)) yield return new(status, source);
        }
    }
}
