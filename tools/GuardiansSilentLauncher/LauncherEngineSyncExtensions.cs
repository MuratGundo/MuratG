namespace UCREW.GuardiansTR;

internal static class LauncherEngineSyncExtensions
{
    public static void PreparePatch(
        this LauncherEngine engine,
        IProgress<LauncherProgress> progress,
        CancellationToken cancellationToken)
    {
        engine.PreparePatchAsync(progress, cancellationToken)
            .GetAwaiter()
            .GetResult();
    }
}
