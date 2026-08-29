using System.Collections.Generic;

namespace NyaLauncher.Avalonia.Framework;

/// <summary>
/// 功能区提供者：一个插件要一次性注册多个区域时实现此接口，
/// 再用 <c>FeatureAreaRegistry.Register(IFeatureAreaProvider)</c> 挂载。
/// </summary>
public interface IFeatureAreaProvider
{
    /// <summary>
    /// 返回本提供者提供的全部功能区定义。
    /// 每次调用都应返回等价的结果——注册表会把返回的区域逐个注册，
    /// 区域 Id 与动作 Id 同样受全局唯一约束。
    /// </summary>
    /// <returns>区域定义序列。</returns>
    IEnumerable<FeatureAreaDefinition> GetFeatureAreas();
}
