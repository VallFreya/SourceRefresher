using Xunit;

namespace SourceRefresher.Tests
{
    public class ConfigTests : IClassFixture<PluginFixture>
    {
        private readonly PluginFixture _fx;

        public ConfigTests(PluginFixture fixture)
        {
            _fx = fixture;
            RefreshManager.SetRules(Array.Empty<RefreshRuleConfig>());
            try { File.Delete(_fx.ConfigPath); File.Delete(_fx.ConfigPath + ".broken"); } catch { }
        }

        [Theory]
        [InlineData(0, RefreshRuleConfig.MinIntervalSeconds)]
        [InlineData(-5, RefreshRuleConfig.MinIntervalSeconds)]
        [InlineData(9, RefreshRuleConfig.MinIntervalSeconds)]
        [InlineData(300, 300)]
        [InlineData(int.MaxValue, RefreshRuleConfig.MaxIntervalSeconds)]
        [InlineData(int.MinValue, RefreshRuleConfig.MinIntervalSeconds)]
        public void Clone_ClampsInterval(int input, int expected)
        {
            var config = new RefreshRuleConfig { interval_seconds = input };
            Assert.Equal(expected, config.Clone().interval_seconds);
        }

        [Fact]
        public void Clone_FixesGarbage()
        {
            var config = new RefreshRuleConfig
            {
                id = "  ",
                name = null!,
                scene_name = null!,
                source_name = null!,
                mode = "что-то",
                silence_seconds = -4,
                silence_db = float.NaN
            };

            var clean = config.Clone();
            Assert.NotEmpty(clean.id);
            Assert.Equal("", clean.name);
            Assert.Equal("", clean.scene_name);
            Assert.Equal("", clean.source_name);
            Assert.Equal(RefreshRuleConfig.ModeInterval, clean.mode);
            Assert.Equal(1, clean.silence_seconds);
            Assert.Equal(RefreshRuleConfig.DefaultSilenceDb, clean.silence_db);

            Assert.Equal(RefreshRuleConfig.MaxSilenceSeconds, new RefreshRuleConfig { silence_seconds = int.MaxValue }.Clone().silence_seconds);
            Assert.Equal(RefreshRuleConfig.MaxSilenceDb, new RefreshRuleConfig { silence_db = 12f }.Clone().silence_db);
        }

        [Fact]
        public void NewRule_DefaultsToStartupMode()
        {
            var rule = new RefreshRuleConfig();
            Assert.True(rule.IsStartup);
            Assert.True(rule.Clone().IsStartup);
        }

        [Fact]
        public void Clone_KeepsIdentityAndSettings()
        {
            var config = new RefreshRuleConfig
            {
                id = "abc", name = " Музыка ", enabled = false, scene_name = "Игра", source_name = "Плеер",
                mode = RefreshRuleConfig.ModeStartup, interval_seconds = 900, only_silent = false
            };
            var copy = config.Clone();
            Assert.Equal("abc", copy.id);
            Assert.Equal("Музыка", copy.name);
            Assert.False(copy.enabled);
            Assert.Equal("Игра", copy.scene_name);
            Assert.Equal("Плеер", copy.source_name);
            Assert.True(copy.IsStartup);
            Assert.Equal(900, copy.interval_seconds);
            Assert.False(copy.only_silent);
        }

        [Fact]
        public void SaveAndLoad_RoundTripsRulesAndCyrillic()
        {
            var rule = Rules.Interval(900, true, source: "Музыка наград");
            rule.name = "Музыка по наградам";
            Assert.True(Plugin.SaveConfig(new List<RefreshRuleConfig> { rule }));

            string json = File.ReadAllText(_fx.ConfigPath);
            Assert.Contains("Музыка по наградам", json);   // кириллица не экранируется
            Assert.False(File.Exists(_fx.ConfigPath + ".tmp"));

            RefreshManager.SetRules(Array.Empty<RefreshRuleConfig>());
            Plugin.LoadConfig();

            var loaded = Assert.Single(RefreshManager.GetRules());
            Assert.Equal(rule.id, loaded.id);
            Assert.Equal("Музыка наград", loaded.source_name);
            Assert.Equal(900, loaded.interval_seconds);
        }

        [Fact]
        public void BrokenConfig_IsNotLostAndRulesStayEmpty()
        {
            File.WriteAllText(_fx.ConfigPath, "{ это не json");
            Plugin.LoadConfig();

            Assert.Empty(RefreshManager.GetRules());
            Assert.Equal("{ это не json", File.ReadAllText(_fx.ConfigPath + ".broken"));
        }

        [Fact]
        public void ConfigWithNullsAndMissingFields_Loads()
        {
            File.WriteAllText(_fx.ConfigPath, "{ \"rules\": [ null, { \"source_name\": \"A\" }, { \"mode\": \"startup\", \"source_name\": \"B\", \"interval_seconds\": 0 } ] }");
            Plugin.LoadConfig();

            var rules = RefreshManager.GetRules();
            Assert.Equal(2, rules.Count);
            Assert.Equal("A", rules[0].source_name);
            Assert.True(rules[0].only_silent);
            Assert.Equal(RefreshRuleConfig.DefaultIntervalSeconds, rules[0].interval_seconds);
            Assert.True(rules[1].IsStartup);
        }

        [Fact]
        public void MissingConfig_IsNotAnError()
        {
            Plugin.LoadConfig();
            Assert.Empty(RefreshManager.GetRules());
        }

        [Fact]
        public void AtomicFile_ReplacesFileWithoutLeavingTemp()
        {
            string path = Path.Combine(_fx.Dir, "a.txt");
            AtomicFile.WriteAllText(path, "один");
            AtomicFile.WriteAllText(path, "два");
            Assert.Equal("два", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void Duration_IsHumanReadable()
        {
            string h = Loc.UnitHours, m = Loc.UnitMinutes, s = Loc.UnitSeconds;
            Assert.Equal($"45 {s}", Loc.Duration(45));
            Assert.Equal($"5 {m}", Loc.Duration(300));
            Assert.Equal($"1 {h} 30 {m}", Loc.Duration(5400));
            Assert.Equal($"1 {h} 5 {s}", Loc.Duration(3605));
        }

        [Fact]
        public void Countdown_Format()
        {
            Assert.Equal("04:12", Loc.Countdown(252));
            Assert.Equal("1:30:05", Loc.Countdown(5405));
        }
    }
}
