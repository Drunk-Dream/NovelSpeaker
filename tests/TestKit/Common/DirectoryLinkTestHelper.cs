using System.ComponentModel;
using System.Diagnostics;
using Xunit;

namespace NovelSpeaker.TestKit.Common;

public static class DirectoryLinkTestHelper
{
    private static readonly Lazy<string?> LinkCapability = new(ProbeLinkCapability);

    public static string? SkipReason => LinkCapability.Value;

    public static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (OperatingSystem.IsWindows() && TryCreateJunction(linkPath, targetPath))
        {
            return;
        }

        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Directory-link support was available during test discovery but creation failed ({exception.GetType().Name}).",
                exception);
        }
    }

    private static string? ProbeLinkCapability()
    {
        var root = Path.Combine(Path.GetTempPath(), "NovelSpeaker-Link-Probe-" + Path.GetRandomFileName());
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "link");

        try
        {
            Directory.CreateDirectory(target);
            if (OperatingSystem.IsWindows() && TryCreateJunction(link, target))
            {
                return null;
            }

            Directory.CreateSymbolicLink(link, target);
            return null;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            return $"Directory link creation is unavailable ({exception.GetType().Name}).";
        }
        finally
        {
            try
            {
                if (Directory.Exists(link))
                {
                    Directory.Delete(link);
                }

                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static bool TryCreateJunction(string linkPath, string targetPath)
    {
        var commandPath = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
        var startInfo = new ProcessStartInfo(commandPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            _ = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}

public sealed class DirectoryLinkFactAttribute : FactAttribute
{
    public DirectoryLinkFactAttribute() => Skip = DirectoryLinkTestHelper.SkipReason;
}

public sealed class DirectoryLinkTheoryAttribute : TheoryAttribute
{
    public DirectoryLinkTheoryAttribute() => Skip = DirectoryLinkTestHelper.SkipReason;
}
