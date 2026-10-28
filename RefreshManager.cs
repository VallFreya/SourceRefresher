using Timer = System.Threading.Timer;

namespace SourceRefresher
{
    /// <summary>Измеритель источника: найден ли он, в активной ли сцене, какой пик звука был с прошлого вызова (для подмены в проверках)</summary>
    internal interface ISourceProbe : IDisposable
    {
        bool Attached { get; }
        bool IsActive { get; }
        float TakePeakDb();
    }

    internal delegate bool RefreshAction(string sourceName, out string method);

    /// <summary>
    /// Работает с правилами: раз в 200 мс спрашивает у каждого правила (RefreshRuleState), не пора ли обновлять источник,
    /// и обновляет. До конца загрузки OBS ничего не происходит: браузерные источники в это время ещё не готовы.
    /// </summary>
    internal static class RefreshManager
    {
        private const int TickMs = 200;

        private sealed class RuleRuntime
        {
            public RuleRuntime(RefreshRuleState state) { State = state; }

            public RefreshRuleState State { get; }
            public ISourceProbe? Probe;
            public string ProbeSource = "";
            public bool Found;
            public bool Active;
        }

        private static readonly object SyncLock = new object();
        private static readonly List<RuleRuntime> Rules = new List<RuleRuntime>();
        private static Timer? _timer;
        private static bool _loaded;
        private static int _ticking;

        /// <summary>Подмена измерителя и самого обновления в проверках: настоящие работают только внутри OBS</summary>
        internal static Func<string, ISourceProbe> ProbeFactory { get; set; } = name => new ObsLevelProbe(name);
        internal static RefreshAction Refresh { get; set; } = ObsRefresher.TryRefresh;

        public static void Start()
        {
            lock (SyncLock)
            {
                _timer ??= new Timer(OnTimer, null, TickMs, TickMs);
            }
        }

        public static void Stop()
        {
            lock (SyncLock)
            {
                _timer?.Dispose();
                _timer = null;
                _loaded = false;
                foreach (var rule in Rules) DisposeProbe(rule);
                ObsLevelProbe.DisposeAll();   // на случай измерителей, оставшихся открытыми: плагин выгружается
            }
        }

        /// <summary>OBS закончил загрузку: отсюда идёт отсчёт интервалов, и правила «при запуске» могут сработать</summary>
        public static void MarkLoaded(long nowMs)
        {
            lock (SyncLock)
            {
                if (_loaded) return;
                _loaded = true;
                foreach (var rule in Rules) rule.State.Restart(nowMs);
                Plugin.LogInfo("OBS закончил загрузку, правила начинают работу");
            }
        }

        /// <summary>Копия текущих правил для окна настроек</summary>
        public static List<RefreshRuleConfig> GetRules()
        {
            lock (SyncLock)
            {
                var result = new List<RefreshRuleConfig>(Rules.Count);
                foreach (var rule in Rules) result.Add(rule.State.Config.Clone());
                return result;
            }
        }

        /// <summary>
        /// Заменяет набор правил. Правило, у которого не изменилось ничего, влияющее на работу, сохраняет свой отсчёт
        /// (смена названия или темы таймер не сбрасывает). initial: правила только что прочитаны из конфига при запуске,
        /// и правила «при запуске» ждут своего обновления; правило, добавленное позже из окна, ждёт следующего запуска.
        /// </summary>
        public static void SetRules(IEnumerable<RefreshRuleConfig> rules, bool initial = false, long? nowMs = null)
        {
            long now = nowMs ?? Environment.TickCount64;

            lock (SyncLock)
            {
                var old = new List<RuleRuntime>(Rules);
                Rules.Clear();

                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var config in rules)
                {
                    if (config == null) continue;   // "null" в списке правил конфига, написанного вручную

                    var copy = config.Clone();
                    // у копии, сделанной вручную в конфиге, номер мог повториться: второму правилу выдаём новый
                    if (!ids.Add(copy.id))
                    {
                        copy.id = RefreshRuleConfig.NewId();
                        ids.Add(copy.id);
                    }

                    var previous = old.Find(r => r.State.Config.id == copy.id);
                    if (previous != null && SameBehavior(previous.State.Config, copy))
                    {
                        old.Remove(previous);
                        Rules.Add(previous);
                    }
                    else
                    {
                        Rules.Add(new RuleRuntime(new RefreshRuleState(copy, now, startupPending: initial && copy.enabled)));
                    }
                }

                foreach (var gone in old) DisposeProbe(gone);

                Plugin.LogInfo($"Правил: {Rules.Count}");
            }
        }

