namespace NyaLauncher.Core.Launch;

/// <summary>
/// Host-composed changes applied after vanilla metadata is resolved and before
/// the Java command is rendered. All paths in this contract must be absolute.
/// </summary>
public sealed class MinecraftLaunchTransform
{
    public IReadOnlyList<string> PrependClasspath { get; init; } = [];

    public IReadOnlyList<string> AppendClasspath { get; init; } = [];

    /// <summary>
    /// Replaces the complete vanilla classpath before exact replacements and
    /// removals are applied. Null preserves the vanilla classpath.
    /// </summary>
    public IReadOnlyList<string>? ReplaceClasspath { get; init; }

    public IReadOnlyList<MinecraftClasspathReplacement> ClasspathReplacements { get; init; } = [];

    /// <summary>Removes entries by exact, platform-aware path comparison.</summary>
    public IReadOnlyList<string> RemoveClasspath { get; init; } = [];

    public string? MainClassOverride { get; init; }

    public string? JavaExecutableOverride { get; init; }

    public string? WorkingDirectoryOverride { get; init; }

    /// <summary>Arguments inserted before all launcher and version JVM arguments.</summary>
    public IReadOnlyList<string> PrependJvmArguments { get; init; } = [];

    /// <summary>Arguments inserted after version JVM arguments but before the main class.</summary>
    public IReadOnlyList<string> AppendJvmArguments { get; init; } = [];

    /// <summary>Arguments inserted immediately after the main class.</summary>
    public IReadOnlyList<string> PrependGameArguments { get; init; } = [];

    /// <summary>Arguments inserted after all launcher and version game arguments.</summary>
    public IReadOnlyList<string> AppendGameArguments { get; init; } = [];

    /// <summary>A null value removes that variable from the Java child process.</summary>
    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } =
        new Dictionary<string, string?>();
}

/// <summary>Replaces one exact entry from the resolved vanilla classpath.</summary>
public sealed record MinecraftClasspathReplacement(
    string ExistingPath,
    string ReplacementPath);

/// <summary>
/// Normalizes a declarative transform into an immutable launch-time snapshot.
/// Keeping this validation beside the contract makes every launcher entry point
/// use identical path, conflict and null-handling rules.
/// </summary>
internal static class MinecraftLaunchTransformResolver
{
    public static ResolvedMinecraftLaunchTransform Resolve(
        MinecraftLaunchOptions options,
        string baseMainClass,
        string gameDirectory,
        IReadOnlyList<string> baseClasspath)
    {
        var transform = options.LaunchTransform ?? new MinecraftLaunchTransform();

        // Validate legacy argument extension points as part of the final plan.
        ValidateAndCopyArguments(options.AdditionalJvmArguments, nameof(options.AdditionalJvmArguments));
        ValidateAndCopyArguments(options.AdditionalGameArguments, nameof(options.AdditionalGameArguments));

        return new ResolvedMinecraftLaunchTransform(
            ResolveMainClass(baseMainClass, transform.MainClassOverride),
            ResolveWorkingDirectory(gameDirectory, transform.WorkingDirectoryOverride),
            ResolveJavaExecutableOverride(transform.JavaExecutableOverride),
            ApplyClasspathTransform(baseClasspath, transform),
            ValidateAndCopyArguments(transform.PrependJvmArguments, nameof(transform.PrependJvmArguments)),
            ValidateAndCopyArguments(transform.AppendJvmArguments, nameof(transform.AppendJvmArguments)),
            ValidateAndCopyArguments(transform.PrependGameArguments, nameof(transform.PrependGameArguments)),
            ValidateAndCopyArguments(transform.AppendGameArguments, nameof(transform.AppendGameArguments)),
            ResolveEnvironmentVariables(transform.EnvironmentVariables));
    }

