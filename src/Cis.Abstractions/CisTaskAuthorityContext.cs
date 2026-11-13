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
        if (CisOutcomeAuthority.Read(authority, changeId) is { } authorization)
            return CaptureOutcomeGates(authority, changeId, authorization);
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

    private static string CaptureOutcomeGates(CisRepositoryContext authority, string changeId, CisOutcomeAuthorization authorization)
    {
        // Scope freshness is selected-task state. Adding an unrelated task, scope,
        // allocation distribution or generated review paragraph is not new human authority.
        var fields = new List<string> { authorization.Id, "visualUi=" + authorization.Request.VisualUi,
            "manualTestCatalogue=" + authorization.Request.ManualTestCatalogue };
        if (authorization.Request.VisualUi == true)
        {
            var relative = $"{authority.DocumentationRoot}/changes/{changeId}/design.md";
            if (!CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, relative, out var path)
                || CisPathSafety.ContainsReparsePoint(authority.RepositoryPath, path))
                throw new InvalidDataException("The applicable design gate must remain inside its unlinked change directory.");
            var content = "missing";
            if (File.Exists(path))
            {
                using var input = File.OpenRead(path);
                if (input.Length > 256 * 1024) throw new InvalidDataException("Applicable design authority exceeds its bounded size.");
                using var reader = new StreamReader(input, new UTF8Encoding(false, true));
                content = reader.ReadToEnd();
            }
            foreach (var key in new[] { "approval_status", "gate_status", "approved_by", "approved_content_hash",
                         "wireframe_sha256", "renderer_sha256", "png_manifest_sha256" })
                fields.Add(key + "=" + CisFrontMatter.Read(content, key));
            // Design's existing canonical approval binds its exact wireframe,
            // renderer and PNG digests in this table rather than front matter.
            var inDecision = false;
            foreach (var line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                if (line.Trim() == "## Approval decision") { inDecision = true; continue; }
                if (inDecision && line.StartsWith("## ", StringComparison.Ordinal)) break;
                if (!inDecision || !line.TrimStart().StartsWith('|')) continue;
                var cells = line.Split('|').Select(cell => cell.Trim()).ToArray();
                if (cells.All(cell => cell.Length == 0 || cell.All(character => character is '-' or ':'))) continue;
                fields.Add(string.Join('|', cells));
            }
        }
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', fields))));
    }

}
