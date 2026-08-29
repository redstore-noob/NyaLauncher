# 07 · 通知框架 NyaNotice

> 全局通知框架，包含 **警示条（NyaAlert）** 与 **弹窗提示（NyaPrompt）** 两套入口。
> 统一命名空间：`NyaLauncher.Avalonia.Framework`

```csharp
using NyaLauncher.Avalonia.Framework;
```

两个宿主已由 `MainWindow` 自动挂载（`NyaAlertHost` ZIndex 940 / `NyaPromptHost` ZIndex 950），
**无需手动注册**，直接调用静态门面即可。

所有 API 均可在**任意线程**调用（内部自动封送 UI 线程）。

---

## 1. 级别枚举 `NyaNoticeSeverity`

决定提示的图标与主题色（颜色经 `DynamicResource` 绑定，主题切换实时跟随）。

| 值 | 图标 | 主题色键 |
|---|---|---|
| `Info` | Info | `InfoBrush` |
| `Success` | CheckCircle | `SuccessBrush` |
| `Warning` | Warning | `WarningBrush` |
| `Error` | Error | `ErrorBrush` |

---

## 2. 警示条 `NyaAlert`

底部左侧滑入的小滑条。自动收回（默认 **4 秒**），点关闭按钮立即收回。

**新警示顶掉旧警示**（就地换文案并重置倒计时，不重播动画）。

### 2.1 便捷方法

| 方法 | 说明 |
|---|---|
| `NyaAlert.Info(string message, TimeSpan? duration = null)` | 信息提示 |
| `NyaAlert.Success(string message, TimeSpan? duration = null)` | 成功提示 |
| `NyaAlert.Warning(string message, TimeSpan? duration = null)` | 警告提示 |
| `NyaAlert.Error(string message, TimeSpan? duration = null)` | 错误提示 |

`duration` 省略时使用 `NyaAlert.DefaultDuration`（4 秒）。

### 2.2 通用方法

| 方法 | 说明 |
|---|---|
| `NyaAlert.Show(string message, NyaNoticeSeverity severity = Info, TimeSpan? duration = null)` | 自定义级别的通用展示 |

### 2.3 常量

| 成员 | 值 | 说明 |
|---|---|---|
| `NyaAlert.DefaultDuration` | `TimeSpan.FromSeconds(4)` | 默认停留时长 |

### 2.4 示例

```csharp
NyaAlert.Success("实例创建完成");

NyaAlert.Error("网络请求失败", TimeSpan.FromSeconds(8));  // 自定义停留时长

NyaAlert.Show("正在同步……", NyaNoticeSeverity.Warning);
```

---

## 3. 弹窗提示 `NyaPrompt`

Material 风居中对话框。嵌入主界面，居中卡片 + 遮罩，PopIn/PopOut 动效
（M3 令牌，尊重 `AnimationGate`）。

### 3.1 方法

| 方法 | 返回 | 说明 |
|---|---|---|
| `NyaPrompt.Show(string title, string message, NyaNoticeSeverity severity = Info, params NyaPromptButton[] buttons)` | `void` | 展示，不等待结果 |
| `NyaPrompt.ShowAsync(string title, string message, NyaNoticeSeverity severity = Info, params NyaPromptButton[] buttons)` | `Task<string?>` | 展示并等待点击的按钮 Id |
| `NyaPrompt.ConfirmAsync(string title, string message, string confirm = "确定", string cancel = "取消", NyaNoticeSeverity severity = Warning)` | `Task<bool>` | 确认对话框，是否点了确认 |

### 3.2 返回值约定

- `ShowAsync` 返回用户点击按钮的 **`ResolvedId`**（即 `Id`，未传 `Id` 时就是 `Label` 文字）
- **不传按钮**时显示单个「好的」按钮
- **宿主缺失**或**被新提示顶掉**时返回 `null`（旧等待立即完成，避免泄漏）
- `ConfirmAsync` 内部把按钮 Id 定为 `"cancel"` / `"confirm"`，返回 `true` 当且仅当点了确认

### 3.3 按钮 `NyaPromptButton`

```csharp
public sealed record NyaPromptButton(string Label, string? Id = null, bool IsDefault = false)
```

| 参数 | 说明 |
|---|---|
| `Label` | 按钮显示文字（必填） |
| `Id` | 可选；作为 `ShowAsync` 的返回值，省略时用 `Label` |
| `IsDefault` | 默认按钮（视觉强调样式） |

### 3.4 示例

```csharp
// 单按钮提示（不等待）
NyaPrompt.Show("已保存", "配置已写入 config.json");

// 确认对话框
var ok = await NyaPrompt.ConfirmAsync("删除实例", "该操作不可撤销");
if (ok)
{
    // 执行删除……
}

// 多按钮选择，等待返回点击的按钮 Id
var id = await NyaPrompt.ShowAsync(
    "选择操作",
    "请选择要执行的操作",
    NyaNoticeSeverity.Info,
    new NyaPromptButton("复制路径", "copy"),
    new NyaPromptButton("打开文件夹", "open"),
    new NyaPromptButton("取消", "cancel", IsDefault: true));

if (id == "copy") { /* …… */ }
```

---

## 4. 行为细节（共同点）

| 方面 | 说明 |
|------|------|
| **动画** | 均基于 Avalonia Transitions（渲染线程驱动），使用 Material Design 3 令牌；`AnimationGate.Enabled` 为 `false` 时直接跳过动画 |
| **主题** | 级别色与卡片背景均走 `DynamicResource`，主题热重载时实时跟随 |
| **线程安全** | 任意线程调用；内部用 `Dispatcher` 封送，无需手动 `InvokeAsync` |
| **宿主生命周期** | `NyaAlert.Register` / `NyaPrompt.Register` 由宿主构造函数自动调用，**调用方不要手动注册** |

---

## 5. 插件中的使用建议

- **短反馈用 `NyaAlert`**："已复制路径"、"下载已开始" 这类一闪而过的信息
- **需要用户决策用 `NyaPrompt`**：删除确认、操作多选
- **必须等待结果时用 `ShowAsync` / `ConfirmAsync`**，不要用 `Show` 然后去猜结果
- **总是处理 `null`**：`ShowAsync` 在宿主缺失或被新提示顶掉时返回 `null`
- 在组件动作里调用是安全的——动作运行在后台线程，门面会自动封送到 UI 线程

```csharp
// 在 Polygon 组件的动作里使用
public override async ValueTask<ComponentActionResult> InvokeAsync(
    ComponentActionInvocation invocation, CancellationToken cancellationToken)
{
    var ok = await NyaPrompt.ConfirmAsync("重置", "确定要重置进度吗？");
    if (!ok)
        return ComponentActionResult.Failed("已取消。");

    Reset();
    NyaAlert.Success("进度已重置");
    return ComponentActionResult.Completed();
}
```
