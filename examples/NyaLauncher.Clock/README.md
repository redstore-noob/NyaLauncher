# NyaLauncher 电子时钟示例插件

这是一个严格使用 `NyaLauncher.Plugin.Abstractions` API v1 的最小动态组件示例。它不引用
Avalonia 或启动器内部类型，只通过声明式 Polygon 组件和宿主管理的全局设置工作。

## 功能与布局

- 中央大字只显示时和分，占据组件绝大部分面积。
- 时区位于顶端的小区域，可在插件设置中隐藏。
- 秒位于右下角的小区域，可在插件设置中隐藏。
- 12 小时制的 AM/PM 位于左下角；24 小时制下自动隐藏。
- 设置保存后，所有已创建的时钟实例会立即刷新。

设置键是稳定接口：`time.format`、`display.timezone` 和 `display.seconds`。组件 ID 是
`io.github.touristh.clock/digital-clock`，后续版本不应改变这些 ID 或设置语义。

## 构建和测试

在 NyaLauncher 仓库根目录运行：

```powershell
dotnet build .\examples\NyaLauncher.Clock\NyaLauncher.Clock.csproj -c Release
dotnet run --project .\examples\NyaLauncher.Clock.Tests\NyaLauncher.Clock.Tests.csproj -c Release
```

生成符合插件仓库要求的 ZIP：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\examples\NyaLauncher.Clock\package.ps1
```

脚本输出精确字节数和小写 SHA-256。ZIP 根目录直接包含 `plugin.json`、
`NyaLauncher.Clock.dll`、`README.md` 和 `LICENSE`，并且不会包含宿主提供的
`NyaLauncher.Plugin.Abstractions.dll`。

## 生命周期说明

每个可视组件实例持有自己的刷新任务和设置事件订阅。`DisposeAsync` 会先解除订阅，随后取消并
等待刷新任务，最后释放同步对象；插件禁用或工作区关闭时不会遗留定时器或静态引用。状态变化始终
发布完整且不可再修改的 `ComponentStateSnapshot`。

## 许可证

MIT，见 [LICENSE](LICENSE)。
