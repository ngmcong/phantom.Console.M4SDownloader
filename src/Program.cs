using System.Diagnostics;
using System.Text.RegularExpressions;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var m4sUrl = "";
        async Task<byte[]> DownLoadFileFromUrl(HttpClient client, string url)
        {
            var fileResponse = await client.GetAsync(url);
            if (fileResponse.IsSuccessStatusCode)
            {
                return fileResponse.Content.ReadAsByteArrayAsync().Result;
            }
            else
            {
                throw new Exception($"Error: {fileResponse.StatusCode}");
            }
        }
        async Task<(bool Success, string Output, string Error, int ExitCode)> RunFFmpegWithExitCodeAsync(string arguments)
        {
            string output = "";
            string error = "";
            int exitCode = -1; // Initialize with a default value

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo("D:\\Projects\\phantom.Console.M4SDownloader\\bin\\Debug\\net8.0\\144p.av1.mp4.m3u8\\ffmpeg", arguments);
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.CreateNoWindow = true;
                startInfo.WorkingDirectory = "D:\\Projects\\phantom.Console.M4SDownloader\\bin\\Debug\\net8.0\\144p.av1.mp4.m3u8";

                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;

                    process.OutputDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            output += args.Data + Environment.NewLine;
                        }
                    };

                    process.ErrorDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            error += args.Data + Environment.NewLine;
                        }
                    };

                    process.Start();

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync();

                    exitCode = process.ExitCode;

                    return (exitCode == 0, output, error, exitCode);
                }
            }
            catch (Exception ex)
            {
                return (false, output, $"Error running FFmpeg: {ex.Message}", exitCode);
            }
        }

        var client = new HttpClient();
        var response = await client.GetAsync(m4sUrl);
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var startUrl = m4sUrl.Substring(0, m4sUrl.LastIndexOf('/') + 1);
            var lines = content.Split("\n", StringSplitOptions.RemoveEmptyEntries);
            var urls = lines.Where(x => x.EndsWith(".m4s")).Select(x => $"{startUrl}{x}").ToList();
            var dirName = m4sUrl.Replace(startUrl, "");
            if (Directory.Exists(dirName) == false) Directory.CreateDirectory(dirName);

            string initFileName = string.Empty;
            if (lines.Any(x => x.StartsWith("#EXT-X-MAP:URI=")))
            {
                var line = lines.First(x => x.StartsWith("#EXT-X-MAP:URI="));
                Regex regex = new Regex(@"#EXT-X-MAP:URI=""(.*)""");
                var match = regex.Match(line);
                var initFileUrl = $"{startUrl}{match.Groups[1].Value}";
                initFileName = initFileUrl.Substring(initFileUrl.LastIndexOf('/') + 1);
                var initFilePath = Path.Combine(dirName, initFileName);
                if (File.Exists(initFilePath)) File.Delete(initFilePath);
                var initBytes = await DownLoadFileFromUrl(client, initFileUrl);
                using (FileStream fileStream = new FileStream(initFilePath, FileMode.Create))
                {
                    // Write the bytes to the end of the stream.
                    fileStream.Write(initBytes, 0, initBytes.Length);
                }
            }
            var allFileName = $"{dirName}.m4s";
            var allFilePath = Path.Combine(dirName, allFileName);
            if (File.Exists(allFilePath) == false) File.Delete(allFilePath);
            foreach (var url in urls)
            {
                try
                {
                    var bytes = await DownLoadFileFromUrl(client, url);
                    using (FileStream fileStream = new FileStream(allFilePath, FileMode.Append))
                    {
                        // Write the bytes to the end of the stream.
                        fileStream.Write(bytes, 0, bytes.Length);
                    }
                    Console.WriteLine($"Downloaded {urls.IndexOf(url) + 1}/{urls.Count}.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }

            (bool success, string output, string error, int exitCode) = await RunFFmpegWithExitCodeAsync($"-i \"concat:{initFileName}|{allFileName}\" -c copy 144p.av1.mp4");

            Console.WriteLine($"FFmpeg Exit Code: {exitCode}");

            if (success)
            {
                Console.WriteLine($"Successfully converted to MP4 file.");
            }
            else
            {
                Console.WriteLine("FFmpeg conversion failed.");
                Console.WriteLine($"Error Output:\n{error}");
            }
        }
        else
        {
            Console.WriteLine($"Error: {response.StatusCode}");
        }
    }
}