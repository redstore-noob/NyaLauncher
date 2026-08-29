using Avalonia;
using System;
using System.IO;
using System.Threading.Tasks;

namespace NyaLauncher.Avalonia;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("Task", e.Exception);
            e.SetObserved();
        };

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    /// <summary>崩溃兜底：把未处理异常写入 %USERPROFILE%\NyaLauncher\Logs\crash-*.log，便于定位线上崩溃。</summary>
    internal static void WriteCrashLog(string source, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "NyaLauncher", "Logs");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"crash-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
            File.AppendAllText(file,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}][{source}]{(ex is null ? "未知异常" : ex.ToString())}\n");
        }
        catch
        {
            // 兜底日志本身失败则静默忽略，不影响程序
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
