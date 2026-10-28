namespace SourceRefresher
{
    internal enum RuleStatusKind
    {
        Off,
        NotReady,
        SourceNotFound,
        SceneInactive,
        WaitingStartup,
        StartupDone,
        Counting,
        WaitingSilence
    }

    internal readonly record struct RuleStatus(RuleStatusKind Kind, int RemainingSeconds = 0);

    /// <summary>
    /// Когда обновлять источник одного правила. Не знает про OBS: на вход приходят время и три признака источника
    /// (найден, в активной сцене, сейчас тихий), на выходе причина обновления или null. Поэтому логика проверяется тестами без OBS.
    ///
    /// Режим «каждые N»: таймер идёт, по его окончании правило переходит в ожидание. Без галочки «только в тишине» обновляет сразу,
    /// с галочкой ждёт, пока в источнике не будет звука нужное число секунд подряд. После обновления таймер начинается заново.
    /// Режим «при запуске»: одно обновление после запуска OBS, как только источник оказался в активной сцене.
    /// Пока источник не найден или его сцена неактивна, ничего не происходит, а тишина не копится.
    /// </summary>
    internal sealed class RefreshRuleState
    {
        private long _startMs;
        private bool _waiting;
        private long _silentSinceMs = -1;

        public RefreshRuleState(RefreshRuleConfig config, long nowMs, bool startupPending)
        {
            Config = config;
            StartupPending = startupPending && config.IsStartup;
            _startMs = nowMs;
        }

        public RefreshRuleConfig Config { get; }

        /// <summary>Обновление при запуске ещё не сделано (источник мог быть в неактивной сцене)</summary>
        public bool StartupPending { get; private set; }

        /// <summary>Начать отсчёт интервала заново (OBS закончил загрузку)</summary>
        public void Restart(long nowMs)
        {
            _startMs = nowMs;
            _waiting = false;
            _silentSinceMs = -1;
        }

        /// <summary>Причина обновления сейчас или null. Если вернулась причина, обновление считается сделанным</summary>
        public string? Tick(long nowMs, bool found, bool active, bool silent)
        {
            if (!Config.enabled || Config.source_name.Length == 0) return null;

            if (Config.IsStartup)
            {
                if (!StartupPending || !found || !active) return null;
                StartupPending = false;
                return "при запуске OBS";
            }

            if (!_waiting && nowMs - _startMs >= Config.interval_seconds * 1000L) _waiting = true;
            if (!_waiting) return null;

            if (!found || !active)
            {
                _silentSinceMs = -1;
                return null;
            }

            if (!Config.only_silent) return Fire(nowMs, "по таймеру");

            if (!silent)
            {
                _silentSinceMs = -1;
                return null;
            }

            if (_silentSinceMs < 0) _silentSinceMs = nowMs;
            if (nowMs - _silentSinceMs >= Config.silence_seconds * 1000L)
            {
                return Fire(nowMs, $"по таймеру, после {Config.silence_seconds} с тишины");
            }

            return null;
        }

        private string Fire(long nowMs, string reason)
        {
            _startMs = nowMs;
            _waiting = false;
            _silentSinceMs = -1;
            return reason;
        }

        public RuleStatus GetStatus(long nowMs, bool found, bool active)
        {
            if (!Config.enabled) return new RuleStatus(RuleStatusKind.Off);
            if (Config.source_name.Length == 0) return new RuleStatus(RuleStatusKind.NotReady);
            if (!found) return new RuleStatus(RuleStatusKind.SourceNotFound);

            if (Config.IsStartup)
            {
                if (!StartupPending) return new RuleStatus(RuleStatusKind.StartupDone);
                return new RuleStatus(active ? RuleStatusKind.WaitingStartup : RuleStatusKind.SceneInactive);
            }

            if (!active) return new RuleStatus(RuleStatusKind.SceneInactive);

            long left = Config.interval_seconds * 1000L - (nowMs - _startMs);
            if (_waiting || left <= 0) return new RuleStatus(Config.only_silent ? RuleStatusKind.WaitingSilence : RuleStatusKind.Counting);
            return new RuleStatus(RuleStatusKind.Counting, (int)((left + 999) / 1000));
        }
    }
}
