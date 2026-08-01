using LLJDeviceLink;

static void Equal(string expectedHex, byte[] actual, string name)
{
    var actualHex = Convert.ToHexString(actual);
    if (!string.Equals(expectedHex, actualHex, StringComparison.OrdinalIgnoreCase))
        throw new Exception($"{name}: expected {expectedHex}, actual {actualHex}");
    Console.WriteLine($"PASS {name}: {actualHex}");
}

static void EqualValue<T>(T expected, T actual, string name) where T : IEquatable<T>
{
    if (!expected.Equals(actual)) throw new Exception($"{name}: expected {expected}, actual {actual}");
    Console.WriteLine($"PASS {name}: {actual}");
}

Equal("3511030100B4010000FF", ProtocolPackets.YcyV1Fixed(true, true, 180, 1), "役次元一代固定模式和 180 上限");
Equal("35110200B4010000B40200B3", ProtocolPackets.YcyV2Realtime(180, 1, 0, 180, 2, 0), "役次元二代实时双通道");
Equal("351205101470", ProtocolPackets.ToyRate(5, 16, 99), "役次元玩具 1-20 限制和校验");
Equal("BF3C3D01020304", ProtocolPackets.CoyoteSoftLimit(new DeviceProfile
{
    CoyoteSoftMaxA = 60,
    CoyoteSoftMaxB = 61,
    CoyoteFrequencyBalanceA = 1,
    CoyoteFrequencyBalanceB = 2,
    CoyoteStrengthBalanceA = 3,
    CoyoteStrengthBalanceB = 4
}), "郊狼 BF 软上限");
Equal("B01F0A140A0A0A0A0014283C141414140A141E28", ProtocolPackets.CoyoteFrame(1, 10, 20,
    new byte[] { 10, 10, 10, 10, 0, 20, 40, 60 },
    new byte[] { 20, 20, 20, 20, 10, 20, 30, 40 }), "郊狼 B0 双通道帧");
Equal("B00000000A0A0A0A0014283C141414140A141E28", ProtocolPackets.CoyoteFrame(0, 0, 0,
    new byte[] { 10, 10, 10, 10, 0, 20, 40, 60 },
    new byte[] { 20, 20, 20, 20, 10, 20, 30, 40 }, false, false), "郊狼等待 B1 时仅发送波形");

var penaltyAction = new DeviceActionConfig
{
    BaseIntensity = 10,
    IntensityPerPenalty = 5,
    DurationSeconds = 3,
    DurationPerPenalty = 2
};
EqualValue(10, new OutputRequest { Config = penaltyAction, Context = new EventContext(FailureCount: 1) }.Intensity(DeviceKind.YcyEmsV2), "第一次惩罚使用基础强度");
EqualValue(20, new OutputRequest { Config = penaltyAction, Context = new EventContext(FailureCount: 3) }.Intensity(DeviceKind.YcyEmsV2), "连续惩罚强度叠加");
EqualValue(7f, penaltyAction.EffectiveDuration(new EventContext(FailureCount: 3)), "连续惩罚时间叠加");

EqualValue(35, new OutputRequest
{
    EventKey = "game.penalty",
    Config = new DeviceActionConfig { BaseIntensity = 10, MaxIntensity = 35, IntensityPerPenalty = 50 },
    Context = new EventContext(FailureCount: 3)
}.Intensity(DeviceKind.YcyEmsV2), "役次元动态事件遵守独立强度上限");
EqualValue(155, new OutputRequest
{
    EventKey = "risk.change",
    Config = new DeviceActionConfig { BaseIntensity = 10, MaxIntensity = 155, UseEventValue = true, EventValueMultiplier = 10 },
    Context = new EventContext(NumericValue: 99)
}.Intensity(DeviceKind.CoyoteV3), "郊狼数值事件遵守独立强度上限");
EqualValue(12, new OutputRequest
{
    EventKey = "item.any.active",
    Config = new DeviceActionConfig { BaseIntensity = 5, MaxIntensity = 12, UseGameItemLevel = true, IntensityPerGameLevel = 5 },
    Context = new EventContext(GameLevel: 9)
}.Intensity(DeviceKind.YcyToy), "玩具挡位事件遵守独立强度上限");

