using System.IO;

namespace NyaLauncher.Core.Logs;

public class LogsWrite
{
    /// <summary>全部实例共享一把写锁：后台线程 / 崩溃回调可能并发写同一个文件。</summary>
    private static readonly object WriteLock = new();

    /// <summary>
    /// 进程内共享的日志文件路径：首个实例创建时以当前时刻定死文件名，
    /// 之后无论再 new 多少个实例（窗口重挂载导致 Loaded 多次触发等场景）
    /// 都追加到同一文件，避免日志分散到多个文件里找不到。
    /// </summary>
    private static string? _sharedFilePath;

    private static string TimeGet()
    {
        return DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
    }

    /// <summary>日志目录：用户目录下的 NyaLauncher/Logs，用户目录不可用时回落到程序目录。</summary>
    private static string GetLogDirectory()
    {
        // GetFolderPath 跨平台可用（Windows/macOS/Linux 都有用户目录语义），
        // 异常场景可能返回空串，此时回落到程序目录保证日志仍可落盘
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(
            string.IsNullOrEmpty(userProfile) ? AppContext.BaseDirectory : userProfile,
            "NyaLauncher", "Logs");
    }

    /// <summary>取得（并按需创建）本次运行的共享日志文件路径，全程持锁防并发竞争。</summary>
    private static string EnsureSharedFilePath()
    {
        lock (WriteLock)
        {
            if (_sharedFilePath is not null)
                return _sharedFilePath;

            var dir = GetLogDirectory();
            Directory.CreateDirectory(dir);
            _sharedFilePath = Path.Combine(dir, TimeGet() + ".log");
            return _sharedFilePath;
        }
    }

    public LogsWrite()
    {
        try
        {
            lock (WriteLock)
            {
                File.AppendAllText(EnsureSharedFilePath(), $"[{TimeGet()}][INIT]日志系统初始化成功\n");
            }
            Console.WriteLine($"[{TimeGet()}][INIT]日志系统初始化成功");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Console.WriteLine("日志初始化系统失败");
        }
    }

    /// <summary>
    /// Writes through the process-wide launcher log without constructing another
    /// logger instance. Host bridges such as the plugin logger use this entry so
    /// they share the same file and console mirror without emitting extra INIT rows.
    /// </summary>
    public static bool Write(string info, string type = "INFO")
    {
        try
        {
            var normalizedType = string.IsNullOrWhiteSpace(type) ? "INFO" : type.Trim();
            var normalizedInfo = info ?? string.Empty;
            lock (WriteLock)
            {
                var entry = $"[{TimeGet()}][{normalizedType}]{normalizedInfo}";
                File.AppendAllText(EnsureSharedFilePath(), entry + Environment.NewLine);
                Console.WriteLine(entry);
            }

            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }

    /// <summary>
    /// 创建一条新的日志.
    /// </summary>
    /// <param name="info">需要在日志中显示的具体信息.</param>
    /// <param name="errorFunction">当日志写入出现错误时,调用的函数,可为 null。
    /// 当type="ERROR"时会自动触发.</param>
    /// <param name="type">日志类型.</param>
    /// <returns>日志写入是否成功.</returns>
    public bool AddLogs(string info, Action? errorFunction, string type = "INFO")
    {
        try
        {
            lock (WriteLock)
            {
                File.AppendAllText(EnsureSharedFilePath(), $"[{TimeGet()}][{type}]{info}\n");
                Console.WriteLine($"[{TimeGet()}][{type}]{info}\n");
            }
            if (type == "ERROR")
            {
                errorFunction?.Invoke();
            }
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            errorFunction?.Invoke();
            return false;
        }
    }

    /// <summary>
    /// 清空日志目录：删除其中的全部 .log 文件.
    /// </summary>
    /// <returns>成功删除的文件数量；目录不存在返回 0，发生异常时返回 -1.</returns>
    public static int ClearLogs()
    {
        try
        {
            var dir = GetLogDirectory();
            if (!Directory.Exists(dir))
                return 0;

            var deleted = 0;
            lock (WriteLock)
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.log"))
                {
                    try
                    {
                        File.Delete(file);
                        deleted++;
                    }
                    catch (Exception e)
                    {
                        // 单个文件被占用（如其他进程正在写入）时跳过，不影响其余文件
                        Console.WriteLine(e);
                    }
                }
            }
            return deleted;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return -1;
        }
    }
}
