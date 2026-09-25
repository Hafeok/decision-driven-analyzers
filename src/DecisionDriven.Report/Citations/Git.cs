using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DecisionDriven.Report.Citations;

/// <summary>
/// The repository's history, through the git command line.
/// </summary>
/// <remarks>
/// Arguments are passed as a list, never through a shell, so a file or ref name is always one
/// argument whatever it contains. A command that fails - no git, not a repository, a ref that does
/// not exist - returns null rather than throwing: the report is not a gate
/// (<c>WholeGraphReport.NothingGatesUntilBaselineDecision</c>), and a citation it cannot date is
/// reported as undated rather than taking the rest of the report down with it.
/// </remarks>
internal sealed class Git
{
    private readonly string root;
    private string? objectFormat;

    internal Git(string root)
    {
        this.root = root;
    }

    /// <summary>The commit checked out, or null outside a repository.</summary>
    internal string? Head => First(Run("rev-parse", "HEAD"));

    /// <summary>
    /// <c>sha1</c> or <c>sha256</c>. <c>WholeGraphReport.LedgerCommitRequired</c> keys a commit as
    /// <c>urn:git:sha1:</c> or <c>urn:git:sha256:</c>, and which one is a property of the repository.
    /// </summary>
    internal string ObjectFormat => objectFormat ??= First(Run("rev-parse", "--show-object-format")) ?? "sha1";

    /// <summary>
    /// The commit that introduced <paramref name="key"/> into <paramref name="file"/>: the oldest one
    /// in which the number of occurrences changed. Falls back to the first commit that touched the
    /// file, and with no file, to the first commit that added the key anywhere outside the ledger.
    /// </summary>
    /// <remarks>
    /// The key rather than the whole attribute line, because the line's exact text is not in the
    /// assembly and the key is the part of it that names the decision. The ledger directory is
    /// excluded from the repository-wide search, since the commit that filed the decision is
    /// certainly one that added its key.
    /// </remarks>
    internal string? IntroducingCommit(string? file, string key, string ledgerDirectory)
    {
        if (file is not null)
        {
            return First(Run("log", "--reverse", "--format=%H", "-S", key, "--", file))
                ?? First(Run("log", "--reverse", "--format=%H", "--", file));
        }

        return First(Run(
            "log", "--reverse", "--format=%H", "-S", key, "--",
            ".", ":(exclude)" + ledgerDirectory.Replace('\\', '/').TrimEnd('/')));
    }

    /// <summary>True when <paramref name="commit"/> is <paramref name="of"/> or one of its ancestors.</summary>
    internal bool IsAncestor(string commit, string of) =>
        Run("merge-base", "--is-ancestor", commit, of) is not null;

    /// <summary>The files under <paramref name="directory"/> as they were at <paramref name="commit"/>.</summary>
    internal IReadOnlyList<string> FilesAt(string commit, string directory)
    {
        string? listing = Run("ls-tree", "-r", "--name-only", commit, "--", directory.Replace('\\', '/'));
        return listing is null
            ? Array.Empty<string>()
            : listing.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>A file's content at a commit.</summary>
    internal string? Show(string commit, string path) => Run("show", commit + ":" + path);

    private static string? First(string? output)
    {
        if (output is null)
        {
            return null;
        }

        foreach (string line in output.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return null;
    }

    private string? Run(params string[] arguments)
    {
        ProcessStartInfo start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };

        // Paths come back as they are, not quoted and octal-escaped.
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.quotepath=false");

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using Process? process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            // Both streams are drained, so a command that writes a lot to stderr cannot fill the
            // pipe and hang waiting for a reader.
            System.Threading.Tasks.Task<string> error = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            _ = error.Result;

            return process.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No git on the path.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
