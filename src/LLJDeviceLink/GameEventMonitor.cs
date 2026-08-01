using Gameplay;
using Il2CppInterop.Runtime.InteropTypes;
using Npc;

namespace LLJDeviceLink;

public sealed class GameEventMonitor
{
    private readonly EventRouter _router;
    private readonly ModSettings _settings;
    private readonly Dictionary<string, ItemSnapshot> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, QuestSnapshot> _quests = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ChallengeSnapshot> _challenges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _exposureStages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _nextSectionErrorLog = new(StringComparer.OrdinalIgnoreCase);
    private float _nextPoll;
    private int? _remainingLife;
    private int? _riskPoints;
    private int _failureCount;
    private bool? _gameOver;
    private bool? _paused;
    private bool? _naked;
    private bool _inGame;
    private string _gameStatus = "";

    public GameEventMonitor(EventRouter router, ModSettings settings)
    {
        _router = router;
        _settings = settings;
    }

    public int FailureCount => _failureCount;
    public string CurrentSummary { get; private set; } = "等待进入游戏";

    public void Tick(float realtimeSinceStartup)
    {
        if (realtimeSinceStartup < _nextPoll) return;
        _nextPoll = realtimeSinceStartup + 0.2f;
        TryPoll("游戏状态", PollGame, realtimeSinceStartup);
        if (!_inGame || _paused == true) return;
        TryPoll("心率/快感", PollHeartAndPleasure, realtimeSinceStartup);
        TryPoll("道具", PollItems, realtimeSinceStartup);
        TryPoll("露出/风险", PollExposure, realtimeSinceStartup);
        TryPoll("任务", PollQuests, realtimeSinceStartup);
        TryPoll("挑战", PollChallenges, realtimeSinceStartup);
        TryPoll("NPC 警戒", PollNpcAlertness, realtimeSinceStartup);
    }

    private void TryPoll(string section, Action poll, float realtimeSinceStartup)
    {
        try
        {
            poll();
        }
        catch (Exception ex)
        {
            if (_nextSectionErrorLog.TryGetValue(section, out var nextLog) && realtimeSinceStartup < nextLog) return;
            _nextSectionErrorLog[section] = realtimeSinceStartup + 10f;
            Plugin.LogSource.LogDebug($"{section}事件轮询暂不可用：{ex.Message}");
        }
    }

    private void PollGame()
    {
        var game = Link.TheGame;
        var status = game.gameStatus.ToString();
        var isInGame = status.Equals("InGame", StringComparison.OrdinalIgnoreCase);
        var gameOver = game.IsGameOver || status.Equals("GameOver", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(_gameStatus, status, StringComparison.Ordinal))
        {
            if (isInGame)
            {
                ResetJourneySnapshots();
                _router.ClearAllStates("新旅程开始");
                _failureCount = 0;
                _router.Pulse("game.start");
            }
            else if (_gameStatus.Equals("InGame", StringComparison.OrdinalIgnoreCase))
            {
                _router.ClearAllStates("旅程结束");
                if (!gameOver)
                    _router.Pulse("game.journey.end", new EventContext(FailureCount: _failureCount));
            }
            _gameStatus = status;
        }

        if (_gameOver == false && gameOver)
        {
            _router.ClearAllStates("游戏最终失败");
            _router.Pulse("game.over", new EventContext(FailureCount: _failureCount));
        }
        _gameOver = gameOver;
        _inGame = isInGame && !gameOver;
        if (!_inGame)
        {
            CurrentSummary = $"{status} | 未处于旅程中";
            return;
        }

        var remainingLife = game.RemainingLife;
        if (_remainingLife is { } previousLife)
        {
            if (remainingLife < previousLife && remainingLife > 0 && !game.IsGameOver)
            {
                _failureCount += Math.Max(1, previousLife - remainingLife);
                _router.Pulse("game.penalty", new EventContext(FailureCount: _failureCount, Detail: $"剩余生命 {remainingLife}"));
            }
            else if (remainingLife > previousLife && !game.IsGameOver)
            {
                _failureCount = 0;
            }
        }
        _remainingLife = remainingLife;

        var paused = Link.GameManager.IsGamePaused;
        if (_paused == false && paused)
            _router.ClearAllStates("游戏暂停");
        _paused = paused;

        CurrentSummary = $"{status}{(paused ? "（暂停）" : "")} | 生命 {remainingLife} | 惩罚 {_failureCount} | 风险 {Link.RiskPointsThisGame}";
    }

