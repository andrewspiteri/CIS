using Cis.Abstractions;

namespace Cis.Modules.Graph;

/// <summary>Shares existence, content and read failures only inside one read-only projection.</summary>
internal sealed record GraphInputInspection(bool Exists, string? Hash, string? Error)
{
    public static GraphInputInspection Read(string relativePath, string absolutePath)
        => CisReadScope.Read(typeof(GraphInputInspection), relativePath, absolutePath,
            () => ReadCore(relativePath, absolutePath));

    private static GraphInputInspection ReadCore(string relativePath, string absolutePath)
    {
        if (!File.Exists(absolutePath)) return new(false, null, null);
        try { return new(true, GraphBuilder.HashInput(relativePath, absolutePath), null); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { return new(true, null, exception.Message); }
    }
}
