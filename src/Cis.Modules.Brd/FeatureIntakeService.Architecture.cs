using System.Text;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Brd;

public sealed partial class FeatureIntakeService
{
    public CisFeatureArchitectureResult Architecture(string workspacePath, string slug, bool prepare, string? expectedRevision = null)
    {
        try
        {
            var state = ReadWizard(workspacePath, slug);
            if (prepare && expectedRevision != ProjectWizard(state).Revision)
                throw new InvalidDataException("The feature changed. Refresh before generating architecture diagrams.");
            var generator = architectureGenerators?.SingleOrDefault()
                ?? throw new InvalidDataException("Load the CIS solution-design module to generate feature diagrams.");
            var input = ArchitectureInput(state);
            return generator.Run(input, prepare, () => ArchitectureInput(ReadWizard(workspacePath, slug)).InputHash == input.InputHash);
        }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        { return new("invalid", null, [], [], [e.Message]); }
    }

    private static CisFeatureArchitectureInput ArchitectureInput(WizardState state)
    {
        string Read(string relative)
        {
            var path = Path.Combine(state.Authority.RepositoryPath, state.Authority.DocumentationRoot, relative);
            if (!SafeAbsolutePath(path)) throw new InvalidDataException("Unsafe architecture context path.");
            if (!File.Exists(path)) return "";
            if (new FileInfo(path).Length > 2_097_152) throw new InvalidDataException("An architecture context document exceeds 2 MiB.");
            return File.ReadAllText(path);
        }
        var direction = string.Join("\n\n", new[] { "technical", "architecture", "contracts" }.SelectMany(page =>
            (state.Review.Pages.GetValueOrDefault(page)?.Answers ?? []).OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => page + "/" + pair.Key + ":\n" + pair.Value)));
        var repositories = state.Workspace.Repositories.Where(repo => SamePath(repo.RepositoryPath, state.Record.Plan.RepositoryPath)
            || state.Record.Plan.IntegrationRepositories.Contains(repo.Id, StringComparer.Ordinal)).Select(repo => repo.Id).Order(StringComparer.Ordinal).ToArray();
        var input = new CisFeatureArchitectureInput(state.Authority.RepositoryPath, state.Record.Plan.Slug, state.Record.Plan.Title,
            state.Workspace.Product?.Name ?? state.Record.Plan.Title, "", state.Source, direction,
            Read("architecture/overall-solution-design.md"), Read("specs/technical-intent-spec.md"), Read("references/component-sheet.md"), repositories);
        return input with { InputHash = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input, Json))) };
    }
}
