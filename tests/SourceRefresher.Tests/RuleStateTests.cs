using Xunit;

namespace SourceRefresher.Tests
{
    public class RuleStateTests
    {
        private const long Sec = 1000;

        private static RefreshRuleState Interval(int seconds, bool onlySilent, int silenceSeconds = 5)
            => new RefreshRuleState(Rules.Interval(seconds, onlySilent, silenceSeconds: silenceSeconds), 0, startupPending: false);

        [Fact]
        public void Interval_WithoutSilenceCondition_RefreshesWhenTimeIsUp()
        {
            var state = Interval(60, onlySilent: false);

            Assert.Null(state.Tick(59 * Sec, found: true, active: true, silent: false));
            Assert.Equal("по таймеру", state.Tick(60 * Sec, true, true, silent: false));
        }

        [Fact]
        public void Interval_RestartsAfterRefresh()
        {
            var state = Interval(60, onlySilent: false);
            Assert.NotNull(state.Tick(60 * Sec, true, true, false));

            Assert.Null(state.Tick(119 * Sec, true, true, false));
            Assert.NotNull(state.Tick(120 * Sec, true, true, false));
        }

        [Fact]
        public void OnlySilent_WaitsForSilenceAfterTimerEnds()
        {
            var state = Interval(60, onlySilent: true);

            // таймер вышел, но звук играет: ждём
            Assert.Null(state.Tick(60 * Sec, true, true, silent: false));
            Assert.Null(state.Tick(100 * Sec, true, true, silent: false));

            // тишина началась, но ещё не 5 секунд
            Assert.Null(state.Tick(101 * Sec, true, true, silent: true));
            Assert.Null(state.Tick(105 * Sec, true, true, silent: true));
            Assert.NotNull(state.Tick(106 * Sec, true, true, silent: true));
        }

        [Fact]
        public void OnlySilent_SoundResetsSilenceCounter()
        {
            var state = Interval(60, onlySilent: true);
            Assert.Null(state.Tick(60 * Sec, true, true, silent: true));
            Assert.Null(state.Tick(64 * Sec, true, true, silent: true));

            // звук на секунду: счёт тишины начинается заново
            Assert.Null(state.Tick(65 * Sec, true, true, silent: false));
            Assert.Null(state.Tick(66 * Sec, true, true, silent: true));
            Assert.Null(state.Tick(70 * Sec, true, true, silent: true));
            Assert.NotNull(state.Tick(71 * Sec, true, true, silent: true));
        }

        [Fact]
        public void OnlySilent_SilenceBeforeTimerEndsDoesNotCount()
        {
            var state = Interval(60, onlySilent: true);

            // тишина была всё время, но обновление только после таймера и ещё 5 секунд тишины после него
            for (long t = 0; t < 60 * Sec; t += Sec) Assert.Null(state.Tick(t, true, true, silent: true));
            Assert.Null(state.Tick(60 * Sec, true, true, silent: true));
            Assert.NotNull(state.Tick(65 * Sec, true, true, silent: true));
        }

        [Fact]
        public void OnlySilent_AfterRefreshTimerStartsOver()
        {
            var state = Interval(60, onlySilent: true);
            Assert.Null(state.Tick(60 * Sec, true, true, true));
            Assert.NotNull(state.Tick(65 * Sec, true, true, true));

            Assert.Null(state.Tick(124 * Sec, true, true, true));
            Assert.Null(state.Tick(125 * Sec, true, true, true));
            Assert.NotNull(state.Tick(130 * Sec, true, true, true));
        }

        [Fact]
        public void InactiveScene_DoesNothingAndDoesNotCountSilence()
        {
            var state = Interval(60, onlySilent: true);

            Assert.Null(state.Tick(60 * Sec, true, active: false, silent: true));
            Assert.Null(state.Tick(200 * Sec, true, active: false, silent: true));

            // сцена стала активной: тишина считается с этого момента, а не с прошлых тиков
            Assert.Null(state.Tick(201 * Sec, true, true, true));
            Assert.Null(state.Tick(205 * Sec, true, true, true));
            Assert.NotNull(state.Tick(206 * Sec, true, true, true));
        }

