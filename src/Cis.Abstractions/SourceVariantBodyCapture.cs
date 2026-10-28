using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace Cis.Abstractions;

/// <summary>Captures the actual subject body from inside a native test; it does not assert that a test passed.</summary>
public static class SourceVariantBodyCapture
{
    public const string Prefix = "CIS_SOURCE_VARIANT_BODY_V1:";
    private static readonly Guid Sha256Document = new("8829d00f-11b8-4213-878b-770e8597ac16");

    /// <summary>Write the returned line through the selected native test's output channel.</summary>
    public static string Encode(Assembly subject, string sourceDocument, Assembly nativeTest, string testSourceDocument)
        => Prefix + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new SourceVariantNativeObservation(1,
            Observe(subject, sourceDocument), Observe(nativeTest, testSourceDocument))));

    /// <summary>Observes a loaded assembly and validates its on-disk PE and external portable PDB identity.</summary>
    public static SourceVariantBodyObservation Observe(Assembly subject, string sourceDocument)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var observed = Inspect(subject.Location, sourceDocument);
        if (subject.ManifestModule.ModuleVersionId.ToString("D") != observed.ModuleVersionId)
        { throw new InvalidDataException("Loaded module differs from its physical subject body."); }
        return observed;
    }

    /// <summary>Independently rechecks retained PE/PDB bytes without loading or executing the subject assembly.</summary>
    public static SourceVariantBodyObservation Inspect(string assemblyPath, string sourceDocument)
    {
        if (string.IsNullOrWhiteSpace(sourceDocument) || Path.IsPathFullyQualified(sourceDocument)
            || sourceDocument.Contains('\\') || sourceDocument.Split('/').Any(part => part.Length == 0 || part is "." or ".."))
        { throw new InvalidDataException("Use one safe relative source document."); }
        string assembly = Path.GetFullPath(assemblyPath), pdb = Path.ChangeExtension(assembly, ".pdb");
        foreach (string path in new[] { assembly, pdb })
        {
            if (!File.Exists(path) || new FileInfo(path).Length is <= 0 or > 134217728
                || CisPathSafety.ContainsReparsePoint(Path.GetPathRoot(path)!, path))
            { throw new InvalidDataException("Missing, linked or excessive subject body/PDB."); }
        }
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) { throw new InvalidDataException("Subject is not a managed body."); }
        MetadataReader metadata = pe.GetMetadataReader();
        var codeViews = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView).ToArray();
        if (codeViews.Length != 1) { throw new InvalidDataException("One external portable PDB identity is required."); }
        var codeView = pe.ReadCodeViewDebugDirectoryData(codeViews[0]);
        using var pdbStream = File.OpenRead(pdb);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
        MetadataReader symbols = provider.GetMetadataReader();
        var header = symbols.DebugMetadataHeader ?? throw new InvalidDataException("Missing portable PDB identity.");
        byte[] id = header.Id.ToArray();
        if (id.Length != 20 || codeView.Age != 1 || codeView.Guid != new Guid(id.AsSpan(0, 16))
            || codeViews[0].Stamp != BinaryPrimitives.ReadUInt32LittleEndian(id.AsSpan(16, 4)))
        { throw new InvalidDataException("Portable PDB does not match its subject PE debug identity."); }
        var checksums = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.PdbChecksum).ToArray();
        if (checksums.Length != 1) { throw new InvalidDataException("One PE-bound PDB checksum is required."); }
        var checksum = pe.ReadPdbChecksumDebugDirectoryData(checksums[0]);
        byte[] pdbBytes = File.ReadAllBytes(pdb);
        if (header.IdStartOffset < 0 || header.IdStartOffset > pdbBytes.Length - 20 || checksum.AlgorithmName != "SHA256")
        { throw new InvalidDataException("Unsupported or invalid PE-bound portable PDB checksum."); }
        // Portable PDB checksums hash the complete image with only its 20-byte
        // ID zeroed. The ordinary artifact hash below still hashes every byte.
        using var checksumHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        checksumHash.AppendData(pdbBytes.AsSpan(0, header.IdStartOffset));
        checksumHash.AppendData(new byte[20]);
        checksumHash.AppendData(pdbBytes.AsSpan(header.IdStartOffset + 20));
        if (!checksum.Checksum.AsSpan().SequenceEqual(checksumHash.GetHashAndReset()))
        { throw new InvalidDataException("Portable PDB checksum differs from the compiled subject PE."); }
        var documents = symbols.Documents.Select(handle => symbols.GetDocument(handle)).Where(document =>
        {
            string name = symbols.GetString(document.Name).Replace('\\', '/');
            return name == sourceDocument || name.EndsWith("/" + sourceDocument, StringComparison.Ordinal);
        }).ToArray();
        if (documents.Length != 1 || symbols.GetGuid(documents[0].HashAlgorithm) != Sha256Document)
        { throw new InvalidDataException("One SHA256 source document is required in the subject PDB."); }
        return new(1, assembly, Hash(assembly), metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString("D"),
            pdb, Hash(pdb), Convert.ToHexStringLower(id), symbols.GetString(documents[0].Name),
            Convert.ToHexStringLower(symbols.GetBlobBytes(documents[0].Hash)), Hash(typeof(SourceVariantBodyCapture).Assembly.Location));
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
