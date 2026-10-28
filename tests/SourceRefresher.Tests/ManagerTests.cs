using Xunit;

namespace SourceRefresher.Tests
{
    public class ManagerTests : IClassFixture<PluginFixture>
    {
        private const long Sec = 1000;
        private readonly List<string> _refreshed = new List<string>();
        private readonly Dictionary<string, FakeProbe> _probes = new Dictionary<string, FakeProbe>();
        private bool _sceneActive = true;

        public ManagerTests(PluginFixture fixture)
        {
            _ = fixture;
            RefreshManager.Stop();
            RefreshManager.SetRules(Array.Empty<RefreshRuleConfig>());
            RefreshManager.ProbeFactory = name =>
            {
                if (!_probes.TryGetValue(name, out var probe)) _probes[name] = probe = new FakeProbe();
                return probe;
            };
            RefreshManager.SceneActive = name => _sceneActive;
            RefreshManager.Refresh = (string name, out string method) =>
            {
                _refreshed.Add(name);
                method = "тест";
                return true;
            };
        }

        [Fact]
        public void NothingHappensBeforeObsFinishedLoading()
        {
            RefreshManager.SetRules(new[] { Rules.Interval(10, false) }, nowMs: 0);
            RefreshManager.TickOnce(1000 * Sec);
            Assert.Empty(_refreshed);
        }

        [Fact]
        public void IntervalCountsFromFinishedLoading()
        {
            RefreshManager.SetRules(new[] { Rules.Interval(60, false) }, nowMs: 0);
            RefreshManager.MarkLoaded(1000 * Sec);

            RefreshManager.TickOnce(1059 * Sec);
            Assert.Empty(_refreshed);
            RefreshManager.TickOnce(1060 * Sec);
            Assert.Equal(new[] { "Музыка" }, _refreshed);
        }

        [Fact]
        public void StartupRule_RefreshesOnceAfterLoading()
        {
            RefreshManager.SetRules(new[] { Rules.Startup() }, nowMs: 0);
            RefreshManager.MarkLoaded(Sec);

            RefreshManager.TickOnce(2 * Sec);
            RefreshManager.TickOnce(3 * Sec);
            Assert.Single(_refreshed);
        }

        [Fact]
        public void StartupRule_AddedWhileObsRunning_RefreshesOnceRightAway()
        {
            RefreshManager.MarkLoaded(0);
            var rule = Rules.Startup();
            RefreshManager.SetRules(new[] { rule }, nowMs: Sec);

            RefreshManager.TickOnce(2 * Sec);
            RefreshManager.TickOnce(3 * Sec);
            Assert.Single(_refreshed);
            Assert.Equal(RuleStatusKind.StartupDone, RefreshManager.GetStatus(rule.id, 3 * Sec)!.Value.Kind);
        }

        [Fact]
        public void StartupRule_WithoutSource_RefreshesWhenSourceIsChosen()
        {
            RefreshManager.MarkLoaded(0);
            var empty = Rules.Startup(source: "");
            RefreshManager.SetRules(new[] { empty }, nowMs: Sec);
            RefreshManager.TickOnce(2 * Sec);
            Assert.Empty(_refreshed);

            var chosen = empty.Clone();
            chosen.source_name = "Музыка";
            RefreshManager.SetRules(new[] { chosen }, nowMs: 3 * Sec);
            RefreshManager.TickOnce(4 * Sec);
            Assert.Equal(new[] { "Музыка" }, _refreshed);
        }

        [Fact]
        public void StartupRule_SameSourceIsNotRefreshedTwiceInOneSession()
        {
            RefreshManager.MarkLoaded(0);
            var rule = Rules.Startup();
            RefreshManager.SetRules(new[] { rule }, nowMs: Sec);
            RefreshManager.TickOnce(2 * Sec);
            Assert.Single(_refreshed);

            // переименование, копия правила, выключение и включение, смена режима туда и обратно: источник уже обновлён в этой сессии
            var renamed = rule.Clone();
            renamed.name = "Другое название";
            var copy = rule.Clone();
            copy.id = RefreshRuleConfig.NewId();
            RefreshManager.SetRules(new[] { renamed, copy }, nowMs: 3 * Sec);

            var off = renamed.Clone();
            off.enabled = false;
            RefreshManager.SetRules(new[] { off, copy }, nowMs: 4 * Sec);
            RefreshManager.SetRules(new[] { renamed, copy }, nowMs: 5 * Sec);

            var interval = renamed.Clone();
            interval.mode = RefreshRuleConfig.ModeInterval;
            interval.interval_seconds = 3600;
            RefreshManager.SetRules(new[] { interval, copy }, nowMs: 6 * Sec);
            RefreshManager.SetRules(new[] { renamed, copy }, nowMs: 7 * Sec);

            RefreshManager.TickOnce(10 * Sec);
            Assert.Single(_refreshed);
        }

