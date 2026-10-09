namespace Cis.Modules.Repository;

public sealed partial class RepositoryInitializer
{
    private static void PlanExampleFiles(string repository, ExampleInstallationPlan examples,
        List<PlannedDirectory> directories, List<PlannedFile> files, List<string> retained, List<string> collisions)
    {
        var parents = new List<string>();
        foreach (var file in examples.Files)
        {
            var absolute = Path.Combine(repository, file.Path.Replace('/', Path.DirectorySeparatorChar));
            AddAncestors(Path.GetDirectoryName(absolute)!, repository, parents);
            // The normal writer reads previous text through StreamReader, which consumes a UTF-8 BOM.
            var previous = file.PreviousContent;
            if (previous?.StartsWith('\uFEFF') == true) previous = previous[1..];
            files.Add(new(file.Path, absolute, file.Content, previous,
                previous is null ? PlannedFileAction.Create : PlannedFileAction.Update, ReplaceAtomically: true));
        }
        PlanDirectories(repository, parents.Distinct(PathComparer)
            .Except(directories.Select(directory => directory.AbsolutePath), PathComparer), directories, retained, collisions);
    }

    private static void WriteExampleFile(string repository, PlannedFile file)
    {
        if (Cis.Abstractions.CisPathSafety.ContainsReparsePoint(repository, file.AbsolutePath))
            throw new IOException($"Example path changed after planning: {file.RelativePath}");
        if (file.PreviousContent is not null)
        {
            var current = BundledExampleFiles.ReadText(file.AbsolutePath);
            if (current.StartsWith('\uFEFF')) current = current[1..];
            if (current != file.PreviousContent) throw new IOException($"Example changed after planning: {file.RelativePath}");
        }
        var temporary = file.AbsolutePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false))) writer.Write(file.Content);
            // Replacement changes this directory entry instead of truncating an inode shared by hard links.
            File.Move(temporary, file.AbsolutePath, overwrite: file.PreviousContent is not null);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
