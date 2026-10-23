namespace QSLauncher.Core.Models;

public sealed class LaunchOptions
{
    public int MemoryMb { get; init; } = 4096;

    public string JvmArgs { get; init; } = string.Empty;

    public string GameArgs { get; init; } = string.Empty;

    public int? Width { get; init; }

    public int? Height { get; init; }

    public bool Demo { get; init; }

    public string? PlayerName { get; init; }
}

public sealed record LaunchPlan(
    IReadOnlyList<string> Arguments,
    string LogPath,
    string JavaExecutable,
    int MemoryMb,
    string WorkingDirectory);

public sealed class GameSession
{
    public required System.Diagnostics.Process Process { get; init; }

    public required string LogPath { get; init; }

    public required InstanceInfo Instance { get; init; }

    public Task WaitForExitAsync(CancellationToken cancellationToken = default)
        => Process.WaitForExitAsync(cancellationToken);
}