        [Fact]
        public void StartupRule_ChangingSourceRefreshesTheNewOne()
        {
            RefreshManager.MarkLoaded(0);
            var rule = Rules.Startup(source: "Первый");
            RefreshManager.SetRules(new[] { rule }, nowMs: Sec);
            RefreshManager.TickOnce(2 * Sec);

            var other = rule.Clone();
            other.source_name = "Второй";
            RefreshManager.SetRules(new[] { other }, nowMs: 3 * Sec);
            RefreshManager.TickOnce(4 * Sec);

            Assert.Equal(new[] { "Первый", "Второй" }, _refreshed);
        }

        [Fact]
        public void StartupRule_DisabledAtAdding_RefreshesWhenEnabled()
        {
            RefreshManager.MarkLoaded(0);
            var rule = Rules.Startup();
            rule.enabled = false;
            RefreshManager.SetRules(new[] { rule }, nowMs: Sec);
            RefreshManager.TickOnce(2 * Sec);
            Assert.Empty(_refreshed);

            var on = rule.Clone();
            on.enabled = true;
            RefreshManager.SetRules(new[] { on }, nowMs: 3 * Sec);
            RefreshManager.TickOnce(4 * Sec);
            Assert.Single(_refreshed);
        }

        [Fact]
        public void StartupRule_WaitsUntilSceneBecomesActive()
        {
            var rule = Rules.Startup();
            _probes["Музыка"] = new FakeProbe { IsActive = false };
            RefreshManager.SetRules(new[] { rule }, nowMs: 0);
            RefreshManager.MarkLoaded(Sec);

            RefreshManager.TickOnce(3 * Sec);
            RefreshManager.TickOnce(4 * Sec);
            Assert.Empty(_refreshed);

            _probes["Музыка"].IsActive = true;
            RefreshManager.TickOnce(5 * Sec);
            Assert.Single(_refreshed);
        }

        [Fact]
        public void OnlySilent_WaitsForSilenceAndIgnoresLoudness()
        {
            var probe = _probes["Музыка"] = new FakeProbe { PeakDb = -10f };
            RefreshManager.SetRules(new[] { Rules.Interval(60, true) }, nowMs: 0);
            RefreshManager.MarkLoaded(0);

            RefreshManager.TickOnce(60 * Sec);
            RefreshManager.TickOnce(100 * Sec);
            Assert.Empty(_refreshed);

            // тихий фоновый шум ниже порога тоже считается тишиной
            probe.PeakDb = -70f;
            RefreshManager.TickOnce(101 * Sec);
            RefreshManager.TickOnce(105 * Sec);
            Assert.Empty(_refreshed);
            RefreshManager.TickOnce(106 * Sec);
            Assert.Single(_refreshed);
        }

        [Fact]
        public void RenamingRuleDoesNotResetTimer_ChangingIntervalDoes()
        {
            var rule = Rules.Interval(60, false);
            RefreshManager.SetRules(new[] { rule }, nowMs: 0);
            RefreshManager.MarkLoaded(0);

            var renamed = rule.Clone();
            renamed.name = "Другое название";
            RefreshManager.SetRules(new[] { renamed }, nowMs: 50 * Sec);
            RefreshManager.TickOnce(60 * Sec);
            Assert.Single(_refreshed);

            var changed = renamed.Clone();
            changed.interval_seconds = 120;
            RefreshManager.SetRules(new[] { changed }, nowMs: 70 * Sec);
            RefreshManager.TickOnce(130 * Sec);   // 60 с с момента правки: ещё рано
            Assert.Single(_refreshed);
            RefreshManager.TickOnce(190 * Sec);
            Assert.Equal(2, _refreshed.Count);
        }

