using System;
using System.Collections.Generic;
using System.Text;

namespace NyaLauncher.Core
{
    /// <summary>
    /// 关于启动器的版本信息的存储位置
    /// </summary>
    readonly struct NyaLauncherInfo
    {
        public static int MainVersion { get; } = 0;
        public static double SubVersion { get; } = 0.1;
        public static double FixVersion { get; } = 0.0;
        public static double ExtensionVersion { get; } = 0.1;
        public static string VersionName { get; } = "Haiku";
    }
}
