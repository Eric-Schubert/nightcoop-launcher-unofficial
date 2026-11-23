using System.Globalization;
using System.Security.Cryptography;
using NightCoopLauncher.Core.Game;
using NightCoopLauncher.Core.Releases;

namespace NightCoopLauncher.Core.Install;

public readonly record struct TransferProgress(long BytesDone, long BytesTotal);

public sealed class ModInstaller(HttpClient http)
{
    private const int BufferSize = 81920;

    public async Task<InstallState> InstallAsync(
        GameInstall game,
        string gameVersion,
        ModRelease release,
        ModFile file,
        InstallState? previous,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(file);

        var workDirectory = Directory.CreateTempSubdirectory("NightCoopLauncher-").FullName;
        try
        {
            var archivePath = Path.Combine(workDirectory, "mod.zip");
            await DownloadAsync(file, archivePath, progress, cancellationToken).ConfigureAwait(false);
            await VerifyAsync(file, archivePath, cancellationToken).ConfigureAwait(false);

            var files = await Task.Run(
                () => ArchiveDeployer.Deploy(archivePath, Path.Combine(workDirectory, "staging"), game.Path, previous?.Files ?? []),
                cancellationToken).ConfigureAwait(false);

            var state = new InstallState(
                release.Version,
                release.Protocol,
                gameVersion,
                file.Sha256,
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                files);
            InstallStateStore.Save(game.Path, state);
            return state;
        }
        catch (HttpRequestException exception)
        {
            throw new LauncherException($"The download failed: {exception.Message}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new LauncherException($"No write access to the game folder: {exception.Message}", exception);
        }
        finally
        {
            TryDelete(workDirectory);
        }
    }

    private async Task DownloadAsync(ModFile file, string target, IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? file.Size;
        var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            var destination = File.Create(target);
            await using (destination.ConfigureAwait(false))
            {
                var buffer = new byte[BufferSize];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    done += read;
                    progress?.Report(new TransferProgress(done, total));
                }
            }
        }
    }

    private static async Task VerifyAsync(ModFile file, string archivePath, CancellationToken cancellationToken)
    {
        var stream = File.OpenRead(archivePath);
        await using (stream.ConfigureAwait(false))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new LauncherException(
                    $"The checksum of {file.Name} does not match the release list. The file is damaged or was replaced; nothing was installed.");
            }
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for the system's temp cleanup; not worth failing a finished install over.
        }
    }
}
