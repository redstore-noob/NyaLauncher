using NyaLauncher.Clock;
using NyaLauncher.Plugin.Abstractions.Components;
using NyaLauncher.Plugin.Abstractions.Plugins;

var tests = new (string Name, Func<Task> Run)[]
{
    ("24-hour formatting", () => RunSync(Test24HourFormatting)),
    ("12-hour formatting", () => RunSync(Test12HourFormatting)),
    ("auxiliary visibility", () => RunSync(TestAuxiliaryVisibility)),
    ("component layout", () => RunSync(TestComponentLayout)),
    ("live settings and disposal", TestLiveSettingsAndDisposalAsync)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL  {test.Name}: {exception.Message}");
    }
}

return failures == 0 ? 0 : 1;

static Task RunSync(Action test)
{
    test();
    return Task.CompletedTask;
}

static void Test24HourFormatting()
{
    var timeZone = TimeZoneInfo.CreateCustomTimeZone("Example/UTC+8", TimeSpan.FromHours(8), "Example", "Example");
    var display = ClockDisplayFormatter.Create(
        new DateTimeOffset(2026, 8, 13, 17, 2, 3, TimeSpan.Zero),
        new ClockOptions(true, true, true),
        timeZone);

    Assert(display.Time == "01:02", "24-hour value should be 01:02 after conversion.");
    Assert(display.Seconds == "03", "seconds should be two digits.");
    Assert(display.TimeZone == "UTC+08:00", "timezone should use a compact UTC offset.");
    Assert(!display.ShowPeriod, "24-hour mode must hide AM/PM.");
}

static void Test12HourFormatting()
{
    var timeZone = TimeZoneInfo.CreateCustomTimeZone("Example/UTC", TimeSpan.Zero, "Example", "Example");
    var display = ClockDisplayFormatter.Create(
        new DateTimeOffset(2026, 8, 13, 13, 4, 5, TimeSpan.Zero),
        new ClockOptions(false, false, false),
        timeZone);

    Assert(display.Time == "01:04", "12-hour value should not include AM/PM in the large time area.");
    Assert(!string.IsNullOrWhiteSpace(display.Period), "12-hour mode should provide a period label.");
    Assert(display.ShowPeriod, "12-hour mode must show the small period area.");
}

static void TestAuxiliaryVisibility()
{
    var display = ClockDisplayFormatter.Create(
        DateTimeOffset.UnixEpoch,
        new ClockOptions(true, false, false),
        TimeZoneInfo.Utc);

    Assert(!display.ShowTimeZone, "timezone visibility should follow settings.");
    Assert(!display.ShowSeconds, "seconds visibility should follow settings.");
}

static void TestComponentLayout()
{
    var definition = ClockComponentDefinition.Create("io.github.touristh.clock");
    Assert(definition.Id == "io.github.touristh.clock/digital-clock", "component ID must be namespaced.");

    var time = FindText(definition, ClockComponentDefinition.TimeElementId);
    var timeZone = FindText(definition, ClockComponentDefinition.TimeZoneElementId);
    var seconds = FindText(definition, ClockComponentDefinition.SecondsElementId);

    var timeArea = time.Bounds.Width * time.Bounds.Height;
    Assert(timeArea > timeZone.Bounds.Width * timeZone.Bounds.Height * 5, "time must dominate the timezone area.");
    Assert(timeArea > seconds.Bounds.Width * seconds.Bounds.Height * 20, "time must dominate the seconds area.");
    Assert(timeZone.Bounds.Y < time.Bounds.Y, "timezone must be above the main time.");
    Assert(seconds.Bounds.X > 0.5 && seconds.Bounds.Y > 0.5, "seconds must be in the lower-right region.");
}

static async Task TestLiveSettingsAndDisposalAsync()
{
    var settings = new FakePluginSettings();
    await using var instance = new ClockComponentInstance(settings);

    Assert(settings.SubscriberCount == 1, "clock instance should subscribe to setting changes.");
    Assert(
        instance.CurrentState.Elements[ClockComponentDefinition.PeriodElementId].IsVisible == false,
        "default 24-hour mode should hide AM/PM.");

    settings.Change(ClockComponentInstance.FormatSettingKey, "12");
    Assert(
        instance.CurrentState.Elements[ClockComponentDefinition.PeriodElementId].IsVisible == true,
        "changing to 12-hour mode should immediately show AM/PM.");

    await instance.DisposeAsync();
    Assert(settings.SubscriberCount == 0, "disposed clock instance should unsubscribe from settings.");
}

static TextElementDefinition FindText(PolygonComponentDefinition definition, string id) =>
    definition.Elements.OfType<TextElementDefinition>().Single(element => element.Id == id);

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class FakePluginSettings : IPluginSettings
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase)
    {
        [ClockComponentInstance.FormatSettingKey] = "24",
        [ClockComponentInstance.ShowTimeZoneSettingKey] = true,
        [ClockComponentInstance.ShowSecondsSettingKey] = true
    };
    private EventHandler<PluginSettingChangedEventArgs>? _changed;

    public int SubscriberCount { get; private set; }

    public event EventHandler<PluginSettingChangedEventArgs>? Changed
    {
        add
        {
            _changed += value;
            SubscriberCount++;
        }
        remove
        {
            _changed -= value;
            SubscriberCount--;
        }
    }

    public bool TryGet<T>(string key, out T? value, string? instanceId = null)
    {
        if (_values.TryGetValue(key, out var stored) && stored is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    public T Get<T>(string key, T fallback, string? instanceId = null) =>
        TryGet<T>(key, out var value, instanceId) ? value! : fallback;

    public ValueTask SetAsync<T>(
        string key,
        T value,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Change(key, value);
        return ValueTask.CompletedTask;
    }

    public ValueTask ResetAsync(
        string key,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _values.Remove(key);
        _changed?.Invoke(this, new PluginSettingChangedEventArgs(key, PluginSettingScope.Global, null));
        return ValueTask.CompletedTask;
    }

    public void Change<T>(string key, T value)
    {
        _values[key] = value;
        _changed?.Invoke(this, new PluginSettingChangedEventArgs(key, PluginSettingScope.Global, null));
    }
}
