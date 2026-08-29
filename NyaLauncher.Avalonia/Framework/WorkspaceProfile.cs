using System.Collections.Generic;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 工作区档案：主界面布局与个性化的完整持久化模型，对应 <c>workspace.json</c>。
/// <para>
/// 这里只保存<b>稳定的引用</b>：区域 Id、组件 Id、相对坐标、停靠树与权重。
/// 组件的定义、工厂、运行时实例与瞬时状态<b>不会</b>被序列化，
/// 因此升级启动器或插件后，只要 Id 不变，用户的布局就能继续沿用。
/// </para>
/// </summary>
public sealed class WorkspaceProfile
{
    /// <summary>当前档案格式版本。读取旧档案时由 <c>WorkspaceProfileMigrator</c> 逐级迁移到此版本。</summary>
    public const int CurrentVersion = 7;

    // 版本 0 预留给「JSON 早于版本字段存在、或缺失该字段」的档案。
    // 新建档案必须走当前档案工厂，那些工厂都会显式写入 CurrentVersion。
    /// <summary>
    /// 档案格式版本。
    /// <c>0</c> 表示这份 JSON 早于版本字段存在（或缺失该字段），需要迁移。
    /// </summary>
    public int Version { get; set; }

    /// <summary>全局组件缩放系数。恢复时会再钳制到注册表允许的区间。</summary>
    public double GlobalComponentScale { get; set; } = 1;

    /// <summary>各功能区的个性化偏好（显示名称、简介、图标、要显示哪些动作）。</summary>
    public List<FeatureAreaPreference> Areas { get; set; } = [];

    /// <summary>用户在个性化窗口中自建的区域；启动时先恢复它们，再套用 <see cref="Areas"/>。</summary>
    public List<UserFeatureAreaProfile> CustomAreas { get; set; } = [];

    /// <summary>
    /// 停靠树根节点。叶子节点带 <c>AreaId</c> 表示一个区域，
    /// 分支节点带 <c>Direction</c> 与子节点表示一次水平或垂直拆分。
    /// 为 <c>null</c> 时工作区回退到默认布局。
    /// </summary>
    public DockLayoutProfile? Layout { get; set; }

    /// <summary>当前停靠在工作区各外边缘的侧栏。</summary>
    public List<SidebarProfile> Sidebars { get; set; } = [];

    /// <summary>每个已摆放组件的位置信息。</summary>
    public List<ComponentPlacementProfile> ComponentPlacements { get; set; } = [];
}

/// <summary>
/// 一个组件在某个功能区内的摆放位置。
/// 坐标是相对该区域工作区的<b>归一化比例</b>（0~1），因此区域缩放后布局比例保持不变。
/// </summary>
public sealed class ComponentPlacementProfile
{
    /// <summary>组件所在的功能区 Id。</summary>
    public string AreaId { get; set; } = string.Empty;

    /// <summary>组件（动作）Id，引用全局目录。</summary>
    public string ComponentId { get; set; } = string.Empty;

    /// <summary>横向位置，归一化比例，默认居中。</summary>
    public double RelativeX { get; set; } = 0.5;

    /// <summary>纵向位置，归一化比例，默认居中。</summary>
    public double RelativeY { get; set; } = 0.5;

    /// <summary>同一区域内的叠放层级，数值越大越靠上。</summary>
    public int ZIndex { get; set; }
}

/// <summary>
/// 用户在个性化窗口中自建的功能区。使用不表达业务含义的中性编号（如 <c>area-004</c>）作为 Id。
/// </summary>
public sealed class UserFeatureAreaProfile
{
    /// <summary>区域 Id，如 <c>area-004</c>。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>区域显示名称。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>区域副标题。</summary>
    public string Subtitle { get; set; } = "用户创建的功能区";

    /// <summary>区域图标字符；支持 Material 前缀与 Emoji。</summary>
    public string Glyph { get; set; } = "material:Apps";

    /// <summary>可选的本地图标图片路径；图片失效时回退到 <see cref="Glyph"/>。</summary>
    public string? IconPath { get; set; }
}

/// <summary>
/// 一个折叠在窗口边缘的侧栏：记录它属于哪个区域、贴哪条边、展开时恢复到多大。
/// </summary>
public sealed class SidebarProfile
{
    /// <summary>被折叠为侧栏的区域 Id。</summary>
    public string AreaId { get; set; } = string.Empty;

    /// <summary>侧栏停靠在窗口的哪条外边缘。</summary>
    public DockEdge Edge { get; set; }

    /// <summary>展开时的目标尺寸（像素）。</summary>
    public double RevealSize { get; set; }
}

/// <summary>工作区的四条外边缘，用于侧栏停靠。</summary>
public enum DockEdge
{
    /// <summary>左边缘。</summary>
    Left,

    /// <summary>右边缘。</summary>
    Right,

    /// <summary>上边缘。</summary>
    Top,

    /// <summary>下边缘。</summary>
    Bottom
}

/// <summary>
/// 停靠树节点。
/// <list type="bullet">
/// <item><description><b>叶子</b>：<see cref="AreaId"/> 非空，表示一个实际的功能区。</description></item>
/// <item><description><b>分支</b>：<see cref="Direction"/> 非空，<see cref="Children"/> 为子区域，
/// <see cref="Weights"/> 给出每个子区域的尺寸权重。</description></item>
/// </list>
/// </summary>
public sealed class DockLayoutProfile
{
    /// <summary>叶子节点对应的功能区 Id；分支节点为 <c>null</c>。</summary>
    public string? AreaId { get; set; }

    /// <summary>分支节点的拆分方向；叶子节点为 <c>null</c>。</summary>
    public DockSplitDirection? Direction { get; set; }

    /// <summary>分支节点的子区域列表。</summary>
    public List<DockLayoutProfile> Children { get; set; } = [];

    /// <summary>
    /// 分支节点中每个子区域的尺寸权重，与 <see cref="Children"/> 一一对应。
    /// 数量不匹配时由布局系统按均分处理。
    /// </summary>
    public List<double> Weights { get; set; } = [];
}

/// <summary>停靠节点的拆分方向。</summary>
public enum DockSplitDirection
{
    /// <summary>水平拆分：子区域左右排列。</summary>
    Horizontal,

    /// <summary>垂直拆分：子区域上下排列。</summary>
    Vertical
}
