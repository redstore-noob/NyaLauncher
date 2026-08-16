using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using NyaLauncher.Avalonia.Framework;

namespace NyaLauncher.Avalonia.Pages;

/// <summary>
/// 皮肤/披风编辑的结果。<see cref="Success"/> 为 true 时表示操作已经完成，
/// 否则 <see cref="Message"/> 给出给用户的提示文字。
/// </summary>
internal readonly record struct AppearanceEditResult(bool Success, string Message)
{
    public static AppearanceEditResult Ok(string message) => new(true, message);

    public static AppearanceEditResult Cancelled(string message) => new(false, message);
}

/// <summary>
/// 正版账号皮肤/披风的共享编辑流程（文件选择 → 校验 → 模型选择 → 上传）。
/// 主窗口的组件回调与账户管理页面都复用这一实现，避免重复造轮子。
/// </summary>
internal static class MinecraftAppearanceEditor
{
    /// <summary>
    /// 为指定的正版账号更换皮肤：弹出文件选择器 → 校验 PNG → 选择模型 → 上传到 Minecraft 服务。
    /// 返回用户可直接展示的结果文案。
    /// </summary>
    public static async Task<AppearanceEditResult> ChangeSkinAsync(
        Window owner,
        MinecraftProfileService service,
        LaunchAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(account);

        // 选择本地 PNG 皮肤文件
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Minecraft Java 皮肤",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Minecraft 皮肤 PNG")
                {
                    Patterns = ["*.png"],
                    MimeTypes = ["image/png"]
                }
            ]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return AppearanceEditResult.Cancelled("未选择皮肤文件。");

        // 校验（含 PNG 解码）放到后台线程，避免大图或异常文件卡住 UI 线程
        try
        {
            await Task.Run(() => ValidateSkinFile(path), cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return AppearanceEditResult.Cancelled(exception.Message);
        }

        // 让用户选择 Steve（经典）或 Alex（纤细）手臂模型
        var modelDialog = new SkinModelDialog();
        var model = await modelDialog.ShowDialog<MinecraftSkinModel?>(owner);
        if (model is null)
            return AppearanceEditResult.Cancelled("未选择皮肤模型。");

        await service.UploadSkinAsync(account, path, model.Value, cancellationToken);
        return AppearanceEditResult.Ok("正版皮肤已更新。");
    }

    /// <summary>
    /// 为指定的正版账号更换披风：读取已有披风列表 → 选择 → 设置当前披风（或停用）。
    /// 返回用户可直接展示的结果文案。
    /// </summary>
    public static async Task<AppearanceEditResult> ChangeCapeAsync(
        Window owner,
        MinecraftProfileService service,
        LaunchAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(account);

        var profile = await service.GetProfileAsync(account, cancellationToken);
        var dialog = new CapeSelectionDialog(profile);
        var selection = await dialog.ShowDialog<CapeSelectionResult?>(owner);
        if (selection is null)
            return AppearanceEditResult.Cancelled("已取消披风选择");

        await service.SetActiveCapeAsync(account, selection.CapeId, cancellationToken);
        return selection.CapeId is null
            ? AppearanceEditResult.Ok("已停用披风。")
            : AppearanceEditResult.Ok("正版披风已更新。");
    }

    /// <summary>
    /// 校验皮肤文件：必须是本地 PNG，大小不超过 4 MiB，且尺寸为 64×64 或兼容的 64×32。
    /// 校验失败时抛出 <see cref="InvalidDataException"/>。
    /// </summary>
    public static void ValidateSkinFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || !string.Equals(file.Extension, ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("皮肤文件必须是本地 PNG 图片。");
        if (file.Length <= 0 || file.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("皮肤文件为空或超过 4 MiB 限制。");

        using var bitmap = new Bitmap(file.FullName);
        if (bitmap.PixelSize.Width != 64 || bitmap.PixelSize.Height is not (32 or 64))
        {
            throw new InvalidDataException(
                "Minecraft Java 皮肤尺寸必须为 64×64，或兼容旧版的 64×32。");
        }
    }
}
