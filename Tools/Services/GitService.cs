using System.Diagnostics;
using System.Threading.Tasks;

namespace BlogTools.Services
{
    public class GitService
    {
        private readonly string _blogPath;

        public GitService(string blogPath)
        {
            _blogPath = blogPath;
        }

        public async Task<string> CommitAndPushAsync(string commitMessage)
        {
            var addResult = await RunGitCommandAsync("add .");
            var commitResult = await RunGitCommandAsync($"commit -m \"{commitMessage.Replace("\"", "\\\"")}\"");
            var pushResult = await RunGitCommandAsync("push");
            return $"{addResult}\n{commitResult}\n{pushResult}";
        }

        /// <summary>
        /// Fetch remote refs without merging.
        /// </summary>
        public async Task<string> FetchAsync()
        {
            return await RunGitCommandAsync("fetch");
        }

        /// <summary>
        /// Check whether the current branch is ahead / behind the remote tracking branch.
        /// Returns a tuple: (behind count, ahead count, raw status line).
        /// </summary>
        public async Task<(int Behind, int Ahead, string RawStatus)> CheckSyncStatusAsync()
        {
            // Make sure remote refs are up-to-date first
            await FetchAsync();

            var output = await RunGitCommandAsync("status -sb");
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
            return await RunGitCommandAsync("pull");
        }

        private async Task<string> RunGitCommandAsync(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = _blogPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return string.Empty;

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return output + "\n" + error;
        }
    }
}
