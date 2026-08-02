namespace NyaLauncher.Core.Tools;

using System.Net.Http;
using System.IO;
using System.Threading.Tasks;

/// <summary>
/// 关于高速下载功能的专用类
/// </summary>
public class FastDownloader
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private const int DefaultBufferSize = 8192;

    public string DownloadUrl { get; set; }
    public int FilePiece { get; set; }
    public string FailReason { get; set; } = string.Empty;

    /// <summary>
    /// 高速下载器类初始化
    /// </summary>
    /// <param name="url">文件的地址</param>
    public FastDownloader(string url)
    {
        DownloadUrl = url;
        FilePiece = 1;
    }

    /// <summary>
    /// 获取文件大小，用于判断需要分成的下载文件块数量
    /// </summary>
    /// <returns>文件大小</returns>
    public long GetFileSize()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Head, DownloadUrl);
            var response = _httpClient.Send(request);

            if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength.HasValue)
            {
                return response.Content.Headers.ContentLength.Value;
            }

            FailReason = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
            return -1;
        }
        catch (Exception ex)
        {
            FailReason = $"获取文件大小失败: {ex.Message}";
            return -1;
        }
    }

    /// <summary>
    /// 启动下载的方法
    /// </summary>
    /// <param name="pieceNum">分片数量，默认为1（单线程下载）</param>
    /// <returns>文件是否下载成功，失败时返回False，同时在FailReason为原因</returns>
    public bool Download(int pieceNum = 1)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(DownloadUrl))
            {
                FailReason = "下载链接不能为空";
                return false;
            }

            pieceNum = Math.Max(1, pieceNum);
            FilePiece = pieceNum;

            // 获取文件大小
            long fileSize = GetFileSize();
            if (fileSize <= 0)
            {
                return false;
            }

            // 提取文件名
            string fileName = Path.GetFileName(new Uri(DownloadUrl).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "download";
            }

            string downloadPath = Path.Combine(Path.GetTempPath(), fileName);
            string tempPath = downloadPath + ".tmp";

            // 单线程下载处理
            if (pieceNum == 1)
            {
                return DownloadSingleThread(downloadPath, tempPath);
            }

            // 多线程分片下载处理
            return DownloadMultiThread(downloadPath, tempPath, fileSize, pieceNum);
        }
        catch (Exception ex)
        {
            FailReason = $"下载过程出错: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 单线程下载
    /// </summary>
    private bool DownloadSingleThread(string downloadPath, string tempPath)
    {
        try
        {
            using (var response = _httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead).Result)
            {
                if (!response.IsSuccessStatusCode)
                {
                    FailReason = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                    return false;
                }

                using (var stream = response.Content.ReadAsStreamAsync().Result)
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, DefaultBufferSize, useAsync: true))
                {
                    stream.CopyTo(fileStream, DefaultBufferSize);
                }
            }

            // 下载完成后移动文件
            if (File.Exists(downloadPath))
            {
                File.Delete(downloadPath);
            }
            File.Move(tempPath, downloadPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            FailReason = $"单线程下载失败: {ex.Message}";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            return false;
        }
    }

    /// <summary>
    /// 多线程分片下载
    /// </summary>
    private bool DownloadMultiThread(string downloadPath, string tempPath, long fileSize, int pieceNum)
    {
        try
        {
            long pieceSize = fileSize / pieceNum;
            var downloadTasks = new Task[pieceNum];
            var pieceFiles = new string[pieceNum];

            // 创建分片下载任务
            for (int i = 0; i < pieceNum; i++)
            {
                long startByte = i * pieceSize;
                long endByte = (i == pieceNum - 1) ? fileSize - 1 : (i + 1) * pieceSize - 1;
                int index = i;

                pieceFiles[i] = $"{tempPath}.part{i}";

                downloadTasks[i] = Task.Run(() =>
                    DownloadPiece(index, startByte, endByte, pieceFiles[index])
                );
            }

            // 等待所有下载任务完成
            Task.WaitAll(downloadTasks);

            // 检查是否所有分片都下载成功
            for (int i = 0; i < pieceNum; i++)
            {
                if (!File.Exists(pieceFiles[i]))
                {
                    FailReason = $"分片 {i} 下载失败";
                    return false;
                }
            }

            // 合并分片文件
            return MergePieceFiles(pieceFiles, downloadPath, tempPath);
        }
        catch (Exception ex)
        {
            FailReason = $"多线程下载失败: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 下载单个文件分片
    /// </summary>
    private bool DownloadPiece(int pieceIndex, long startByte, long endByte, string outputPath)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, DownloadUrl);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(startByte, endByte);

            using (var response = _httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead))
            {
                if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                {
                    FailReason = $"分片 {pieceIndex} 下载失败: HTTP {(int)response.StatusCode}";
                    return false;
                }

                using (var stream = response.Content.ReadAsStreamAsync().Result)
                using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, DefaultBufferSize, useAsync: true))
                {
                    stream.CopyTo(fileStream, DefaultBufferSize);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            FailReason = $"分片 {pieceIndex} 下载异常: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 合并分片文件
    /// </summary>
    private bool MergePieceFiles(string[] pieceFiles, string downloadPath, string tempPath)
    {
        try
        {
            using (var outputStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                foreach (var pieceFile in pieceFiles)
                {
                    using (var inputStream = new FileStream(pieceFile, FileMode.Open, FileAccess.Read))
                    {
                        inputStream.CopyTo(outputStream, DefaultBufferSize);
                    }
                    File.Delete(pieceFile);
                }
            }

            // 下载完成后移动文件
            if (File.Exists(downloadPath))
            {
                File.Delete(downloadPath);
            }
            File.Move(tempPath, downloadPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            FailReason = $"合并分片文件失败: {ex.Message}";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            return false;
        }
    }
}