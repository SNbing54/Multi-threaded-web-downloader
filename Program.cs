using System.Net;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using System.Text.Json;

class DownloaderSettings
{
    public int ThreadCount { get; set; } = 4;
    public List<string> AllowedExtensions { get; set; } = new List<string> { ".mp4", ".zip", ".exe", ".jpg" };
    public string DownloadDirectory { get; set; } = Directory.GetCurrentDirectory();
}

class MultiThreadDownloader
{
    public int ThreadCount { get; set; } = 4;
    public List<string> AllowedExtensions { get; set; } = new List<string> { ".mp4", ".zip", ".exe", ".jpg" };
    public string DownloadDirectory { get; set; } = Directory.GetCurrentDirectory();

    private long downloadedBytes;
    public const string SettingsFile = "downloader_settings.json";

    public void LoadSettings()
    {
        if (File.Exists(SettingsFile))
        {
            var json = File.ReadAllText(SettingsFile);
            var s = JsonSerializer.Deserialize<DownloaderSettings>(json);
            if (s != null)
            {
                ThreadCount = s.ThreadCount;
                AllowedExtensions = s.AllowedExtensions;
                DownloadDirectory = string.IsNullOrWhiteSpace(s.DownloadDirectory) ? Directory.GetCurrentDirectory() : s.DownloadDirectory;
            }
        }
    }

    public void SaveSettings()
    {
        var s = new DownloaderSettings { ThreadCount = ThreadCount, AllowedExtensions = AllowedExtensions, DownloadDirectory = DownloadDirectory };
        var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFile, json);
    }

    public async Task DownloadFileAsync(string url, string outputFileName)
    {
        string ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLower();
        if (!AllowedExtensions.Contains(ext))
        {
            Console.WriteLine($"Extension '{ext}' not allowed. Skipping download.");
            return;
        }

        string outputPath = Path.Combine(DownloadDirectory, outputFileName);
        Directory.CreateDirectory(DownloadDirectory);

        using var client = new HttpClient();
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to access file: {response.StatusCode}");
            return;
        }
        if (!response.Content.Headers.ContentLength.HasValue)
        {
            Console.WriteLine("Cannot determine file size.");
            return;
        }
        long fileSize = response.Content.Headers.ContentLength.Value;
        Console.WriteLine($"File size: {fileSize / 1024} KB");

        var tasks = new List<Task>();
        long partSize = fileSize / ThreadCount;
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Write);
        fs.SetLength(fileSize);
        fs.Close();

        downloadedBytes = 0;
        var cts = new CancellationTokenSource();
        var progressTask = ShowProgressAsync(fileSize, cts.Token);

        for (int i = 0; i < ThreadCount; i++)
        {
            long start = i * partSize;
            long end = (i == ThreadCount - 1) ? fileSize - 1 : (start + partSize - 1);
            int part = i;
            tasks.Add(Task.Run(async () =>
            {
                using var partClient = new HttpClient();
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);
                var resp = await partClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                using var stream = await resp.Content.ReadAsStreamAsync();
                byte[] buffer = new byte[8192];
                long position = start;
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    lock (this)
                    {
                        using var fsPart = new FileStream(outputPath, FileMode.Open, FileAccess.Write, FileShare.Write);
                        fsPart.Seek(position, SeekOrigin.Begin);
                        fsPart.Write(buffer, 0, bytesRead);
                    }
                    Interlocked.Add(ref downloadedBytes, bytesRead);
                    position += bytesRead;
                }
            }));
        }
        await Task.WhenAll(tasks);
        cts.Cancel();
        await progressTask;
        Console.WriteLine("\nDownload complete.");
    }

    // 进度条和剩余时间显示方法，单位改为KB
    private async Task ShowProgressAsync(long totalBytes, CancellationToken token)
    {
        var sw = Stopwatch.StartNew();
        while (!token.IsCancellationRequested)
        {
            long current = Interlocked.Read(ref downloadedBytes);
            double percent = totalBytes == 0 ? 0 : (current * 100.0 / totalBytes);
            double speed = sw.Elapsed.TotalSeconds > 0 ? (current / sw.Elapsed.TotalSeconds) : 0; // bytes/sec
            long remainBytes = totalBytes - current;
            double remainSec = speed > 0 ? remainBytes / speed : 0;
            string bar = new string('#', (int)(percent / 2)).PadRight(50);
            Console.Write($"\r[{bar}] {percent:F2}%  {current / 1024}/{totalBytes / 1024} KB  速度: {speed / 1024:F2} KB/s  剩余: {TimeSpan.FromSeconds(remainSec):hh':'mm':'ss}   ");
            await Task.Delay(500, token).ContinueWith(_ => { });
        }
        Console.WriteLine();
    }
}

// --- Main program ---
class Program
{
    static async Task Main(string[] args)
    {
        var downloader = new MultiThreadDownloader();
        downloader.LoadSettings();
        while (true)
        {
            Console.WriteLine("输入1下载文件，输入0修改设置，输入其它退出:");
            var op = Console.ReadLine();
            if (op == "1")
            {
                Console.WriteLine($"当前线程数: {downloader.ThreadCount}");
                Console.WriteLine($"当前允许扩展名: {string.Join(",", downloader.AllowedExtensions)}");
                Console.WriteLine($"当前下载保存路径: {downloader.DownloadDirectory}");
                Console.WriteLine("输入下载链接:");
                var url = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(url)) continue;
                var fileName = Path.GetFileName(new Uri(url).LocalPath);
                await downloader.DownloadFileAsync(url, fileName);
            }
            else if (op == "0")
            {
                Console.WriteLine($"当前线程数: {downloader.ThreadCount}");
                Console.WriteLine("输入下载线程数 (默认4):");
                if (int.TryParse(Console.ReadLine(), out int threads))
                    downloader.ThreadCount = threads;
                Console.WriteLine($"当前允许扩展名: {string.Join(",", downloader.AllowedExtensions)}");
                Console.WriteLine("输入允许自动下载的扩展名(用逗号分隔,如 .mp4,.zip):");
                var exts = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(exts))
                    downloader.AllowedExtensions = exts.Split(',').Select(e => e.Trim().ToLower()).ToList();
                Console.WriteLine($"当前下载保存路径: {downloader.DownloadDirectory}");
                Console.WriteLine("输入下载保存路径(留空为当前路径):");
                var dir = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(dir))
                    downloader.DownloadDirectory = dir;
                downloader.SaveSettings();
                Console.WriteLine("设置已保存！");
            }
            else
            {
                break;
            }
        }
    }
}
