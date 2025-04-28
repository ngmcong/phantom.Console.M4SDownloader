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
                        // Console.WriteLine(args.Data);
                    }
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        error += args.Data + Environment.NewLine;
                        // Console.WriteLine($"Error: {args.Data}");
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
        if (contentUrl.Length > 20)
        {
            var lastIndex = startUrl.LastIndexOf(contentUrl.Substring(0, 20));
            if (lastIndex > 0)
            {
                return startUrl.Substring(0, lastIndex) + contentUrl;
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
            if (Directory.Exists(Path.Combine(rootFoler, "M4S", dirName)) == false) Directory.CreateDirectory(Path.Combine(rootFoler, "M4S", dirName));

            string initFileName = string.Empty;
            string initFilePath = string.Empty;
            if (lines.Any(x => x.StartsWith("#EXT-X-MAP:URI=")))
            {
                var line = lines.First(x => x.StartsWith("#EXT-X-MAP:URI="));
                Regex regex = new Regex(@"#EXT-X-MAP:URI=""(.*)""");
                var match = regex.Match(line);
                var initFileUrl = GetCorrectUrl(match.Groups[1].Value, startUrl);
                initFileName = initFileUrl.Substring(initFileUrl.LastIndexOf('/') + 1);
                initFilePath = Path.Combine(rootFoler, "M4S", dirName, initFileName);
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
            bool success; string output; string error; int exitCode;
            using (StreamWriter writer = new StreamWriter(Path.Combine(rootFoler, "M4S", dirName, $"{dirName}.txt"), true))
            {
                foreach (var url in urls)
                {
                    try
                    {
                        var fileName = url.Substring(url.LastIndexOf('/') + 1);
                        var bytes = await DownLoadFileFromUrl(client, url);
                        using (FileStream fileStream = new FileStream(Path.Combine(rootFoler, "M4S", dirName, fileName), FileMode.Create))
                        {
                            fileStream.Write(bytes, 0, bytes.Length);
                        }

                        // Run FFmpeg to convert the M4S file to MP4
                        (success, output, error, exitCode) = await RunFFmpegWithExitCodeAsync($"-i \"concat:{initFileName}|{fileName}\" -c copy \"{fileName.Replace(".m4s", ".mp4")}\""
                            , Path.Combine(rootFoler, "M4S", dirName));
                        if (success == false)
                        {
                            Console.WriteLine("FFmpeg conversion failed.");
                            Console.WriteLine($"Error Output:\n{error}");
                            return;
                        }

                        File.Delete(Path.Combine(rootFoler, "M4S", dirName, fileName));
                        await writer.WriteLineAsync($"file '{Path.Combine(rootFoler, "M4S", dirName, fileName.Replace(".m4s", ".mp4"))}'");

                        Console.WriteLine($"Downloaded {urls.IndexOf(url) + 1}/{urls.Count}.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error: {ex.Message}");
                        return;
                    }
                }
            }
            if (string.IsNullOrEmpty(initFilePath) == false && File.Exists(initFilePath)) File.Delete(initFilePath);
            defaultFileName = string.IsNullOrEmpty(defaultFileName) ? $"{dirName}.mp4" : defaultFileName;
            // Run FFmpeg to merge the list mp4 file to final MP4
            (success, output, error, exitCode) = await RunFFmpegWithExitCodeAsync($"-f concat -safe 0 -i {dirName}.txt -c copy \"{defaultFileName}\""
                , Path.Combine(rootFoler, "M4S", dirName));
            Console.WriteLine($"FFmpeg Exit Code: {exitCode}");
            if (success)
            {
                Console.WriteLine($"Successfully converted to MP4 file.");
            }
            else
            {
                Console.WriteLine("FFmpeg conversion failed.");
                Console.WriteLine($"Error Output:\n{error}");
                return;
            }
            File.Move(Path.Combine(rootFoler, "M4S", dirName, defaultFileName), Path.Combine(rootFoler, "M4S", defaultFileName));
            var files = Directory.GetFiles(Path.Combine(rootFoler, "M4S", dirName), "*.*");
            foreach (var file in files)
            {
                File.Delete(file);
            }
            Directory.Delete(Path.Combine(rootFoler, "M4S", dirName));
        }
        else
        {
            Console.WriteLine($"Error: {response.StatusCode}");
        }
    }
}