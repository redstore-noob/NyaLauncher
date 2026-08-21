# 关于 NyaLauncher.Core 新修改/新功能的汇总
========
**所有原有的类/接口未修改，仅优化了代码结构，同时新增了部分功能，对原有功能基本无影响，能正常使用原有方法。**
<br>

- 新增 NyaLauncherInfo.cs，存储启动器的基本数据（当前版本号等），启动器迭代时修改数据等更便捷
- Launcher 子模块下，合并了大部分文件（除 JavaRuntimeLocator.cs 保留），LaunchInfo.cs 主要存储各种接口，LaunchTool.cs 主要存储各种启动 Minecraft 时需要进行的处理的方法，其他不变
- ManifestGet.cs 中 `GetVersionsAsync()` 方法新增了 url 参数，用于自定义获取 Minecraft 版本的地址，默认为 Mojang 官方源，且强制 HTTPS 校验

```csharp
public static async Task<List<MinecraftVersion>> GetVersionsAsync(string url = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
```

## 新增共享工具

- `PathUtil.cs` — 路径比较、共享 HttpClient、JSON 字符串安全读取、平台路径比较器
- `PngEncoder.cs` — 共享 PNG 编码工具
- `MinecraftRuleEvaluator.CreateDefaultFeatures()` — 标准 Minecraft 特性字典工厂，替代各处重复构建

## 重构要点

- 全项目路径相等判断统一走 `PathUtil.PathsEqual()`，各页面不再各自声明包装方法
- `TryGetString` / `GetPathComparer` 提取到 `PathUtil` 共享
- `ConfigFileManage` → `ConfigFileManager`（类名与文件名同步更正）
- 静态 HttpClient 统一为 `PathUtil.SharedHttpClient`（ManifestGet、ModrinthSearch 共用）
- `MicrosoftDeviceCodeAuthenticator` 实现 `IDisposable`，正确释放内部 HttpClient
- DownloadPage 搜索事件处理器增加异常保护
- 动画 RippleBehavior 的 Border 清理加入 try/finally