var toyAction = new DeviceActionConfig { BaseIntensity = 5, UseGameItemLevel = true, IntensityPerGameLevel = 3 };
EqualValue(14, new OutputRequest { EventKey = "item.any.active", Config = toyAction, Context = new EventContext(GameLevel: 4) }.Intensity(DeviceKind.YcyToy), "飞机杯跳蛋按道具挡位调强度");
EqualValue(20, new OutputRequest { EventKey = "item.any.active", Config = toyAction, Context = new EventContext(GameLevel: 99) }.Intensity(DeviceKind.YcyToy), "飞机杯跳蛋强度限制 20");
EqualValue(5, new OutputRequest { EventKey = "risk.change", Config = toyAction, Context = new EventContext(GameLevel: 4) }.Intensity(DeviceKind.YcyToy), "非道具事件不误用道具挡位");

var numericAction = new DeviceActionConfig { BaseIntensity = 10, UseEventValue = true, EventValueMultiplier = 1.5f };
EqualValue(40, new OutputRequest { EventKey = "risk.change", Config = numericAction, Context = new EventContext(NumericValue: 20) }.Intensity(DeviceKind.YcyEmsV2), "风险值按倍率换算设备强度");
EqualValue(180, new OutputRequest { EventKey = "heart.active", Config = numericAction, Context = new EventContext(NumericValue: 200) }.Intensity(DeviceKind.YcyEmsV2), "数值倍率仍受役次元强度上限保护");
EqualValue(10, new OutputRequest { EventKey = "risk.change", Config = new DeviceActionConfig { BaseIntensity = 10, EventValueMultiplier = 5 }, Context = new EventContext(NumericValue: 20) }.Intensity(DeviceKind.YcyEmsV2), "未启用数值换算时保持基础强度");

var invalidYcyInput = new DeviceActionConfig
{
    BaseIntensity = 999,
    MaxIntensity = 999,
    IntensityPerPenalty = 999,
    IntensityPerGameLevel = 999,
    DurationSeconds = -1,
    DurationPerPenalty = -1,
    EventValueMultiplier = -1
};
EqualValue(true, invalidYcyInput.NormalizeFor(DeviceKind.YcyEmsV1), "越界配置输入会被归一化");
EqualValue(true,
    invalidYcyInput.BaseIntensity == 180 && invalidYcyInput.MaxIntensity == 180 &&
    invalidYcyInput.IntensityPerPenalty == 180 && invalidYcyInput.IntensityPerGameLevel == 180 &&
    invalidYcyInput.DurationSeconds == 0.1f && invalidYcyInput.DurationPerPenalty == 0f &&
    invalidYcyInput.EventValueMultiplier == 0f,
    "役次元所有强度输入限制为 0-180");

var invalidToyInput = new DeviceActionConfig { BaseIntensity = 200, MaxIntensity = 200, IntensityPerGameLevel = 200 };
invalidToyInput.NormalizeFor(DeviceKind.YcyToy);
EqualValue(true,
    invalidToyInput.BaseIntensity == 20 && invalidToyInput.MaxIntensity == 20 && invalidToyInput.IntensityPerGameLevel == 20,
    "飞机杯跳蛋所有强度输入限制为 0-20");
var invalidCoyoteInput = new DeviceActionConfig { BaseIntensity = 999, MaxIntensity = 999 };
invalidCoyoteInput.NormalizeFor(DeviceKind.CoyoteV3);
EqualValue(true, invalidCoyoteInput.BaseIntensity == 200 && invalidCoyoteInput.MaxIntensity == 200,
    "郊狼所有强度输入限制为 0-200");

var itemSumConfig = new DeviceActionConfig { BaseIntensity = 8, MaxIntensity = 20, ToyAggregation = ToyAggregationMode.SumClamped };
var concurrentToyRequests = new[]
{
    new OutputRequest { EventKey = "item.any.active", Config = itemSumConfig.Copy() },
    new OutputRequest { EventKey = "item.any.active", Config = itemSumConfig.Copy() },
    new OutputRequest { EventKey = "heart.active", Config = new DeviceActionConfig { BaseIntensity = 18, MaxIntensity = 20 } }
};
EqualValue(18, OutputArbitration.ToyMotorIntensity(concurrentToyRequests, config => config.MotorA),
    "多道具允许相加但不同事件之间取最高值不意外叠加");

