using System.Diagnostics;
using System.Text.RegularExpressions;

internal class Program
{
    private static async Task<byte[]> DownLoadFileFromUrl(HttpClient client, string url)
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
    private static async Task<(bool Success, string Output, string Error, int ExitCode)> RunFFmpegWithExitCodeAsync(string arguments
    , string workingDirectory = "")
    {
        string output = "";
        string error = "";
        int exitCode = -1; // Initialize with a default value

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo("/Users/phantom/Downloads/ffmpeg", arguments);
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            startInfo.WorkingDirectory = workingDirectory;

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;

                process.OutputDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        output += args.Data + Environment.NewLine;
                        Console.WriteLine(args.Data);
                    }
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        error += args.Data + Environment.NewLine;
                        Console.WriteLine($"Error: {args.Data}");
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
    private static string GetCorrectUrl(string contentUrl, string startUrl)
    {
        if (contentUrl.Length > 10)
        {
            var beginIndex = startUrl.IndexOf(contentUrl.Substring(0, 5));
            if (beginIndex > 10)
            {
                return startUrl.Substring(0, beginIndex) + contentUrl;
            }
        }
        return contentUrl.StartsWith("https://") ? contentUrl : $"{startUrl}{contentUrl}";
    }
    private static async Task Main(string[] args)
    {
        var m4sUrl = string.Empty;
        var defaultFileName = string.Empty;
        if (args.Length > 0)
        {
            m4sUrl = args[0];
            defaultFileName = args[1];
        }
        if (string.IsNullOrEmpty(m4sUrl))
        {
            Console.Write("Please provide the M4S URL:");
            m4sUrl = Console.ReadLine();
            Console.WriteLine(string.Empty);
            return;
        }
        if (string.IsNullOrEmpty(defaultFileName))
        {
            Console.WriteLine("Please provide the default file name:");
            defaultFileName = Console.ReadLine();
            Console.WriteLine(string.Empty);
            return;
        }
        Console.WriteLine($"M4S URL: {m4sUrl}");
        Console.WriteLine($"Default File Name: {defaultFileName}");

        var client = new HttpClient();
        var response = await client.GetAsync(m4sUrl);
        // string rootFoler = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string rootFoler = "/Volumes/SSD";
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var startUrl = m4sUrl.Substring(0, m4sUrl.LastIndexOf('/') + 1);
            var lines = content.Split("\n", StringSplitOptions.RemoveEmptyEntries);
            var urls = lines.Where(x => x.EndsWith(".m4s")).Select(x => GetCorrectUrl(x, startUrl)).ToList();
            var dirName = m4sUrl.Replace(startUrl, "");

            if (Directory.Exists(Path.Combine(rootFoler, "M4S")) == false) Directory.CreateDirectory(Path.Combine(rootFoler, "M4S"));

            string initFileName = string.Empty;
            if (lines.Any(x => x.StartsWith("#EXT-X-MAP:URI=")))
            {
                var line = lines.First(x => x.StartsWith("#EXT-X-MAP:URI="));
                Regex regex = new Regex(@"#EXT-X-MAP:URI=""(.*)""");
                var match = regex.Match(line);
                var initFileUrl =GetCorrectUrl(match.Groups[1].Value, startUrl);
                initFileName = initFileUrl.Substring(initFileUrl.LastIndexOf('/') + 1);
                var initFilePath = Path.Combine(rootFoler, "M4S", initFileName);
                if (File.Exists(initFilePath)) File.Delete(initFilePath);
                try
                {
                    var initBytes = await DownLoadFileFromUrl(client, initFileUrl);
                    using (FileStream fileStream = new FileStream(initFilePath, FileMode.Create))
                    {
                        // Write the bytes to the end of the stream.
                        fileStream.Write(initBytes, 0, initBytes.Length);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    return;
                }
            }
            var allFileName = $"{dirName}.m4s";
            var allFilePath = Path.Combine(rootFoler, "M4S", allFileName);
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
                    return;
                }
            }

            defaultFileName = string.IsNullOrEmpty(defaultFileName) ? $"{dirName}.mp4" : defaultFileName;
            if (File.Exists(Path.Combine(rootFoler, "M4S", defaultFileName))) File.Delete(Path.Combine(rootFoler, "M4S", defaultFileName));
            // Run FFmpeg to convert the M4S file to MP4
            (bool success, string output, string error, int exitCode) = await RunFFmpegWithExitCodeAsync($"-i \"concat:{initFileName}|{allFileName}\" -c copy \"{defaultFileName}\""
                , Path.Combine(rootFoler, "M4S"));

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