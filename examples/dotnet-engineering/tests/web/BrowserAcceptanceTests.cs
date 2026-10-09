using Microsoft.Playwright;

namespace Example.WebTests;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1506", Justification = "This one end-to-end journey composes native browser options, locators, assertions and retained evidence. Keep the full journey readable together; production coupling limits still apply.")]
public sealed class BrowserAcceptanceTests(WebFixture server, ITestOutputHelper output) : IClassFixture<WebFixture>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Layer", "browser")]
    public async Task AccessibleJourneyPersistsOrExplainsDeniedWrite(bool canWrite)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            BaseURL = server.Address.ToString(),
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + server.Token(canWrite) },
        });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        var page = await context.NewPageAsync();
        var evidence = Path.Combine(server.EvidenceDirectory, canWrite ? "allowed" : "denied");
        Directory.CreateDirectory(evidence);
        try
        {
            await page.GotoAsync("/");
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Reading replay example" })).ToBeVisibleAsync();
            await page.GetByLabel("Quantity", new() { Exact = true }).FillAsync("0.00000001");
            await page.GetByRole(AriaRole.Button, new() { Name = "Save reading" }).ClickAsync();
            if (canWrite)
            {
                await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("1 readings stored.");
                await page.ReloadAsync();
                await page.GetByRole(AriaRole.Button, new() { Name = "Reload readings" }).ClickAsync();
                await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("1 readings stored.");
            }
            else await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Unable to save reading.");
        }
        finally
        {
            await page.ScreenshotAsync(new() { Path = Path.Combine(evidence, "final.png"), FullPage = true });
            await context.Tracing.StopAsync(new() { Path = Path.Combine(evidence, "trace.zip") });
            output.WriteLine($"Native browser evidence: {evidence}");
        }
    }
}
