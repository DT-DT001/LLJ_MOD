using System.Collections.Concurrent;

namespace LLJDeviceLink;

public enum DeviceKind
{
    Unknown,
    YcyEmsV1,
    YcyEmsV2,
    CoyoteV3,
    YcyToy
}

public enum LinkState
{
    Discovered,
    Connecting,
    Connected,
    Disconnecting,
    Disconnected,
    Error
}

public enum ToyAggregationMode
{
    Maximum,
    SumClamped
}

public enum WaveformKind
{
    Ycy,
    Coyote,
    Toy
}

public sealed class DeviceProfile
{
    public string DisplayName = "";
    public bool AutoConnect = true;
    public int CoyoteSoftMaxA = 60;
    public int CoyoteSoftMaxB = 60;
    public int CoyoteFrequencyBalanceA;
    public int CoyoteFrequencyBalanceB;
    public int CoyoteStrengthBalanceA;
    public int CoyoteStrengthBalanceB;
}

public sealed class DeviceActionConfig
{
    public bool Enabled = true;
    public int BaseIntensity = 10;
    public int IntensityPerPenalty = 5;
    public float DurationSeconds = 3f;
    public float DurationPerPenalty = 1f;
    public string WaveformId = "";
    public bool ChannelA = true;
    public bool ChannelB;
    public bool MotorA = true;
    public bool MotorB = true;
    public bool MotorC = true;
    public bool UseGameItemLevel = true;
    public int IntensityPerGameLevel = 2;
    public bool UseEventValue;
    public float EventValueMultiplier = 1f;
    public ToyAggregationMode ToyAggregation = ToyAggregationMode.Maximum;

    public DeviceActionConfig Copy() => (DeviceActionConfig)MemberwiseClone();

    public float EffectiveDuration(EventContext context)
        => Math.Clamp(DurationSeconds + Math.Max(0, context.FailureCount - 1) * DurationPerPenalty, 0.1f, 600f);
}

public sealed class ModSettings
{
    public int ConfigSchemaVersion;
    // Retained for backwards-compatible config loading. Scanning is now explicitly
    // started only by the UI's 10-second "连接设备" action.
    public bool AutoScanOnStartup;
    public bool AutoConnectRecognizedDevices;
    public float HeartMediumThreshold = 0.35f;
    public float HeartHighThreshold = 0.7f;
    public int RiskMediumThreshold = 25;
    public int RiskHighThreshold = 60;
    public Dictionary<string, DeviceProfile> DeviceProfiles = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, DeviceActionConfig>> EventBindings = new(StringComparer.OrdinalIgnoreCase);

    public DeviceProfile GetProfile(string deviceId, string displayName)
    {
        if (!DeviceProfiles.TryGetValue(deviceId, out var profile))
        {
            profile = new DeviceProfile { DisplayName = displayName };
            DeviceProfiles[deviceId] = profile;
        }
        else if (!string.IsNullOrWhiteSpace(displayName))
        {
            profile.DisplayName = displayName;
        }

        return profile;
    }

    public Dictionary<string, DeviceActionConfig> GetBindings(string eventKey)
    {
        if (!EventBindings.TryGetValue(eventKey, out var bindings))
        {
            bindings = new Dictionary<string, DeviceActionConfig>(StringComparer.OrdinalIgnoreCase);
            EventBindings[eventKey] = bindings;
        }

        return bindings;
    }
}

public sealed record EventDefinition(string Key, string Category, string Title, bool Stateful, string NumericValueLabel = "");

