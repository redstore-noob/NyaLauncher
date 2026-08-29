using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace NyaLauncher.Avalonia.Controls;

/// <summary>下载目标类型：决定可用选项与落盘子目录提示。</summary>
public enum DownloadTargetKind
{
    /// <summary>Mod：下载到实例 mods 目录 / 自定义保存路径。</summary>
    Mod,

    /// <summary>整合包：仅能新建独立实例（自定义名字作为版本 id）。</summary>
    Modpack,

    /// <summary>资源包：实例 resourcepacks 目录 / 自定义保存路径。</summary>
    Resourcepack,

    /// <summary>光影包：实例 shaderpacks 目录 / 自定义保存路径。</summary>
    Shaderpack
}

/// <summary>
/// 可复用的"下载到"选择组件：整合了实例下拉 / 自定义保存路径 / 新建独立实例命名，
/// 供 ModDownloadView / ContentDownloadView 共享，避免重复实现目标选择与提示逻辑。
/// 使用前必须先调用 <see cref="Setup"/> 初始化。
/// </summary>
public partial class DownloadTargetPicker : UserControl
{
    public const string NewInstanceOption = "新建独立实例…";

    private DownloadTargetKind _kind;

    public DownloadTargetPicker()
    {
        InitializeComponent();
    }

    /// <summary>按目标类型构建选项并选中默认项。整合包默认展开命名框，其余隐藏。</summary>
    public void Setup(DownloadTargetKind kind)
    {
        _kind = kind;
        TargetNameBox.Text = string.Empty;

        if (kind == DownloadTargetKind.Modpack)
        {
            TargetComboBox.ItemsSource = new List<string> { NewInstanceOption };
            TargetComboBox.SelectedIndex = 0;
            TargetNameBox.IsVisible = true;
        }
        else
        {
            var items = InstanceTargetHelper.BuildItems();
            TargetComboBox.ItemsSource = items;
            InstanceTargetHelper.SelectDefault(TargetComboBox, items);
            TargetNameBox.IsVisible = false;
        }

        UpdateTargetHint();
    }

    /// <summary>当前选中的目标项（实例标签 / 自定义路径 / 新建实例）。</summary>
    public string? SelectedTarget => TargetComboBox.SelectedItem as string;

    /// <summary>是否选择了"新建独立实例"。</summary>
    public bool IsNewInstance => SelectedTarget == NewInstanceOption;

    /// <summary>是否选择了"自定义保存路径"。</summary>
    public bool IsCustomPath => SelectedTarget == InstanceTargetHelper.CustomPathOption;

    /// <summary>新建实例的名字（可能为空，调用方需校验）。</summary>
    public string InstanceName => TargetNameBox.Text?.Trim() ?? string.Empty;

    /// <summary>从选中项解析的真实实例 id；自定义路径 / 空返回空串。</summary>
    public string SelectedInstanceId => InstanceTargetHelper.GetSelectedInstanceId(TargetComboBox);

    /// <summary>解析选中实例的内容目录；不可用时返回空串。</summary>
    public string ResolveContentDir() => InstanceTargetHelper.ResolveContentDir(SelectedInstanceId);

    /// <summary>当前类型对应的落盘子目录（mods / resourcepacks / shaderpacks / 整合包解压安装）。</summary>
    public string SubDirectory => _kind switch
    {
        DownloadTargetKind.Mod => "mods",
        DownloadTargetKind.Resourcepack => "resourcepacks",
        DownloadTargetKind.Shaderpack => "shaderpacks",
        _ => "整合包解压安装"
    };

    /// <summary>校验自定义实例名：拒绝空、"."/".."、非法文件名字符与路径分隔符（防路径穿越）。</summary>
    public static bool IsValidInstanceName(string name, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "请输入新实例的名字。";
            return false;
        }
        if (name is "." or ".." ||
            name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('/', StringComparison.Ordinal) ||
            name.Contains('\\', StringComparison.Ordinal))
        {
            error = "实例名包含不安全字符，请换一个名字。";
            return false;
        }
        return true;
    }

    private void OnTargetSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selection = SelectedTarget;
        var isNewInstance = selection == NewInstanceOption;
        TargetNameBox.IsVisible = isNewInstance;
        if (!isNewInstance)
            TargetNameBox.Text = string.Empty;
        UpdateTargetHint();
    }

    private void UpdateTargetHint()
    {
        var selection = SelectedTarget;
        if (string.IsNullOrEmpty(selection))
        {
            TargetHint.Text = "选择要下载到的目标";
            return;
        }
        if (selection == InstanceTargetHelper.CustomPathOption)
        {
            TargetHint.Text = "下载时将弹出文件保存对话框";
            return;
        }
        if (selection == NewInstanceOption)
        {
            var name = InstanceName;
            TargetHint.Text = string.IsNullOrEmpty(name)
                ? "将新建一个独立实例，并在下方输入它的名字"
                : $"将新建独立实例「{name}」并自动安装整合包所需的游戏版本与加载器";
            return;
        }

        var contentDir = ResolveContentDir();
        TargetHint.Text = string.IsNullOrWhiteSpace(contentDir)
            ? "实例内容目录不可用"
            : $"将放入 {contentDir}（{SubDirectory}）";
    }
}
