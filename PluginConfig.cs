using System.Text.Json.Serialization;

namespace SourceRefresher
{
    // Одно правило: какой источник OBS обновлять и когда
    public class RefreshRuleConfig
    {
        public const string ModeStartup = "startup";
        public const string ModeInterval = "interval";

        public const int DefaultIntervalSeconds = 300;
        // Границы интервала. Значения из ручной правки конфига (0, отрицательные, int.MaxValue) подтягиваются к ним:
        // нулевой интервал обновлял бы источник на каждом тике
        public const int MinIntervalSeconds = 10;
        public const int MaxIntervalSeconds = 99 * 3600 + 59 * 60 + 59;

        public const int DefaultSilenceSeconds = 5;
        public const int MaxSilenceSeconds = 600;

        // «Нет звука»: пик источника ниже этого порога. Цифровая тишина приходит как -inf, то есть ниже любого порога
        public const float DefaultSilenceDb = -60.0f;
        public const float MinSilenceDb = -90.0f;
        public const float MaxSilenceDb = -20.0f;

        // Постоянный номер правила: по нему движок узнаёт то же правило после правки настроек, а окно отличает копию от оригинала
        public string id { get; set; } = "";

        // Название правила, которое пользователь видит в окне. Пустое значит «по умолчанию»
        public string name { get; set; } = "";

        // Выключенное правило остаётся в списке и в конфиге, но ничего не обновляет
        public bool enabled { get; set; } = true;

        public string scene_name { get; set; } = "";
        public string source_name { get; set; } = "";

        // "startup": один раз после запуска OBS (по умолчанию у нового правила); "interval": раз в interval_seconds
        public string mode { get; set; } = ModeStartup;
        public int interval_seconds { get; set; } = DefaultIntervalSeconds;

        // Только для интервала: когда время вышло, ждать, пока в источнике не будет звука silence_seconds секунд
        public bool only_silent { get; set; } = true;

        // Скрытые из окна настройки: правятся в config.json вручную
        public int silence_seconds { get; set; } = DefaultSilenceSeconds;
        public float silence_db { get; set; } = DefaultSilenceDb;

        [JsonIgnore]
        public bool IsStartup => string.Equals(mode, ModeStartup, StringComparison.OrdinalIgnoreCase);

        public RefreshRuleConfig Clone() => new RefreshRuleConfig
        {
            id = string.IsNullOrWhiteSpace(id) ? NewId() : id.Trim(),
            name = (name ?? "").Trim(),
            enabled = enabled,
            scene_name = scene_name ?? "",
            source_name = source_name ?? "",
            mode = IsStartup ? ModeStartup : ModeInterval,
            interval_seconds = ClampInterval(interval_seconds),
            only_silent = only_silent,
            silence_seconds = Math.Clamp(silence_seconds, 1, MaxSilenceSeconds),
            silence_db = float.IsFinite(silence_db) ? Math.Clamp(silence_db, MinSilenceDb, MaxSilenceDb) : DefaultSilenceDb
        };

        public static string NewId() => Guid.NewGuid().ToString("N");

        public static int ClampInterval(int seconds) => Math.Clamp(seconds, MinIntervalSeconds, MaxIntervalSeconds);
    }

    // Файл конфига
    public class PluginConfig
    {
        public int version { get; set; } = 1;
        // Тема окна настроек: "dark" (по умолчанию) или "light"
        public string theme { get; set; } = "dark";
        public List<RefreshRuleConfig> rules { get; set; } = new List<RefreshRuleConfig>();
    }

    // Source Generator для NativeAOT (убирает требование рефлексии)
    [JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(PluginConfig))]
    internal partial class PluginConfigJsonContext : JsonSerializerContext;
}
