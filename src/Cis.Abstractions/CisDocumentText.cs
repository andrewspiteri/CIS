using System.Text;

namespace Cis.Abstractions;

/// <summary>Reads a complete bounded text source, reporting extraction failures explicitly.</summary>
public static class CisDocumentText
{
    public static string Read(string path, int maximumBytes = 20_971_520)
    {
        if (new FileInfo(path).Length > maximumBytes)
            throw new InvalidDataException($"Import problem: {Path.GetFileName(path)} exceeds the supported {maximumBytes} byte source limit. Its facts have not been assessed.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > maximumBytes)
            throw new InvalidDataException($"Import problem: {Path.GetFileName(path)} grew beyond the supported source limit while being read.");
        Encoding encoding = new UTF8Encoding(false, true);
        var offset = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) { encoding = new UTF32Encoding(false, false, true); offset = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, false, true); offset = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); offset = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); offset = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) offset = 3;
        try
        {
            var text = encoding.GetString(bytes, offset, bytes.Length - offset);
            if (text.Contains('\0')) throw new InvalidDataException($"Import problem: {Path.GetFileName(path)} contains unsupported binary content. Its facts have not been assessed.");
            return text;
        }
        catch (DecoderFallbackException)
        { throw new InvalidDataException($"Import problem: {Path.GetFileName(path)} contains unreadable text. Its facts have not been assessed."); }
    }
}