    private void PollHeartAndPleasure()
    {
        var intensity = Math.Clamp(Link.HeartbeatManager.Intensity, 0f, 1f);
        var heartElevated = intensity >= _settings.HeartMediumThreshold;
        var heartLevel = intensity >= _settings.HeartHighThreshold ? 2 : 1;
        _router.SetState("heart.active", "heart", heartElevated,
            new EventContext(GameLevel: heartLevel, NormalizedValue: intensity, Detail: heartLevel == 2 ? "高" : "中", NumericValue: intensity * 100f));

        var pleasure = Link.PleasureTracker;
        var state = pleasure.PleasureState.ToString();
        var context = new EventContext(GameLevel: pleasure.PleasureLimitLevel + 1, NormalizedValue: pleasure.PleasurePercentage,
            NumericValue: pleasure.PleasurePercentage * 100f);
        _router.SetState("pleasure.climax", "pleasure", state.Equals("Climax", StringComparison.OrdinalIgnoreCase), context);
        _router.SetState("pleasure.restraint", "pleasure", state.Equals("Restraint", StringComparison.OrdinalIgnoreCase), context);
    }

    private void PollItems()
    {
        var current = new Dictionary<string, ItemSnapshot>(StringComparer.OrdinalIgnoreCase);
        var operableItems = Link.ItemManager.AllOperableItems;
        for (var itemIndex = 0; itemIndex < operableItems.Count; itemIndex++)
        {
            var item = operableItems[itemIndex];
            if (item is null || item.worker is null || item.config is null) continue;
            var name = DisplayName(item.config.configName, item.config.label);
            var worker = item.worker.TryCast<ItemWorker_Pleasure>();
            if (worker is null) continue;
            var stage = Math.Max(0, worker.CurrentStageIndex);
            var active = worker.IsActive;
            if (!current.TryGetValue(name, out var aggregate))
                current[name] = new ItemSnapshot(active, stage);
            else
                current[name] = new ItemSnapshot(aggregate.Active || active, Math.Max(aggregate.Stage, stage));
        }

        foreach (var pair in current)
        {
            var hadBefore = _items.TryGetValue(pair.Key, out var before);
            var context = new EventContext(GameLevel: pair.Value.Stage + 1, Detail: pair.Key);
            if (!before.Active && pair.Value.Active)
            {
                _router.SetState("item.any.active", pair.Key, true, context);
            }
            else if (before.Active && !pair.Value.Active)
            {
                _router.SetState("item.any.active", pair.Key, false);
            }

            if (hadBefore && before.Stage != pair.Value.Stage)
            {
                if (pair.Value.Active)
                    _router.SetState("item.any.active", pair.Key, true, context);
            }
            if (pair.Value.Active)
                _router.SetState("item.any.active", pair.Key, true, context);
        }

        foreach (var missing in _items.Keys.Except(current.Keys, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            if (_items[missing].Active)
            {
                _router.SetState("item.any.active", missing, false);
            }
        }

        _items.Clear();
        foreach (var pair in current) _items[pair.Key] = pair.Value;
    }

    private void PollExposure()
    {
        var naked = Player.RoshuPlayer.IsNaked;
        if (_naked != naked)
        {
            _router.SetState("exposure.naked", "player", naked);
            _naked = naked;
        }
        _router.SetState("exposure.naked", "player", naked);

        var current = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var operableItems = Link.ItemManager.AllOperableItems;
        for (var itemIndex = 0; itemIndex < operableItems.Count; itemIndex++)
        {
            var item = operableItems[itemIndex];
            var worker = item?.worker?.TryCast<ItemWorker_Exposable>();
            if (worker is null || item!.config is null) continue;
            var name = DisplayName(item.config.configName, item.config.label);
            current[name] = worker.CurrentExposedStageIndex;
            if (_exposureStages.TryGetValue(name, out var before) && before != worker.CurrentExposedStageIndex)
            {
                var increasing = worker.CurrentExposedStageIndex > before;
                var context = new EventContext(GameLevel: worker.CurrentExposedStageIndex + 1,
                    Detail: $"{name}：{(increasing ? "增加" : "降低")}", NumericValue: worker.CurrentExposedStageIndex + 1);
                _router.Pulse("exposure.change", context, name);
            }
        }
        _exposureStages.Clear();
        foreach (var pair in current) _exposureStages[pair.Key] = pair.Value;

        var risk = Math.Max(0, Link.RiskPointsThisGame);
        var riskLevel = risk >= _settings.RiskHighThreshold ? 3 : risk >= _settings.RiskMediumThreshold ? 2 : 1;
        if (_riskPoints is { } previousRisk && previousRisk != risk)
            _router.Pulse("risk.change",
                new EventContext(GameLevel: riskLevel,
                    NormalizedValue: _settings.RiskHighThreshold <= 0 ? 0 : Math.Clamp((float)risk / _settings.RiskHighThreshold, 0f, 1f),
                    Detail: $"风险值 {previousRisk} → {risk}", NumericValue: risk), "risk");
        _riskPoints = risk;
    }

    private void PollQuests()
    {
        var quests = Link.QuestManager.quests;
        for (var questIndex = 0; questIndex < quests.Count; questIndex++)
        {
            var quest = quests[questIndex];
            if (quest?.Config is null) continue;
            var name = DisplayName(quest.Config.configName, quest.Config.label);
            var snapshot = new QuestSnapshot(quest.IsActive, quest.IsCompleted, quest.IsFailed);
            var hadBefore = _quests.TryGetValue(name, out var before);
            if ((!hadBefore || !before.Active) && snapshot.Active && !snapshot.Completed && !snapshot.Failed) PulseQuest("activate", name);
            if (hadBefore && !before.Completed && snapshot.Completed) PulseQuest("complete", name);
            if (hadBefore && !before.Failed && snapshot.Failed) PulseQuest("fail", name);
            _quests[name] = snapshot;
        }
    }

    private void PulseQuest(string suffix, string name)
    {
        var context = new EventContext(Detail: name);
        _router.Pulse($"task.{(suffix == "activate" ? "start" : suffix)}", context, name);
    }

    private void PollChallenges()
    {
        var challenges = Link.ChallengeManager.challengeList;
        var currentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var challengeIndex = 0; challengeIndex < challenges.Count; challengeIndex++)
        {
            var challenge = challenges[challengeIndex];
            if (challenge?.config is null) continue;
            var name = DisplayName(challenge.config.configName, challenge.config.label);
            currentNames.Add(name);
            var snapshot = new ChallengeSnapshot(challenge.IsActive && !challenge.achieved, challenge.achieved);
            var hadBefore = _challenges.TryGetValue(name, out var before);
            if ((!hadBefore || !before.Active) && snapshot.Active)
                _router.Pulse("task.start", new EventContext(Detail: name), name);
            if (hadBefore && !before.Achieved && snapshot.Achieved)
                _router.Pulse("task.complete", new EventContext(Detail: name), name);
            _challenges[name] = snapshot;
        }

        foreach (var missing in _challenges.Keys.Except(currentNames, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            _challenges.Remove(missing);
        }
    }

    private void PollNpcAlertness()
    {
        var severity = 0;
        var npcs = Link.NpcPopulation.npcList;
        for (var npcIndex = 0; npcIndex < npcs.Count; npcIndex++)
        {
            var npc = npcs[npcIndex];
            if (npc?.alertness is null || !npc.isSpawned || !npc.alertness.IsActive || !npc.alertness.HasPlayerReport) continue;
            severity = Math.Max(severity, npc.alertness.State.ToString() switch { "Notice" => 2, "Alert" => 1, _ => 0 });
        }
        _router.SetState("npc.detected", "npc", severity > 0,
            new EventContext(GameLevel: Math.Max(1, severity), NormalizedValue: severity / 2f,
                Detail: severity == 2 ? "已发现玩家" : severity == 1 ? "正在警戒" : "玩家已躲开"));
    }

    private void ResetJourneySnapshots()
    {
        _items.Clear();
        _quests.Clear();
        _challenges.Clear();
        _exposureStages.Clear();
        _remainingLife = null;
        _riskPoints = null;
        _gameOver = false;
        _paused = null;
        _naked = null;
    }

    private static string DisplayName(string? configName, string? label)
        => !string.IsNullOrWhiteSpace(label) ? label : !string.IsNullOrWhiteSpace(configName) ? configName : "未命名";

    private readonly record struct ItemSnapshot(bool Active, int Stage);
    private readonly record struct QuestSnapshot(bool Active, bool Completed, bool Failed);
    private readonly record struct ChallengeSnapshot(bool Active, bool Achieved);
}
