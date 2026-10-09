using System.Security.Cryptography;
using System.Text;

namespace Cis.Abstractions;

/// <summary>Binds task dossier context omitted from repository execution identity.</summary>
public static class CisTaskAuthorityContext
{
    public static string Capture(CisRepositoryContext authority, string changeId)
    {
        if (string.IsNullOrWhiteSpace(changeId) || changeId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new InvalidDataException("Unsafe change identity for authority context.");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var total = 0;
        // The selected plan row and task document are bound separately by TaskContractDigest.
        foreach (var name in new[] { "proposal.md", "impact.md", "design.md", "decisions.md", "test-cases.md", "verification.md" })
        {
            var relative = $"{authority.DocumentationRoot}/changes/{changeId}/{name}";
            if (!CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, relative, out var path)
                || CisPathSafety.ContainsReparsePoint(authority.RepositoryPath, path))
                throw new InvalidDataException("Unsafe task authority context: " + relative);
            var content = "missing";
            if (File.Exists(path))
            {
                using var input = File.OpenRead(path);
                using var output = new MemoryStream();
                var buffer = new byte[8192];
                int count;
                while ((count = input.Read(buffer)) > 0)
                {
                    total += count;
                    if (output.Length + count > 256 * 1024 || total > 1024 * 1024)
                        throw new InvalidDataException("Task authority context exceeds its bounded size.");
                    output.Write(buffer, 0, count);
                }
                string text;
                try { text = new UTF8Encoding(false, true).GetString(output.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal); }
                catch (DecoderFallbackException error) { throw new InvalidDataException("Task authority context is not valid UTF-8: " + relative, error); }
                // This exact generated row is appended by task transition after verification.
                if (name == "verification.md") text = string.Join('\n', text.Split('\n').Where(line => !CisToolUsageSnapshot.IsVerificationRow(line)));
                content = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.TrimEnd())));
            }
            digest.AppendData(Encoding.UTF8.GetBytes(name + "\0" + content + "\n"));
        }
        return "sha256:" + Convert.ToHexStringLower(digest.GetHashAndReset());
    }

}
