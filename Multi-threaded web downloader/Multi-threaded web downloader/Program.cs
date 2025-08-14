using System.Net;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;

class MultiThreadDownloader
{
    public int ThreadCount { get; set; } = 4;
    public List<string> AllowedExtensions { get; set; } = new List<string> { ".mp4", ".zip", ".exe", ".jpg" };

    // 新增：用于统计已下载字节数
    private long downloadedBytes;

    public async Task DownloadFileAsync(string url, string outputPath)
    {
        // 修正扩展名获取逻辑
        string ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLower();
        if (!AllowedExtensions.Contains(ext))
        {
            Console.WriteLine($"Extension '{ext}' not allowed. Skipping download.");
            return;
        }

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
        Console.WriteLine($"File size: {fileSize} bytes");

        var tasks = new List<Task>();
        long partSize = fileSize / ThreadCount;
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Write);
        fs.SetLength(fileSize);
        fs.Close();

        // 新增：进度条显示任务
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
                // 实时流式下载
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
                // 不再输出分块完成信息，避免干扰进度条
                // Console.WriteLine($"Part {part + 1} done: {start}-{end}");
            }));
        }
        await Task.WhenAll(tasks);
        cts.Cancel(); // 停止进度条显示
        await progressTask;
        Console.WriteLine("\nDownload complete.");
    }

    // 新增：进度条和剩余时间显示方法
    private async Task ShowProgressAsync(long totalBytes, CancellationToken token)
    {
        var sw = Stopwatch.StartNew();
        long lastBytes = 0;
        while (!token.IsCancellationRequested)
        {
            long current = Interlocked.Read(ref downloadedBytes);
            double percent = totalBytes == 0 ? 0 : (current * 100.0 / totalBytes);
            double speed = sw.Elapsed.TotalSeconds > 0 ? (current / sw.Elapsed.TotalSeconds) : 0; // bytes/sec
            long remainBytes = totalBytes - current;
            double remainSec = speed > 0 ? remainBytes / speed : 0;
            string bar = new string('#', (int)(percent / 2)).PadRight(50);
            Console.Write($"\r[{bar}] {percent:F2}%  {current}/{totalBytes} bytes  速度: {speed / 1024:F2} KB/s  剩余: {TimeSpan.FromSeconds(remainSec):hh':'mm':'ss}   ");
            lastBytes = current;
            await Task.Delay(500, token).ContinueWith(_ => { });
        }
        // 下载完成后输出换行，避免和后续输出混在一起
        Console.WriteLine();
    }
}

// --- Main program ---
class Program
{
    static async Task Main(string[] args)
    {
        var downloader = new MultiThreadDownloader();
        Console.WriteLine("输入下载线程数 (默认4):");
        if (int.TryParse(Console.ReadLine(), out int threads))
            downloader.ThreadCount = threads;

        Console.WriteLine("输入允许自动下载的扩展名(用逗号分隔,如 .mp4,.zip):");
        var exts = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(exts))
            downloader.AllowedExtensions = exts.Split(',').Select(e => e.Trim().ToLower()).ToList();

        Console.WriteLine("输入下载链接:");
        var url = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(url)) return;
        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        await downloader.DownloadFileAsync(url, fileName);
    }
}
