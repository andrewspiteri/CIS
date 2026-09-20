namespace Cis.Abstractions;

/// <summary>Invokes explicitly loaded commands in-process for built-in orchestration modules.</summary>
public interface ICisCommandDispatcher
{
    CisCommandCapture Capture(string[] arguments);
}

public sealed record CisCommandCapture(int ExitCode, string StandardOutput, string StandardError);