var requiredEvents = new[]
{
    "game.start", "game.penalty", "game.over", "game.journey.end", "item.any.active",
    "exposure.change", "exposure.naked", "risk.change", "heart.active", "pleasure.climax", "pleasure.restraint",
    "task.start", "task.complete", "task.fail", "npc.detected"
};
var catalogKeys = EventCatalog.All.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
EqualValue(true, requiredEvents.All(catalogKeys.Contains) && catalogKeys.Count == requiredEvents.Length, "精简游戏事件目录完整且无冗余项");
EqualValue(true,
    EventCatalog.Find("risk.change")?.NumericValueLabel == "风险值" &&
    EventCatalog.Find("heart.active")?.NumericValueLabel == "心率百分比" &&
    EventCatalog.Find("exposure.change")?.NumericValueLabel == "露出阶段",
    "带数值事件公开正确的倍率单位");
EqualValue(true,
    EventCatalog.HasVariableIntensity("game.penalty", DeviceKind.YcyEmsV1) &&
    EventCatalog.HasVariableIntensity("risk.change", DeviceKind.CoyoteV3) &&
    EventCatalog.HasVariableIntensity("item.any.active", DeviceKind.YcyToy) &&
    !EventCatalog.HasVariableIntensity("item.any.active", DeviceKind.YcyEmsV2) &&
    !EventCatalog.HasVariableIntensity("game.start", DeviceKind.YcyToy),
    "仅强度会变化的事件显示每设备强度上限");

var invalidThresholds = new ModSettings
{
    HeartMediumThreshold = 0.8f,
    HeartHighThreshold = 0.2f,
    RiskMediumThreshold = 80,
    RiskHighThreshold = 20
};
EqualValue(true, invalidThresholds.NormalizeGlobalValues(), "冲突阈值会被归一化");
EqualValue(true,
    invalidThresholds.HeartHighThreshold == invalidThresholds.HeartMediumThreshold &&
    invalidThresholds.RiskHighThreshold == invalidThresholds.RiskMediumThreshold,
    "高阈值不会低于中阈值");

var legacy = new ModSettings();
legacy.GetBindings("npc.alert")["device-a"] = new DeviceActionConfig { BaseIntensity = 11 };
legacy.GetBindings("exposure.ol短裙.increase")["device-b"] = new DeviceActionConfig { BaseIntensity = 22 };
legacy.GetBindings("item.any.level")["device-c"] = new DeviceActionConfig { BaseIntensity = 7 };
EqualValue(true, EventCatalog.MigrateBindings(legacy), "旧事件配置执行迁移");
EqualValue(true,
    legacy.EventBindings["npc.detected"].ContainsKey("device-a") &&
    legacy.EventBindings["exposure.change"].ContainsKey("device-b") &&
    legacy.EventBindings["item.any.active"].ContainsKey("device-c") &&
    legacy.EventBindings.Keys.All(catalogKeys.Contains),
    "旧配置合并到精简事件并清除动态冗余项");

var schemaTwo = new ModSettings { ConfigSchemaVersion = 2 };
schemaTwo.GetBindings("challenge.activate")["device-a"] = new DeviceActionConfig { BaseIntensity = 13 };
schemaTwo.GetBindings("quest.complete")["device-b"] = new DeviceActionConfig { BaseIntensity = 17 };
EqualValue(true, EventCatalog.MigrateBindings(schemaTwo), "1.3.0 中间配置继续迁移");
EqualValue(true,
    schemaTwo.EventBindings["task.start"].ContainsKey("device-a") &&
    schemaTwo.EventBindings["task.complete"].ContainsKey("device-b"),
    "任务与挑战合并后保留旧设备配置");

var settings = new ModSettings();
var multiBindings = settings.GetBindings("game.penalty");
multiBindings["device-a"] = new DeviceActionConfig { BaseIntensity = 10 };
multiBindings["device-b"] = new DeviceActionConfig { BaseIntensity = 20 };
multiBindings["device-c"] = new DeviceActionConfig { BaseIntensity = 30 };
multiBindings.Remove("device-a");
EqualValue(true, multiBindings.Count == 2 && multiBindings.ContainsKey("device-b") && multiBindings.ContainsKey("device-c"), "移除单台设备绑定不影响其他设备");

Console.WriteLine("All protocol and event logic tests passed.");
