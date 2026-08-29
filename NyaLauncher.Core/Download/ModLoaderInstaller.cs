using System.Diagnostics;
using System.IO.Compression;
using NyaLauncher.Core.Launch;

namespace NyaLauncher.Core.Download;

/// <summary>
/// Mod Loader 安装器。在原版 Minecraft 已安装的基础上，叠加安装指定的 Mod Loader。
/// 核心原理：Loader 的版本 JSON 通过 <c>inheritsFrom</c> 继承原版，
/// 现有 <see cref="MinecraftVersionInstaller"/> 可直接处理此类 JSON。
/// </summary>
public sealed class ModLoaderInstaller
{
    private readonly MinecraftVersionInstaller _baseInstaller = new();

    /// <summary>
    /// 安装指定 Mod Loader 到 Minecraft 目录。
    /// </summary>
    /// <param name="loader">要安装的 Loader 版本信息（含元数据 URL）。</param>
    /// <param name="instanceName">
    /// 实例名称，用作 <c>versions/</c> 下的文件夹名。
    /// 如 "fabric-loader-0.16.14-1.21.8" 或用户自定义名称。
    /// </param>
    /// <param name="minecraftDirectory">Minecraft 根目录。</param>
    /// <param name="minecraftVersion">原版 Minecraft 版本号，用于确保原版已安装。</param>
    /// <param name="progress">进度回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task InstallAsync(
        ModLoaderVersion loader,
        string instanceName,
        string minecraftDirectory,
        string minecraftVersion,
        IProgress<MinecraftInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(minecraftDirectory));

        // 1. 确保原版 Minecraft 已安装（Loader 的 inheritsFrom 需要原版文件）
        await EnsureVanillaInstalledAsync(root, minecraftVersion, cancellationToken)
            .ConfigureAwait(false);

        if (loader.RequiresInstallerExtraction)
        {
            // NeoForge / Forge：需要从安装器 JAR 中提取版本 JSON
            await InstallFromInstallerJarAsync(
                    loader, instanceName, root, minecraftVersion, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            // Fabric：版本 JSON 可直接从 API 获取
            await _baseInstaller.InstallAsync(
                    instanceName,
                    loader.MetadataUrl,
                    root,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 生成默认的实例名称。
    /// </summary>
    public static string CreateDefaultInstanceName(
        ModLoaderType type,
        string loaderVersion,
        string minecraftVersion) => type switch
    {
        ModLoaderType.Fabric => $"fabric-loader-{loaderVersion}-{minecraftVersion}",
        ModLoaderType.NeoForge => $"neoforge-{loaderVersion}-{minecraftVersion}",
        ModLoaderType.Forge => $"forge-{loaderVersion}",
        _ => minecraftVersion
    };

    /// <summary>
    /// 检查原版 Minecraft 是否已安装；未安装时使用 Mojang 官方源下载。
    /// 作为依赖安装时静默执行，不报告进度。
    /// </summary>
    private async Task EnsureVanillaInstalledAsync(
        string root,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        var versionJsonPath = Path.Combine(
            root, "versions", minecraftVersion, $"{minecraftVersion}.json");

        if (File.Exists(versionJsonPath))
            return;

        // 原版未安装，从 Mojang 版本清单获取元数据 URL
        var versions = await ManifestGet.GetVersionsAsync()
            .ConfigureAwait(false);
        var vanilla = versions.FirstOrDefault(v =>
            string.Equals(v.Id, minecraftVersion, StringComparison.OrdinalIgnoreCase));

        if (vanilla is null || string.IsNullOrWhiteSpace(vanilla.Url))
            throw new InvalidOperationException(
                $"无法从 Mojang 版本清单中找到 Minecraft {minecraftVersion}。");

        // 作为依赖静默下载，不向用户报告进度
        await _baseInstaller.InstallAsync(
                minecraftVersion,
                vanilla.Url,
                root,
                progress: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 从安装器 JAR 安装 NeoForge / Forge。
    /// <para>
    /// 优先直接运行安装器（<c>java -jar installer.jar --installClient &lt;目录&gt;</c>；
    /// 旧版安装器用 <c>--install-client</c>，由 TryRunInstaller 自动探测切换）：
    /// NeoForge / Forge 的 SRG 重映射客户端（libraries/net/minecraft/client/...-srg.jar）
    /// 只由安装器生成，任何 Maven 源都没有该文件；只提取 version.json 会导致
    /// 启动时报 "NeoForge installation is corrupted"。
    /// </para>
    /// <para>
    /// 运行安装器需要本机 Java（走 JavaRuntimeLocator 全链查找）；失败时回退到
    /// 旧的"提取 version.json + maven 库"逻辑尽力安装。
    /// </para>
    /// </summary>
    private async Task InstallFromInstallerJarAsync(
        ModLoaderVersion loader,
        string instanceName,
        string root,
        string minecraftVersion,
        IProgress<MinecraftInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        // 1. 下载安装器 JAR 到临时文件
        var tempJar = Path.Combine(Path.GetTempPath(), $"nyalauncher-installer-{Guid.NewGuid():N}.jar");
        try
        {
            var jarBytes = await DownloadSourceProvider.GetBytesAsync(
                    loader.MetadataUrl, TimeSpan.FromMinutes(2), cancellationToken)
                .ConfigureAwait(false);
            await File.WriteAllBytesAsync(tempJar, jarBytes, cancellationToken)
                .ConfigureAwait(false);

            // 快照 versions 目录已有目录（安装器运行前的基线）。
            // 安装器成功运行后，只处理基线之外新增的目录，绝不触碰任何已有实例目录。
            var versionsDir = Path.Combine(root, "versions");
            var preExistingDirs = Directory.Exists(versionsDir)
                ? new HashSet<string>(
                    Directory.GetDirectories(versionsDir),
                    StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 2. 优先运行安装器：生成 srg 客户端等核心产物
            if (TryRunInstaller(tempJar, root, progress, cancellationToken, out var installerError))
            {
                // 校验安装器确实生成了运行时客户端产物；缺则说明安装不完整。
                // NeoForge 26.x（NeoForgeV1）产出 minecraft-client-patched.jar；
                // Forge 老架构（MCP）产出 client-*-srg.jar。
                if (!HasRuntimeClientArtifact(root, loader.Type, loader.LoaderVersion))
                {
                    throw new InvalidOperationException(
                        $"{loader.DisplayName} 安装器运行结束，但未生成必需的运行时客户端产物" +
                        "（NeoForge: libraries/net/neoforged/minecraft-client-patched/*.jar；" +
                        "Forge: libraries/net/minecraft/client/*-srg.jar）。" +
                        "请重试安装，或检查安装器输出确认 Java 版本与网络。" +
                        (string.IsNullOrWhiteSpace(installerError) ? string.Empty : $"\n{installerError}"));
                }

                // 安装器成功后会生成 versions/{id}/ 与所需 libraries；
                // 将安装器默认版本名对齐到用户指定的实例名（仅处理新增目录，绝不碰已有实例）
                AlignInstanceDirectory(root, minecraftVersion, instanceName, preExistingDirs);
                return;
            }

            // 3. 安装器运行失败：提取式安装永远无法生成 SRG 客户端等核心产物，
            // 装出来的版本启动必报 "installation corrupted"，因此直接报错并带上真实原因。
            throw new InvalidOperationException(
                $"Loader 安装器运行失败：{installerError ?? "未知错误"}" +
                "。NeoForge/Forge 必须由安装器完成安装（需要生成 SRG 客户端库），请检查 Java 与网络后重试。");
        }
        finally
        {
            try { File.Delete(tempJar); } catch { /* 清理临时文件 */ }
        }
    }

    /// <summary>
    /// 检查安装器是否生成了该 Loader 架构对应的运行时客户端产物：
    /// NeoForge 26.x（NeoForgeV1）→ minecraft-client-patched-{version}.jar；
    /// Forge 老架构（MCP）→ libraries/net/minecraft/client/*-srg.jar。
    /// </summary>
    private static bool HasRuntimeClientArtifact(
        string minecraftRoot,
        ModLoaderType loaderType,
        string loaderVersion)
    {
        try
        {
            if (loaderType == ModLoaderType.NeoForge)
            {
                if (string.IsNullOrWhiteSpace(loaderVersion))
                    return false;
                var patchedJar = Path.Combine(
                    minecraftRoot, "libraries", "net", "neoforged", "minecraft-client-patched",
                    loaderVersion, $"minecraft-client-patched-{loaderVersion}.jar");
                return File.Exists(patchedJar);
            }

            // Forge（MCP 架构）需要 SRG 重映射客户端
            return HasSrgClientJar(minecraftRoot);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>检查 SRG 客户端库（*-srg.jar）是否已生成到 libraries/net/minecraft/client。</summary>
    private static bool HasSrgClientJar(string minecraftRoot)
    {
        try
        {
            var clientLibraries = Path.Combine(minecraftRoot, "libraries", "net", "minecraft", "client");
            return Directory.Exists(clientLibraries) &&
                   Directory.EnumerateFiles(
                       clientLibraries, "*-srg.jar", SearchOption.AllDirectories)
                       .Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 把安装器生成的版本目录对齐到用户指定的实例名：
    /// 安装器固定使用 version.json 的 id（如 "neoforge-26.2.0.66"）作为目录名，
    /// 与启动器的自定义实例名（如 "neoforge-26.2.0.66-26.2"）不一致。
    /// 这里重命名目录并同步更新版本 JSON 的 id 字段。
    /// </summary>
    private static void AlignInstanceDirectory(
        string root,
        string minecraftVersion,
        string instanceName,
        HashSet<string> preExistingDirs)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
            return;

        var versionsDir = Path.Combine(root, "versions");
        if (!Directory.Exists(versionsDir))
            return;

        foreach (var directory in Directory.GetDirectories(versionsDir))
        {
            var name = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (string.Equals(name, minecraftVersion, StringComparison.OrdinalIgnoreCase))
                continue; // 原版目录，跳过
            if (string.Equals(name, instanceName, StringComparison.OrdinalIgnoreCase))
                continue; // 实例目录本身（重装场景），跳过，不打断遍历
            // 关键防线：只处理安装器本次新增的目录，绝不触碰任何安装器运行前已存在的目录
            // （已有实例目录可能含用户 mods/saves/config 等数据）。
            if (preExistingDirs.Contains(directory))
                continue;
            if (!name.Contains("neoforge", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("forge", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = Path.Combine(versionsDir, instanceName);
            if (Directory.Exists(target))
            {
                // 重装场景：实例目录已存在（内含用户 mods/saves/config，绝不能移动或删除），
                // 只把安装器新生成的版本 JSON（及可能的新 client jar）合并进现有实例目录。
                TryMergeInstallerJson(target, directory, instanceName);
                return;
            }

            try
            {
                Directory.Move(directory, target);
                // 版本 JSON 的 id 字段同步更新，并重命名 json 文件
                var newJson = Path.Combine(target, $"{instanceName}.json");
                var oldJson = Path.Combine(target, $"{name}.json");
                if (File.Exists(oldJson))
                {
                    UpdateJsonId(oldJson, newJson, instanceName);
                }
                return;
            }
            catch
            {
                // 改名失败则保留安装器默认目录名（实例仍可识别）
            }
        }
    }

    /// <summary>
    /// 重装场景：把安装器新生成目录中的版本 JSON 合并进已存在的实例目录，
    /// 不移动用户数据；随后清理安装器生成的空壳目录。
    /// </summary>
    private static void TryMergeInstallerJson(
        string targetDir,
        string sourceDir,
        string instanceName)
    {
        try
        {
            var sourceJson = Directory.GetFiles(sourceDir, "*.json").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(sourceJson))
                return;

            // 新 JSON 写入实例目录并更新 id
            var targetJson = Path.Combine(targetDir, $"{instanceName}.json");
            UpdateJsonId(sourceJson, targetJson, instanceName);

            // 新 JSON 可能引用了版本目录内同名 client jar，一并补齐
            var sourceJar = Path.Combine(
                sourceDir, $"{Path.GetFileNameWithoutExtension(sourceJson)}.jar");
            var targetJar = Path.Combine(targetDir, $"{instanceName}.jar");
            if (File.Exists(sourceJar) && !File.Exists(targetJar))
            {
                File.Copy(sourceJar, targetJar);
            }

            // 清理安装器生成的目录（仅删除其中的 json/jar 产物，不动其它内容；失败无害）
            foreach (var file in Directory.EnumerateFiles(sourceDir))
            {
                var lower = file.ToLowerInvariant();
                if (lower.EndsWith(".json") || lower.EndsWith(".jar"))
                {
                    try { File.Delete(file); } catch { /* 忽略 */ }
                }
            }
            try
            {
                if (!Directory.EnumerateFileSystemEntries(sourceDir).Any())
                    Directory.Delete(sourceDir);
            }
            catch { /* 清理失败则保留，无害 */ }
        }
        catch
        {
            // 合并失败不影响已安装的 libraries 产物
        }
    }

    /// <summary>
    /// NeoForge / Forge 安装器强制要求 <c>.minecraft</c> 下存在 launcher_profiles.json
    /// （官方启动器才会生成该文件）；启动器不写此文件时安装器会直接拒绝：
    /// "There is no minecraft launcher profile ... you need to run the launcher first!"
    /// 这里补一个最小合法模板。
    /// </summary>
    private static void EnsureLauncherProfiles(string minecraftRoot)
    {
        try
        {
            var profilePath = Path.Combine(minecraftRoot, "launcher_profiles.json");
            if (File.Exists(profilePath))
                return;

            var template = """
            {
              "profiles": {},
              "selectedProfile": "(Default)",
              "clientToken": "nyalauncher",
              "authenticationDatabase": {}
            }
            """;
            File.WriteAllText(profilePath, template);
        }
        catch
        {
            // 写失败不阻塞安装流程（部分安装器版本可能不强制检查）
        }
    }

    /// <summary>读取版本 JSON，更新 id 字段并写入新文件。</summary>
    private static void UpdateJsonId(string sourceJson, string targetJson, string newId)
    {
        try
        {
            var text = File.ReadAllText(sourceJson);
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return;

            var node = System.Text.Json.Nodes.JsonNode.Parse(text)
                as System.Text.Json.Nodes.JsonObject;
            if (node is null)
                return;

            node["id"] = newId;
            File.WriteAllText(targetJson, node.ToJsonString());
            try { File.Delete(sourceJson); } catch { /* 旧 json 文件名清理失败可忽略 */ }
        }
        catch
        {
            // id 更新失败：保留原 json，目录名仍可用于识别
        }
    }

    /// <summary>
    /// 运行 Loader 安装器（java -jar installer.jar --install-client 目录）。
    /// 成功返回 true；java 缺失或安装器失败返回 false 并给出原因。
    /// </summary>
    private static bool TryRunInstaller(
        string installerJarPath,
        string minecraftRoot,
        IProgress<MinecraftInstallProgress>? progress,
        CancellationToken cancellationToken,
        out string error)
    {
        error = string.Empty;

        // 查找本机可用的 Java：
        // 1) 启动器托管的运行时目录（<mcDir>/runtime，递归扫描已下载的 JRE）
        // 2) 无托管时回退全链查找（JAVA_HOME / PATH 等）
        string? javaExecutable;
        try
        {
            var runtimeDirectory = JavaRuntimeInstaller.GetRuntimeDirectory();
            javaExecutable = new JavaRuntimeLocator().FindJavaExecutable(
                runtimeDirectory: runtimeDirectory);
            if (string.IsNullOrWhiteSpace(javaExecutable) || !File.Exists(javaExecutable))
            {
                javaExecutable = new JavaRuntimeLocator().FindJavaExecutable();
            }
        }
        catch
        {
            javaExecutable = null;
        }

        if (string.IsNullOrWhiteSpace(javaExecutable) || !File.Exists(javaExecutable))
        {
            error = "未找到可用的 Java 运行时，无法运行安装器。";
            return false;
        }

        EnsureLauncherProfiles(minecraftRoot);

        progress?.Report(new MinecraftInstallProgress(
            1,
            "运行 Loader 安装器",
            "正在运行安装器（首次需要下载依赖，请耐心等待）…",
            0, 0, 0, 0, 0));

        // 现代 NeoForge/Forge 安装器（joptsimple）改用 camelCase 的 --installClient；
        // 旧版安装器用 --install-client。先试新语法，遇到 "is not a recognized option"
        // 自动换组合重试（joptsimple 解析失败在下载前发生，重试代价低）。
        // 同时对 --mirror 做开关降级：个别新安装器移除了 --mirror 选项。
        var installArgForms = new[] { "--installClient", "--install-client" };
        string? lastError = null;
        foreach (var installArg in installArgForms)
        {
            foreach (var useMirror in new[] { true, false })
            {
                if (RunInstallerOnce(javaExecutable, installerJarPath, minecraftRoot,
                        installArg, useMirror, cancellationToken,
                        out var onceError, out var unrecognized))
                {
                    error = string.Empty;
                    return true;
                }
                lastError = onceError;
                // 仅当是 joptsimple 不可识别选项时才换组合；
                // 其它失败（下载失败 / 退出码非 0 / 超时）直接结束，避免无谓重跑。
                if (!unrecognized)
                {
                    error = onceError;
                    return false;
                }
            }
        }
        error = lastError ?? "安装器运行失败。";
        return false;
    }

    /// <summary>单次运行安装器；unrecognizedOption 标记是否因 joptsimple 不可识别选项而失败。</summary>
    private static bool RunInstallerOnce(
        string javaExecutable,
        string installerJarPath,
        string minecraftRoot,
        string installArg,
        bool useMirror,
        CancellationToken cancellationToken,
        out string error,
        out bool unrecognizedOption)
    {
        error = string.Empty;
        unrecognizedOption = false;

        var psi = new ProcessStartInfo
        {
            FileName = javaExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installerJarPath);
        psi.ArgumentList.Add(installArg);
        psi.ArgumentList.Add(minecraftRoot);

        if (useMirror)
        {
            AddMirrorArgument(psi, DownloadSourceProvider.Active?.Maven);
            if (DownloadSourceProvider.Fallback is not null &&
                !string.Equals(
                    DownloadSourceProvider.Fallback.Maven,
                    DownloadSourceProvider.Active?.Maven,
                    StringComparison.OrdinalIgnoreCase))
            {
                AddMirrorArgument(psi, DownloadSourceProvider.Fallback.Maven);
            }
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                error = "无法启动安装器进程。";
                return false;
            }

            // 异步读取输出，避免管道阻塞；仅保留尾部用于报错
            var outputTail = new System.Text.StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    lock (outputTail)
                    {
                        AppendTail(outputTail, e.Data);
                    }
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    lock (outputTail)
                    {
                        AppendTail(outputTail, e.Data);
                    }
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(TimeSpan.FromMinutes(10)))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                error = "安装器运行超时（10 分钟）。";
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new OperationCanceledException(cancellationToken);
            }

            if (process.ExitCode != 0)
            {
                lock (outputTail)
                {
                    var tail = outputTail.ToString().Trim();
                    error = tail.Length > 0
                        ? $"安装器退出码 {process.ExitCode}：{tail}"
                        : $"安装器退出码 {process.ExitCode}。";
                    // joptsimple 不可识别选项的特征串（如 "install-client is not a recognized option"）
                    unrecognizedOption = tail.Contains(
                        "is not a recognized option", StringComparison.OrdinalIgnoreCase);
                }
                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            error = $"运行安装器失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>为安装器追加 --mirror 参数（跳过官方默认 maven，避免重复）。</summary>
    private static void AddMirrorArgument(ProcessStartInfo psi, string? mavenBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(mavenBaseUrl))
            return;
        // 官方源无需镜像
        if (string.Equals(mavenBaseUrl, DownloadSources.Official.Maven, StringComparison.OrdinalIgnoreCase))
            return;
        if (string.Equals(mavenBaseUrl, "https://maven.neoforged.net/releases/", StringComparison.OrdinalIgnoreCase))
            return;

        psi.ArgumentList.Add("--mirror");
        psi.ArgumentList.Add(mavenBaseUrl.TrimEnd('/') + "/");
    }

    /// <summary>保留输出尾部（最近若干行），用于错误诊断。</summary>
    private static void AppendTail(System.Text.StringBuilder builder, string line)
    {
        const int maxLines = 12;
        var lines = new List<string>(
            builder.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        lines.Add(line);
        if (lines.Count > maxLines)
            lines.RemoveRange(0, lines.Count - maxLines);
        builder.Clear();
        builder.Append(string.Join('\n', lines));
    }

    /// <summary>
    /// 从安装器 JAR 的 maven/ 目录提取所有库文件到 Minecraft 的 libraries/ 目录。
    /// NeoForge/Forge 安装器 JAR 内含完整的 Maven 仓库结构，提取后可直接使用。
    /// </summary>
    private static void ExtractLibrariesFromJar(string jarPath, string minecraftRoot)
    {
        var librariesDir = Path.Combine(minecraftRoot, "libraries");

        using var archive = ZipFile.OpenRead(jarPath);
        foreach (var entry in archive.Entries)
        {
            // 只处理 maven/ 目录下的 .jar 文件（跳过校验文件和目录）
            if (!entry.FullName.StartsWith("maven/", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!entry.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                continue;
            if (entry.Length == 0)
                continue;

            // maven/net/neoforged/neoforge/21.8.54/neoforge-21.8.54-universal.jar
            // → libraries/net/neoforged/neoforge/21.8.54/neoforge-21.8.54-universal.jar
            var relativePath = entry.FullName["maven/".Length..];
            // 防路径穿越（Zip-Slip）：拒绝 .. 与绝对路径，确保目标始终在 libraries 目录内
            var targetPath = ResolveContainedPath(librariesDir, relativePath);
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDir))
                Directory.CreateDirectory(targetDir);

            // 如果目标文件已存在且大小一致，跳过
            if (File.Exists(targetPath) && new FileInfo(targetPath).Length == entry.Length)
                continue;

            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    /// <summary>
    /// 将安装器 JAR 内的相对路径解析到目标根目录内，阻止路径穿越与绝对路径写入。
    /// </summary>
    private static string ResolveContainedPath(string root, string relativePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var target = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!target.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                comparison))
        {
            throw new InvalidDataException($"安装器 JAR 包含不安全的解压路径：{relativePath}");
        }

        return target;
    }

    /// <summary>
    /// 从安装器 JAR（ZIP 格式）中提取版本 JSON 字符串。
    /// 优先查找 version.json，其次查找 install_profile.json 中的 versionInfo 字段。
    /// </summary>
    private static string? ExtractVersionJsonFromJar(string jarPath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(jarPath);

        // 优先查找 version.json（NeoForge 新版本、Forge 新版本均有此文件）
        var versionEntry = archive.GetEntry("version.json");
        if (versionEntry is not null)
        {
            using var stream = versionEntry.Open();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        // 回退：从 install_profile.json 的 versionInfo 字段提取
        var profileEntry = archive.GetEntry("install_profile.json");
        if (profileEntry is not null)
        {
            using var stream = profileEntry.Open();
            using var reader = new StreamReader(stream);
            var profileJson = reader.ReadToEnd();

            using var doc = System.Text.Json.JsonDocument.Parse(profileJson);
            if (doc.RootElement.TryGetProperty("versionInfo", out var versionInfo))
            {
                return versionInfo.GetRawText();
            }
        }

        return null;
    }
}