    public static void ValidateFinalArguments(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] is null || arguments[index].Contains('\0'))
            {
                throw new MinecraftLaunchException(
                    $"The final Java command contains an invalid argument at index {index}.");
            }
        }
    }

    public static bool PathsEqual(string left, string right) =>
        GetPathComparer().Equals(left, right);

    private static IReadOnlyList<string> ApplyClasspathTransform(
        IReadOnlyList<string> baseClasspath,
        MinecraftLaunchTransform transform)
    {
        var comparer = GetPathComparer();
        var selectedBase = transform.ReplaceClasspath ?? baseClasspath;
        var original = selectedBase
            .Select(path => NormalizeExistingClasspathPath(path, "resolved classpath"))
            .Distinct(comparer)
            .ToArray();

        var originalSet = new HashSet<string>(original, comparer);
        var replacements = ResolveReplacements(transform, originalSet, comparer);
        var removals = ResolveRemovals(transform, originalSet, comparer);
        var conflict = replacements.Keys.FirstOrDefault(removals.Contains);
        if (conflict is not null)
        {
            throw new MinecraftLaunchException(
                $"A classpath entry cannot be replaced and removed together: {conflict}");
        }

        var result = NormalizeClasspathEntries(
                RequireList(transform.PrependClasspath, nameof(transform.PrependClasspath)),
                "prepended classpath")
            .ToList();
        foreach (var path in original)
        {
            if (!removals.Contains(path))
                result.Add(replacements.GetValueOrDefault(path, path));
        }
        result.AddRange(NormalizeClasspathEntries(
            RequireList(transform.AppendClasspath, nameof(transform.AppendClasspath)),
            "appended classpath"));

        // The first occurrence wins, allowing prepend to move an existing entry.
        var finalClasspath = result.Distinct(comparer).ToArray();
        if (finalClasspath.Length == 0)
            throw new MinecraftLaunchException("The final classpath cannot be empty.");
        return finalClasspath;
    }

    private static Dictionary<string, string> ResolveReplacements(
        MinecraftLaunchTransform transform,
        IReadOnlySet<string> original,
        StringComparer comparer)
    {
        var result = new Dictionary<string, string>(comparer);
        foreach (var replacement in RequireList(
                     transform.ClasspathReplacements,
                     nameof(transform.ClasspathReplacements)))
        {
            if (replacement is null)
                throw new MinecraftLaunchException("Classpath replacements cannot contain null items.");

            var source = NormalizeExistingClasspathPath(
                replacement.ExistingPath,
                "classpath replacement source");
            var target = NormalizeExistingClasspathPath(
                replacement.ReplacementPath,
                "classpath replacement target");
            if (!original.Contains(source))
            {
                throw new MinecraftLaunchException(
                    $"Classpath replacement source was not found exactly: {source}");
            }
            if (result.TryGetValue(source, out var previous) && !comparer.Equals(previous, target))
            {
                throw new MinecraftLaunchException(
                    $"Conflicting classpath replacements target: {source}");
            }
            result[source] = target;
        }
        return result;
    }

    private static HashSet<string> ResolveRemovals(
        MinecraftLaunchTransform transform,
        IReadOnlySet<string> original,
        StringComparer comparer)
    {
        var result = new HashSet<string>(comparer);
        foreach (var path in RequireList(transform.RemoveClasspath, nameof(transform.RemoveClasspath)))
        {
            var normalized = NormalizeExistingClasspathPath(path, "classpath removal");
            if (!original.Contains(normalized))
            {
                throw new MinecraftLaunchException(
                    $"Classpath removal source was not found exactly: {normalized}");
            }
            result.Add(normalized);
        }
        return result;
    }

    private static IEnumerable<string> NormalizeClasspathEntries(
        IReadOnlyList<string> paths,
        string description)
    {
        foreach (var path in paths)
            yield return NormalizeExistingClasspathPath(path, description);
    }

    private static string NormalizeExistingClasspathPath(string path, string description)
    {
        var fullPath = NormalizeAbsolutePath(path, description);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            throw new MinecraftLaunchException($"{description} does not exist: {fullPath}");
        return fullPath;
    }

    private static string? ResolveJavaExecutableOverride(string? path)
    {
        if (path is null)
            return null;
        if (string.IsNullOrWhiteSpace(path))
            throw new MinecraftLaunchException("JavaExecutableOverride cannot be blank.");

        var fullPath = NormalizeAbsolutePath(path, "Java executable override");
        if (!File.Exists(fullPath))
            throw new MinecraftLaunchException($"Java executable override does not exist: {fullPath}");
        return fullPath;
    }

    private static string ResolveWorkingDirectory(string fallback, string? path)
    {
        if (path is null)
            return fallback;
        if (string.IsNullOrWhiteSpace(path))
            throw new MinecraftLaunchException("WorkingDirectoryOverride cannot be blank.");

        var fullPath = NormalizeAbsolutePath(path, "working directory override");
        if (!Directory.Exists(fullPath))
            throw new MinecraftLaunchException($"Working directory override does not exist: {fullPath}");
        return fullPath;
    }

    private static string NormalizeAbsolutePath(string path, string description)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new MinecraftLaunchException($"{description} cannot be blank.");

            var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(expanded))
                throw new MinecraftLaunchException($"{description} must be an absolute path: {path}");
            return Path.GetFullPath(expanded);
        }
        catch (MinecraftLaunchException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new MinecraftLaunchException($"Invalid {description}: {path}", exception);
        }
    }

    private static string ResolveMainClass(string fallback, string? mainClassOverride)
    {
        var mainClass = mainClassOverride is null ? fallback : mainClassOverride.Trim();
        if (!IsValidJavaClassName(mainClass))
            throw new MinecraftLaunchException($"Invalid Minecraft main class: {mainClass}");
        return mainClass;
    }

    private static bool IsValidJavaClassName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var atSegmentStart = true;
        foreach (var character in value)
        {
            if (character == '.')
            {
                if (atSegmentStart)
                    return false;
                atSegmentStart = true;
                continue;
            }

            if (atSegmentStart)
            {
                if (!char.IsLetter(character) && character is not '_' and not '$')
                    return false;
                atSegmentStart = false;
            }
            else if (!char.IsLetterOrDigit(character) && character is not '_' and not '$')
            {
                return false;
            }
        }
        return !atSegmentStart;
    }

    private static IReadOnlyList<string> ValidateAndCopyArguments(
        IReadOnlyList<string>? arguments,
        string description)
    {
        if (arguments is null)
            throw new MinecraftLaunchException($"{description} cannot be null.");

        var copy = new string[arguments.Count];
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument is null || argument.Contains('\0'))
            {
                throw new MinecraftLaunchException(
                    $"{description} contains an invalid argument at index {index}.");
            }
            copy[index] = argument;
        }
        return copy;
    }

    private static IReadOnlyDictionary<string, string?> ResolveEnvironmentVariables(
        IReadOnlyDictionary<string, string?>? variables)
    {
        if (variables is null)
            throw new MinecraftLaunchException("EnvironmentVariables cannot be null.");

        var result = new Dictionary<string, string?>(GetEnvironmentVariableComparer());
        foreach (var variable in variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Key) ||
                variable.Key.Contains('=') ||
                variable.Key.Contains('\0'))
            {
                throw new MinecraftLaunchException($"Invalid environment variable name: {variable.Key}");
            }
            if (variable.Value?.Contains('\0') == true)
            {
                throw new MinecraftLaunchException(
                    $"Environment variable contains a null character: {variable.Key}");
            }
            if (result.TryGetValue(variable.Key, out var previous) &&
                !string.Equals(previous, variable.Value, StringComparison.Ordinal))
            {
                throw new MinecraftLaunchException(
                    $"Conflicting environment variable values: {variable.Key}");
            }
            result[variable.Key] = variable.Value;
        }
        return result;
    }

    private static IReadOnlyList<T> RequireList<T>(IReadOnlyList<T>? values, string description) =>
        values ?? throw new MinecraftLaunchException($"{description} cannot be null.");

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparer GetEnvironmentVariableComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

internal sealed record ResolvedMinecraftLaunchTransform(
    string MainClass,
    string WorkingDirectory,
    string? JavaExecutableOverride,
    IReadOnlyList<string> Classpath,
    IReadOnlyList<string> PrependJvmArguments,
    IReadOnlyList<string> AppendJvmArguments,
    IReadOnlyList<string> PrependGameArguments,
    IReadOnlyList<string> AppendGameArguments,
    IReadOnlyDictionary<string, string?> EnvironmentVariables);
