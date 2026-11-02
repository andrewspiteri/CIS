using System.Text;

namespace Cis.Abstractions.Tests;

public sealed class CisDocumentTextTests
{
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32")]
    [InlineData("utf32be")]
    public void ReadsCompleteTextInSupportedEncodings(string name)
    {
        Encoding encoding = name switch
        {
            "utf16le" => new UnicodeEncoding(false, true, true),
            "utf16be" => new UnicodeEncoding(true, true, true),
            "utf32" => new UTF32Encoding(false, true, true),
            "utf32be" => new UTF32Encoding(true, true, true),
            _ => new UTF8Encoding(true, true)
        };
        WithSource([.. encoding.GetPreamble(), .. encoding.GetBytes("# Intent\nRetain 30 seconds — except during shutdown.\n")], path =>
            Assert.Equal("# Intent\nRetain 30 seconds — except during shutdown.\n", CisDocumentText.Read(path)));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("nul")]
    [InlineData("oversized")]
    public void ReportsUnreadableSourceWithoutTruncatingOrChangingIt(string kind)
    {
        byte[] bytes = kind switch { "invalid" => [0xff, 0xfe, 0xff], "nul" => [65, 0, 66], _ => new byte[101] };
        WithSource(bytes, path =>
        {
            var error = Assert.Throws<InvalidDataException>(() => CisDocumentText.Read(path, 100));
            Assert.Contains("Import problem", error.Message);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        });
    }

    private static void WithSource(byte[] bytes, Action<string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "cis-source-" + Guid.NewGuid().ToString("N") + ".md");
        try { File.WriteAllBytes(path, bytes); check(path); }
        finally { File.Delete(path); }
    }
}
