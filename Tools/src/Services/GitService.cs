using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlogTools.Services
{
    public class GitService
    {
        private readonly string _blogPath;

        private const int MaxDiffChars = 12_000;

        public GitService(string blogPath)
        {
            _blogPath = blogPath;
        }

        public string BlogPath => _blogPath;

        // ── High-level operations ───────────────────────────────

        /// <summary>
        /// Commit all changes and push. Legacy entry point preserved for compatibility.
        /// </summary>
        public async Task<string> CommitAndPushAsync(string commitMessage)
        {
            var results = new List<string>();

            var (okAdd, addOutput) = await StageAllAsync();
            results.Add(addOutput);
            if (!okAdd)
                return string.Join("\n", results);

            var (okCommit, commitOutput) = await CommitAsync(commitMessage);
            results.Add(commitOutput);
            if (!okCommit)
                return string.Join("\n", results);

            var (okPush, pushOutput) = await PushAsync();
            results.Add(pushOutput);

            return string.Join("\n", results);
        }

        /// <summary>
        /// Stage all changes (git add .). Returns (success, output).
        /// </summary>
        public async Task<(bool Success, string Output)> StageAllAsync()
        {
            return await RunGitCommandAsync("add", ".");
        }

        /// <summary>
        /// Create a commit with the given message. Returns (success, output).
        /// </summary>
        public async Task<(bool Success, string Output)> CommitAsync(string commitMessage)
        {
            return await RunGitCommandAsync("commit", "-m", commitMessage);
        }

        /// <summary>
        /// Push to the remote. Returns (success, output).
        /// </summary>
        public async Task<(bool Success, string Output)> PushAsync()
        {
            return await RunGitCommandAsync("push");
        }

        // ── Diff & file info for AI commit message ──────────────

        /// <summary>
        /// Get the list of files with changes (staged + unstaged).
        /// Binary files are labeled but their content is not included.
        /// </summary>
        public async Task<List<string>> GetChangedFilesAsync()
        {
            var (success, output) = await RunGitCommandAsync("diff", "--name-status", "HEAD");
            if (!success)
            {
                // Fall back to comparing with the index
                (success, output) = await RunGitCommandAsync("diff", "--name-status", "--cached");
            }

            var changedFiles = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();

            var untrackedFiles = await GetUntrackedFilesAsync();
            changedFiles.AddRange(untrackedFiles.Select(file => $"??\t{file}"));

            return changedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Get a summary of all current changes suitable for AI commit message generation.
        /// Includes:
        /// - List of changed files with status
        /// - Diff content truncated to ~12,000 characters
        /// - Binary files are listed by name only
        /// </summary>
        public async Task<string> GetDiffSummaryAsync(int maxChars = MaxDiffChars)
        {
            var sb = new StringBuilder();

            // 1. List changed files
            var changedFiles = await GetChangedFilesAsync();
            if (changedFiles.Count == 0)
                return string.Empty;

            sb.AppendLine("Changed files:");
            foreach (var file in changedFiles)
                sb.AppendLine($"  {file}");
            sb.AppendLine();

            // 2. Get file stats
            var (_, statOutput) = await RunGitCommandAsync("diff", "--stat", "HEAD");
            if (!string.IsNullOrWhiteSpace(statOutput))
            {
                sb.AppendLine("File statistics:");
                sb.AppendLine(statOutput.Trim());
                sb.AppendLine();
            }

            // 3. Get the actual diff (unified format), truncated
            var (_, diffOutput) = await RunGitCommandAsync("diff", "--unified=3", "HEAD");
            if (string.IsNullOrWhiteSpace(diffOutput))
            {
                // Try staged diff
                (_, diffOutput) = await RunGitCommandAsync("diff", "--unified=3", "--cached");
            }

            if (!string.IsNullOrWhiteSpace(diffOutput))
            {
                // Split diff into per-file blocks and include each with size awareness
                var blocks = SplitDiffIntoBlocks(diffOutput);
                sb.AppendLine("Diff summary:");
                var remaining = maxChars - sb.Length;
                foreach (var block in blocks)
                {
                    // For each file block, include the header and content up to remaining budget
                    var lines = block.Split('\n');
                    var header = lines.FirstOrDefault() ?? string.Empty;
                    sb.AppendLine(header);

                    foreach (var line in lines.Skip(1))
                    {
                        if (sb.Length + line.Length + 1 > maxChars)
                        {
                            sb.AppendLine("... (truncated)");
                            return TruncateSummary(sb.ToString(), maxChars);
                        }

                        sb.AppendLine(line);
                    }
                }
            }

            // 4. If there are new/untracked files not yet staged, note them
            var untrackedFiles = await GetUntrackedFilesAsync();
            if (untrackedFiles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("New/untracked files:");
                foreach (var file in untrackedFiles)
                {
                    sb.AppendLine($"  {file}");

                    var remaining = maxChars - sb.Length;
                    if (remaining <= 0)
                        return TruncateSummary(sb.ToString(), maxChars);

                    if (TryReadUntrackedTextSnippet(file, Math.Min(remaining, 2_000), out var snippet))
                    {
                        sb.AppendLine("  Text preview:");
                        sb.AppendLine(snippet);
                    }
                    else
                    {
                        sb.AppendLine("  (binary or unreadable; filename only)");
                    }
                }
            }

            return TruncateSummary(sb.ToString(), maxChars);
        }

        /// <summary>
        /// Check if there are any changes to commit (staged or unstaged).
        /// </summary>
        public async Task<bool> HasChangesAsync()
        {
            var (success, output) = await RunGitCommandAsync("status", "--porcelain");
            return success && !string.IsNullOrWhiteSpace(output);
        }

        // ── Remote operations ───────────────────────────────────

        /// <summary>
        /// Fetch remote refs without merging.
        /// </summary>
        public async Task<string> FetchAsync()
        {
            var (_, output) = await RunGitCommandAsync("fetch");
            return output;
        }

        /// <summary>
        /// Check whether the current branch is ahead / behind the remote tracking branch.
        /// Returns a tuple: (behind count, ahead count, raw status line).
        /// </summary>
        public async Task<(int Behind, int Ahead, string RawStatus)> CheckSyncStatusAsync()
        {
            // Make sure remote refs are up-to-date first
            await FetchAsync();

            var (_, output) = await RunGitCommandAsync("status", "-sb");
            // Example output: "## main...origin/main [behind 2]" or "[ahead 1, behind 3]"
            int behind = 0, ahead = 0;
            var firstLine = output.Split('\n')[0];

            var match = System.Text.RegularExpressions.Regex.Match(firstLine, @"\[(.+?)\]");
            if (match.Success)
            {
                var info = match.Groups[1].Value;
                var behindMatch = System.Text.RegularExpressions.Regex.Match(info, @"behind\s+(\d+)");
                if (behindMatch.Success) behind = int.Parse(behindMatch.Groups[1].Value);
                var aheadMatch = System.Text.RegularExpressions.Regex.Match(info, @"ahead\s+(\d+)");
                if (aheadMatch.Success) ahead = int.Parse(aheadMatch.Groups[1].Value);
            }

            return (behind, ahead, firstLine);
        }

        /// <summary>
        /// Pull (fetch + merge) the remote tracking branch.
        /// </summary>
        public async Task<string> PullAsync()
        {
            var (_, output) = await TryPullAsync();
            return output;
        }

        /// <summary>
        /// Pull (fetch + merge) and preserve the command exit status.
        /// </summary>
        public async Task<(bool Success, string Output)> TryPullAsync()
        {
            return await RunGitCommandAsync("pull");
        }

        /// <summary>
        /// Clone a repository into the target path.
        /// </summary>
        public static async Task<string> CloneAsync(string url, string targetPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("clone");
            psi.ArgumentList.Add(url);
            psi.ArgumentList.Add(targetPath);

            using var process = Process.Start(psi);
            if (process == null) return string.Empty;

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
                throw new InvalidOperationException($"git clone failed (exit {process.ExitCode}): {error.Trim()}");

            return output + "\n" + error;
        }

        // ── Private helpers ─────────────────────────────────────

        /// <summary>
        /// Run a git command with ArgumentList (safe argument passing) and exit code checking.
        /// Returns (success, combinedOutput).
        /// </summary>
        private async Task<(bool Success, string Output)> RunGitCommandAsync(params string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = _blogPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process == null)
                return (false, "Failed to start git process.");

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var combined = (output + "\n" + error).Trim();
            return (process.ExitCode == 0, combined);
        }

        /// <summary>
        /// Split a unified diff into per-file blocks, keeping binary file markers.
        /// </summary>
        private static List<string> SplitDiffIntoBlocks(string diff)
        {
            var blocks = new List<string>();
            var current = new StringBuilder();
            var inBinary = false;

            foreach (var line in diff.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');

                if (trimmed.StartsWith("diff --git "))
                {
                    // Start of a new file block
                    if (current.Length > 0)
                    {
                        blocks.Add(current.ToString().TrimEnd());
                        current.Clear();
                    }

                    inBinary = false;
                    current.AppendLine(trimmed);
                }
                else if (trimmed.StartsWith("Binary files "))
                {
                    inBinary = true;
                    current.AppendLine(trimmed);
                }
                else if (inBinary)
                {
                    // Skip the content of binary diffs (only keep the header)
                    if (trimmed.StartsWith("diff --git "))
                    {
                        inBinary = false;
                        blocks.Add(current.ToString().TrimEnd());
                        current.Clear();
                        current.AppendLine(trimmed);
                    }
                }
                else
                {
                    current.AppendLine(trimmed);
                }
            }

            if (current.Length > 0)
                blocks.Add(current.ToString().TrimEnd());

            return blocks;
        }

        private async Task<List<string>> GetUntrackedFilesAsync()
        {
            var (success, output) = await RunGitCommandAsync("ls-files", "--others", "--exclude-standard");
            if (!success || string.IsNullOrWhiteSpace(output))
                return new List<string>();

            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
        }

        private bool TryReadUntrackedTextSnippet(string relativePath, int maxChars, out string snippet)
        {
            snippet = string.Empty;
            if (maxChars <= 0)
                return false;

            try
            {
                var root = Path.GetFullPath(_blogPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                var fullPath = Path.GetFullPath(Path.Combine(_blogPath, relativePath));
                if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
                    return false;

                var bytes = new byte[Math.Max(maxChars * 3, 512)];
                using var stream = File.OpenRead(fullPath);
                var byteCount = stream.Read(bytes, 0, bytes.Length);
                if (bytes.Take(byteCount).Any(value => value == 0))
                    return false;

                snippet = Encoding.UTF8.GetString(bytes, 0, byteCount);
                if (snippet.Length > maxChars)
                    snippet = snippet[..maxChars];
                if (stream.Position < stream.Length)
                    snippet += "\n... (truncated)";

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string TruncateSummary(string summary, int maxChars)
        {
            if (summary.Length <= maxChars)
                return summary;

            const string suffix = "\n... (truncated)";
            return summary[..Math.Max(0, maxChars - suffix.Length)] + suffix;
        }
    }
}
