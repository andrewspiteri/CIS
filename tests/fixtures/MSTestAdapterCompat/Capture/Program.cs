// Fixture-only bounded copy of original native bytes before transient pruning.
// It does not classify outcomes, approve cleanup or assign mutation credit.
if (args.Length != 2) return 2;
var native = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
var destination = Path.GetFullPath(args[1]);
var logs = Path.Combine(native, "logs");
if (!Directory.Exists(logs) || Directory.Exists(destination)) return 3;
var files = Directory.GetFiles(logs, "*", SearchOption.AllDirectories);
if (files.Length > 32 || files.Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
    || files.Sum(path => new FileInfo(path).Length) > 16 * 1024 * 1024) return 4;
Directory.CreateDirectory(destination);
foreach (var file in files)
{
    var output = Path.Combine(destination, "logs", Path.GetRelativePath(logs, file));
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.Copy(file, output);
}
File.Copy(Path.Combine(native, "native-result.json"), Path.Combine(destination, "native-result.json"));
return 0;