        private static bool SameBehavior(RefreshRuleConfig a, RefreshRuleConfig b)
        {
            return a.enabled == b.enabled
                && a.source_name == b.source_name
                && a.mode == b.mode
                && a.interval_seconds == b.interval_seconds
                && a.only_silent == b.only_silent
                && a.silence_seconds == b.silence_seconds
                && a.silence_db == b.silence_db;
        }

        /// <summary>Состояние правила для плашки в окне; null, если правила с таким номером нет (его ещё не применили)</summary>
        public static RuleStatus? GetStatus(string id, long? nowMs = null)
        {
            long now = nowMs ?? Environment.TickCount64;
            lock (SyncLock)
            {
                var rule = Rules.Find(r => r.State.Config.id == id);
                return rule?.State.GetStatus(now, rule.Found, rule.Active);
            }
        }

        private static void OnTimer(object? state)
        {
            // Исключение в потоке пула завершило бы процесс OBS. Тик, который не успел закончиться до следующего, пропускается
            if (Interlocked.Exchange(ref _ticking, 1) != 0) return;
            try
            {
                TickOnce(Environment.TickCount64);
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Ошибка проверки правил: {ex}");
            }
            finally
            {
                Interlocked.Exchange(ref _ticking, 0);
            }
        }

        internal static void TickOnce(long nowMs)
        {
            List<(string Source, string Reason)>? due = null;

            lock (SyncLock)
            {
                if (!_loaded) return;

                foreach (var rule in Rules)
                {
                    var config = rule.State.Config;
                    if (!config.enabled || config.source_name.Length == 0)
                    {
                        DisposeProbe(rule);
                        rule.Found = rule.Active = false;
                        continue;
                    }

                    var probe = EnsureProbe(rule);
                    if (probe == null) continue;

                    float peak = probe.TakePeakDb();
                    rule.Found = probe.Attached;
                    rule.Active = rule.Found && probe.IsActive;

                    string? reason = rule.State.Tick(nowMs, rule.Found, rule.Active, silent: peak <= config.silence_db);
                    if (reason != null) (due ??= new List<(string, string)>()).Add((config.source_name, reason));
                }
            }

            if (due == null) return;

            // Само обновление вне блокировки: окно настроек не должно ждать OBS при сохранении правил
            foreach (var (source, reason) in due)
            {
                bool done = false;
                string method = "";
                try
                {
                    done = Refresh(source, out method);
                }
                catch (Exception ex)
                {
                    Plugin.LogError($"Не удалось обновить источник '{source}': {ex.Message}");
                    continue;
                }

                if (done) Plugin.LogInfo($"Обновлён источник '{source}': {reason} ({method})");
                else Plugin.LogWarn($"Источник '{source}' не обновлён ({reason}): его нет или у него нет кнопки обновления");
            }
        }

        private static ISourceProbe? EnsureProbe(RuleRuntime rule)
        {
            string source = rule.State.Config.source_name;
            if (rule.Probe != null && rule.ProbeSource == source) return rule.Probe;

            DisposeProbe(rule);
            try
            {
                rule.Probe = ProbeFactory(source);
                rule.ProbeSource = source;
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Не удалось подключиться к источнику '{source}': {ex.Message}");
            }
            return rule.Probe;
        }

        private static void DisposeProbe(RuleRuntime rule)
        {
            var probe = rule.Probe;
            rule.Probe = null;
            rule.ProbeSource = "";
            try { probe?.Dispose(); } catch { }
        }
    }
}