        [Fact]
        public void InactiveScene_WithoutSilenceCondition_RefreshesAsSoonAsSceneBecomesActive()
        {
            var state = Interval(60, onlySilent: false);
            Assert.Null(state.Tick(100 * Sec, true, active: false, silent: true));
            Assert.NotNull(state.Tick(101 * Sec, true, active: true, silent: true));
        }

        [Fact]
        public void SourceNotFound_DoesNothing()
        {
            var state = Interval(60, onlySilent: false);
            Assert.Null(state.Tick(100 * Sec, found: false, active: false, silent: true));
        }

        [Fact]
        public void DisabledRule_DoesNothing()
        {
            var config = Rules.Interval(60, false);
            config.enabled = false;
            var state = new RefreshRuleState(config, 0, false);
            Assert.Null(state.Tick(1000 * Sec, true, true, true));
        }

        [Fact]
        public void Startup_RefreshesOnceWhenSceneIsActive()
        {
            var state = new RefreshRuleState(Rules.Startup(), 0, startupPending: true);

            Assert.Equal("при запуске OBS", state.Tick(Sec, true, true, true));
            Assert.Null(state.Tick(2 * Sec, true, true, true));
            Assert.Null(state.Tick(100000 * Sec, true, true, true));
        }

        [Fact]
        public void Startup_WaitsForActiveScene()
        {
            var state = new RefreshRuleState(Rules.Startup(), 0, startupPending: true);

            Assert.Null(state.Tick(Sec, found: false, active: false, silent: true));
            Assert.Null(state.Tick(2 * Sec, true, active: false, silent: true));
            Assert.True(state.StartupPending);
            Assert.NotNull(state.Tick(3 * Sec, true, active: true, silent: false));
        }

        [Fact]
        public void Startup_NotPending_NeverRefreshes()
        {
            // правило, добавленное из окна уже после запуска OBS
            var state = new RefreshRuleState(Rules.Startup(), 0, startupPending: false);
            Assert.Null(state.Tick(Sec, true, true, true));
        }

        [Fact]
        public void Startup_IgnoredForIntervalRule()
        {
            var state = new RefreshRuleState(Rules.Interval(60, false), 0, startupPending: true);
            Assert.False(state.StartupPending);
        }

        [Fact]
        public void Restart_ResetsTimer()
        {
            var state = Interval(60, onlySilent: false);
            state.Restart(100 * Sec);
            Assert.Null(state.Tick(159 * Sec, true, true, true));
            Assert.NotNull(state.Tick(160 * Sec, true, true, true));
        }

        [Fact]
        public void Status_ShowsCountdownThenWaiting()
        {
            var state = Interval(60, onlySilent: true);

            var counting = state.GetStatus(10 * Sec, true, true);
            Assert.Equal(RuleStatusKind.Counting, counting.Kind);
            Assert.Equal(50, counting.RemainingSeconds);

            Assert.Equal(RuleStatusKind.WaitingSilence, state.GetStatus(61 * Sec, true, true).Kind);
            Assert.Equal(RuleStatusKind.SceneInactive, state.GetStatus(10 * Sec, true, false).Kind);
            Assert.Equal(RuleStatusKind.SourceNotFound, state.GetStatus(10 * Sec, false, false).Kind);
        }

        [Fact]
        public void Status_StartupPendingThenDone()
        {
            var state = new RefreshRuleState(Rules.Startup(), 0, true);
            Assert.Equal(RuleStatusKind.WaitingStartup, state.GetStatus(0, true, true).Kind);
            Assert.Equal(RuleStatusKind.SceneInactive, state.GetStatus(0, true, false).Kind);

            state.Tick(Sec, true, true, true);
            Assert.Equal(RuleStatusKind.StartupDone, state.GetStatus(2 * Sec, true, true).Kind);
        }

        [Fact]
        public void Status_OffAndNotReady()
        {
            var off = Rules.Interval(60, false);
            off.enabled = false;
            Assert.Equal(RuleStatusKind.Off, new RefreshRuleState(off, 0, false).GetStatus(0, true, true).Kind);

            var empty = Rules.Interval(60, false, source: "");
            Assert.Equal(RuleStatusKind.NotReady, new RefreshRuleState(empty, 0, false).GetStatus(0, true, true).Kind);
        }
    }
}
