# 关于NyaLauncher.Core新修改/新功能的汇总
========
**所有原有的类/接口未修改，仅优化了代码结构，同时新增了部分功能，对原有功能基本无影响，能正常使用原有方法。**
<br>

- 新增NyaLauncherInfo.cs，存储启动器的基本数据（当前版本号等），启动器迭代时修改数据等更便捷
- Launcher子模块下，合并了大部分文件(除JavaRuntimeLocator.cs保留)，LauncherInfo.cs主要存储各种接口,LauncherTool.cs主要存储各种启动Minecraft时需要进行的处理的方法，其他不变
- ManifestGet.cs中`GetVersionsAsync()`方法与`GetVersionsByTypeAsync()`方法分别新增了url参数，用于自定义获取Minecraft版本的地址，默认为mojang官方源
具体如下：
```csharp
public static async Task<List<MinecraftVersion>> GetVersionsAsync(string url="\"https://piston-meta.mojang.com/mc/game/version_manifest_v2.json\"");
public static async Task<List<MinecraftVersion>> GetVersionsByTypeAsync(string type, string url="https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
```
在上面的两个函数定义中的url，即为Minecraft版本列表的获取地址