        [Fact]
        public void DuplicateIdsGetNewIds()
        {
            var a = Rules.Interval(60, false);
            var b = a.Clone();
            RefreshManager.SetRules(new[] { a, b });

            var rules = RefreshManager.GetRules();
            Assert.Equal(2, rules.Count);
            Assert.NotEqual(rules[0].id, rules[1].id);
        }

        [Fact]
        public void DisabledRule_DoesNotRefresh_AndNullRuleIsSkipped()
        {
            var rule = Rules.Interval(10, false);
            rule.enabled = false;
            RefreshManager.SetRules(new RefreshRuleConfig?[] { rule, null }!);
            RefreshManager.MarkLoaded(0);

            RefreshManager.TickOnce(1000 * Sec);
            Assert.Empty(_refreshed);
            Assert.Single(RefreshManager.GetRules());
        }

        [Fact]
        public void RemovedRuleReleasesItsProbe()
        {
            var rule = Rules.Interval(60, false);
            RefreshManager.SetRules(new[] { rule }, nowMs: 0);
            RefreshManager.MarkLoaded(0);
            RefreshManager.TickOnce(Sec);
            var probe = _probes["Музыка"];

            RefreshManager.SetRules(Array.Empty<RefreshRuleConfig>());
            Assert.True(probe.Disposed);
        }

        [Fact]
        public void FailingRefreshDoesNotRetryEveryTick()
        {
            int calls = 0;
            RefreshManager.Refresh = (string name, out string method) => { calls++; method = ""; return false; };
            RefreshManager.SetRules(new[] { Rules.Interval(60, false) }, nowMs: 0);
            RefreshManager.MarkLoaded(0);

            RefreshManager.TickOnce(60 * Sec);
            RefreshManager.TickOnce(61 * Sec);
            RefreshManager.TickOnce(62 * Sec);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void ExceptionInRefreshDoesNotEscape()
        {
            RefreshManager.Refresh = (string name, out string method) => throw new InvalidOperationException("boom");
            RefreshManager.SetRules(new[] { Rules.Interval(60, false) }, nowMs: 0);
            RefreshManager.MarkLoaded(0);

            RefreshManager.TickOnce(60 * Sec);
        }

        [Fact]
        public void ExceptionInProbeFactoryDoesNotEscape()
        {
            RefreshManager.ProbeFactory = name => throw new DllNotFoundException("obs.dll");
            RefreshManager.SetRules(new[] { Rules.Interval(60, false) }, nowMs: 0);
            RefreshManager.MarkLoaded(0);

            RefreshManager.TickOnce(60 * Sec);
            Assert.Empty(_refreshed);
        }

        [Fact]
        public void Status_TellsInactiveSceneFromHiddenSource()
        {
            var rule = Rules.Interval(60, false);
            _probes["Музыка"] = new FakeProbe { IsActive = false };
            RefreshManager.SetRules(new[] { rule }, nowMs: 0);
            RefreshManager.MarkLoaded(0);

            // источник неактивен, а его сцена в эфире: источник скрыт
            _sceneActive = true;
            RefreshManager.TickOnce(Sec);
            Assert.Equal(RuleStatusKind.SourceInactive, RefreshManager.GetStatus(rule.id, Sec)!.Value.Kind);

            // сцена не в эфире
            _sceneActive = false;
            RefreshManager.TickOnce(2 * Sec);
            Assert.Equal(RuleStatusKind.SceneInactive, RefreshManager.GetStatus(rule.id, 2 * Sec)!.Value.Kind);

            _probes["Музыка"].IsActive = true;
            RefreshManager.TickOnce(3 * Sec);
            Assert.Equal(RuleStatusKind.Counting, RefreshManager.GetStatus(rule.id, 3 * Sec)!.Value.Kind);
        }

        [Fact]
        public void StatusIsAvailableByRuleId()
        {
            var rule = Rules.Interval(60, false);
            RefreshManager.SetRules(new[] { rule }, nowMs: 0);
            RefreshManager.MarkLoaded(0);
            RefreshManager.TickOnce(10 * Sec);

            var status = RefreshManager.GetStatus(rule.id, 10 * Sec);
            Assert.Equal(RuleStatusKind.Counting, status!.Value.Kind);
            Assert.Equal(50, status.Value.RemainingSeconds);
            Assert.Null(RefreshManager.GetStatus("нет такого", 0));
        }
    }
}