public static class EventCatalog
{
    private static readonly ConcurrentDictionary<string, EventDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase);

    static EventCatalog()
    {
        Add("game.start", "游戏", "游戏/旅程开始", false);
        Add("game.penalty", "游戏", "失败并进入惩罚（扣除生命）", false);
        Add("game.over", "游戏", "游戏最终失败", false);
        Add("game.journey.end", "游戏", "旅程结束", false);
        Add("item.any.active", "道具", "快感道具运行（持续；自动跟随挡位）", true);
        Add("exposure.change", "露出", "衣物露出程度变化", false, "露出阶段");
        Add("exposure.naked", "露出", "完全裸露（持续）", true);
        Add("risk.change", "露出", "风险值变化", false, "风险值");
        Add("heart.active", "心率", "心率升高（持续；恢复后停止）", true, "心率百分比");
        Add("pleasure.climax", "快感", "高潮（持续）", true, "快感百分比");
        Add("pleasure.restraint", "快感", "忍耐/克制（持续）", true, "快感百分比");
        Add("task.start", "任务", "任务/挑战开始", false);
        Add("task.complete", "任务", "任务/挑战完成", false);
        Add("task.fail", "任务", "任务失败", false);
        Add("npc.detected", "发现", "NPC 警戒/发现玩家（持续；躲开后停止）", true);
    }

    public static IEnumerable<EventDefinition> All => Definitions.Values.OrderBy(x => x.Category).ThenBy(x => x.Title);

    public static EventDefinition Add(string key, string category, string title, bool stateful, string numericValueLabel = "")
        => Definitions.GetOrAdd(key, _ => new EventDefinition(key, category, title, stateful, numericValueLabel));

    public static EventDefinition? Find(string key) => Definitions.TryGetValue(key, out var value) ? value : null;

    public const int CurrentConfigSchemaVersion = 3;

    public static bool MigrateBindings(ModSettings settings)
    {
        if (settings.ConfigSchemaVersion >= CurrentConfigSchemaVersion) return false;

        var old = settings.EventBindings ?? new Dictionary<string, Dictionary<string, DeviceActionConfig>>(StringComparer.OrdinalIgnoreCase);
        var migrated = new Dictionary<string, Dictionary<string, DeviceActionConfig>>(StringComparer.OrdinalIgnoreCase);

        void Merge(string target, Func<string, bool> sourceFilter, params string[] priority)
        {
            var targetBindings = new Dictionary<string, DeviceActionConfig>(StringComparer.OrdinalIgnoreCase);
            var priorityLookup = priority.Select((key, index) => (key, index))
                .ToDictionary(item => item.key, item => item.index, StringComparer.OrdinalIgnoreCase);
            foreach (var source in old.Where(pair => sourceFilter(pair.Key))
                         .OrderBy(pair => priorityLookup.TryGetValue(pair.Key, out var index) ? index : int.MaxValue)
                         .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var binding in source.Value)
                    targetBindings.TryAdd(binding.Key, binding.Value);
            }
            if (targetBindings.Count > 0) migrated[target] = targetBindings;
        }

        static bool IsOneOf(string value, params string[] candidates)
            => candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));

        foreach (var definition in All)
            Merge(definition.Key, key => string.Equals(key, definition.Key, StringComparison.OrdinalIgnoreCase), definition.Key);

        Merge("item.any.active",
            key => string.Equals(key, "item.any.active", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, "item.any.level", StringComparison.OrdinalIgnoreCase) ||
                   key.StartsWith("item.", StringComparison.OrdinalIgnoreCase) && key.EndsWith(".active", StringComparison.OrdinalIgnoreCase),
            "item.any.active", "item.any.level");
        Merge("exposure.change",
            key => string.Equals(key, "exposure.change", StringComparison.OrdinalIgnoreCase) ||
                   key.StartsWith("exposure.", StringComparison.OrdinalIgnoreCase) &&
                   (key.EndsWith(".increase", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".decrease", StringComparison.OrdinalIgnoreCase)),
            "exposure.change", "exposure.increase", "exposure.decrease");
        Merge("risk.change", key => IsOneOf(key, "risk.change", "risk.active", "risk.high", "risk.medium", "risk.low"),
            "risk.change", "risk.active", "risk.high", "risk.medium", "risk.low");
        Merge("heart.active", key => IsOneOf(key, "heart.active", "heart.high", "heart.medium", "heart.low"),
            "heart.active", "heart.high", "heart.medium", "heart.low");
        Merge("task.start", key => string.Equals(key, "task.start", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("quest.", StringComparison.OrdinalIgnoreCase) && key.EndsWith(".activate", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("challenge.", StringComparison.OrdinalIgnoreCase) && key.EndsWith(".activate", StringComparison.OrdinalIgnoreCase), "task.start", "quest.activate", "challenge.activate");
        Merge("task.complete", key => string.Equals(key, "task.complete", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("quest.", StringComparison.OrdinalIgnoreCase) && key.EndsWith(".complete", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("challenge.", StringComparison.OrdinalIgnoreCase) && key.EndsWith(".achieved", StringComparison.OrdinalIgnoreCase), "task.complete", "quest.complete", "challenge.achieved");
        Merge("task.fail", key => string.Equals(key, "task.fail", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("quest.", StringComparison.OrdinalIgnoreCase) && key.EndsWith(".fail", StringComparison.OrdinalIgnoreCase), "task.fail", "quest.fail");
        Merge("npc.detected", key => IsOneOf(key, "npc.detected", "npc.notice", "npc.alert"),
            "npc.detected", "npc.notice", "npc.alert");

        settings.EventBindings = migrated;
        settings.ConfigSchemaVersion = CurrentConfigSchemaVersion;
        return true;
    }
}

public readonly record struct EventContext(int FailureCount = 0, int GameLevel = 1, float NormalizedValue = 0f, string Detail = "", float NumericValue = 0f);

public sealed class OutputRequest
{
    public string RequestId { get; init; } = "";
    public string EventKey { get; init; } = "";
    public DeviceActionConfig Config { get; init; } = new();
    public EventContext Context { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    public int Intensity(DeviceKind kind)
    {
        var value = Config.BaseIntensity;
        if (Context.FailureCount > 1)
            value += (Context.FailureCount - 1) * Config.IntensityPerPenalty;
        if (kind == DeviceKind.YcyToy && Config.UseGameItemLevel &&
            string.Equals(EventKey, "item.any.active", StringComparison.OrdinalIgnoreCase) && Context.GameLevel > 1)
            value += (Context.GameLevel - 1) * Config.IntensityPerGameLevel;
        if (Config.UseEventValue && Context.NumericValue > 0f)
            value += (int)MathF.Round(Context.NumericValue * Math.Max(0f, Config.EventValueMultiplier));

        return Math.Clamp(value, 0, kind switch
        {
            DeviceKind.YcyToy => 20,
            DeviceKind.CoyoteV3 => 200,
            DeviceKind.YcyEmsV1 or DeviceKind.YcyEmsV2 => 180,
            _ => 0
        });
    }
}
