using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SourceRefresher.Tests
{
    /// <summary>
    /// Общая подготовка: журнал плагина выключен, конфиг пишется во временную папку, состояние RefreshManager сбрасывается.
    /// Статическое состояние плагина общее, поэтому проверки не идут параллельно.
    /// </summary>
    public sealed class PluginFixture : IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), "SourceRefresherTests", Guid.NewGuid().ToString("N"));
        public string ConfigPath => Path.Combine(Dir, "config.json");

        public PluginFixture()
        {
            Directory.CreateDirectory(Dir);
            Plugin.LogEnabled = false;
            Plugin.ConfigPathOverride = ConfigPath;
            RefreshManager.Stop();
            RefreshManager.SetRules(Array.Empty<RefreshRuleConfig>());
        }

        public void Dispose()
        {
            RefreshManager.Stop();
            RefreshManager.SetRules(Array.Empty<RefreshRuleConfig>());
            RefreshManager.ProbeFactory = name => new ObsLevelProbe(name);
            RefreshManager.Refresh = ObsRefresher.TryRefresh;
            RefreshManager.SceneActive = ObsRefresher.IsActive;
            ObsRefresher.ActiveOverride = null;
            Plugin.ConfigPathOverride = null;
            Plugin.LogEnabled = true;
            try { Directory.Delete(Dir, recursive: true); } catch { }
        }
    }

    internal sealed class FakeProbe : ISourceProbe
    {
        public bool Attached { get; set; } = true;
        public bool IsActive { get; set; } = true;
        public float PeakDb { get; set; } = -96f;
        public bool Disposed { get; private set; }

        public float TakePeakDb() => PeakDb;
        public void Dispose() => Disposed = true;
    }

    internal static class Rules
    {
        public static RefreshRuleConfig Interval(int seconds, bool onlySilent, string source = "Музыка", int silenceSeconds = 5) => new RefreshRuleConfig
        {
            id = RefreshRuleConfig.NewId(),
            scene_name = "Сцена",
            source_name = source,
            mode = RefreshRuleConfig.ModeInterval,
            interval_seconds = seconds,
            only_silent = onlySilent,
            silence_seconds = silenceSeconds
        }.Clone();

        public static RefreshRuleConfig Startup(string source = "Музыка") => new RefreshRuleConfig
        {
            id = RefreshRuleConfig.NewId(),
            scene_name = "Сцена",
            source_name = source,
            mode = RefreshRuleConfig.ModeStartup
        }.Clone();
    }
}
