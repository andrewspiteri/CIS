namespace Example.BusinessTests;

internal sealed record ProcessDiagnostic(int? ExitCode, int ProcessId, string Stdout, string Stderr, string? Failure, string? CleanupFailure);
