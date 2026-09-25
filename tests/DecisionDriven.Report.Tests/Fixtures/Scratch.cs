using System;
using System.Diagnostics;
using System.IO;

namespace DecisionDriven.Report.Tests.Fixtures;

/// <summary>
/// A temporary directory that is also, on request, a git repository.
/// </summary>
/// <remarks>
/// Commits are made with signing switched off and a fixed identity, whatever the machine's global
/// git configuration says: a developer or runner with commit signing on would otherwise need a key
/// for a test to pass, and one without an identity could not commit at all.
/// </remarks>
internal sealed class Scratch : IDisposable
{
    internal Scratch()
    {
        Root = Path.Combine(Path.GetTempPath(), "dd-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    internal string Root { get; }

    internal string PathOf(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    internal void Write(string relative, string text)
    {
        string path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    internal void Init() => Git("init", "--quiet");

    /// <summary>Stages everything and commits it, returning the new commit's id.</summary>
    internal string Commit(string message)
    {
        Git("add", "--all");
        Git("commit", "--quiet", "--message", message);
        return Git("rev-parse", "HEAD").Trim();
    }

    private string Git(params string[] arguments)
    {
        ProcessStartInfo start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string setting in new[]
        {
            "user.name=Fixture",
            "user.email=fixture@example.com",
            "commit.gpgsign=false",
            "core.autocrlf=false",
            "init.defaultBranch=main",
        })
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(setting);
        }

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        string error = process.StandardError.ReadToEndAsync().Result;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("git " + string.Join(" ", arguments) + " failed: " + error);
        }

        return output;
    }

    public void Dispose()
    {
        try
        {
            // git marks its object files read-only, which a recursive delete on Windows refuses.
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
