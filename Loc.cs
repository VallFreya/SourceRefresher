using System.Runtime.InteropServices;

namespace SourceRefresher
{
    /// <summary>
    /// Тексты интерфейса. Язык берётся из OBS (obs_get_locale): русский для ru-*, иначе английский.
    /// Пункт меню Сервис нельзя переименовать после регистрации, поэтому язык определяется один раз
    /// и меняется вместе с языком OBS (после перезапуска OBS).
    /// </summary>
    internal static class Loc
    {
        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr obs_get_locale();

        private static bool _initialized;
        private static bool _ru;

        private static bool Ru
        {
            get
            {
                if (!_initialized)
                {
                    try
                    {
                        string? locale = Marshal.PtrToStringUTF8(obs_get_locale());
                        _ru = locale != null && locale.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        _ru = false;
                    }
                    _initialized = true;
                }
                return _ru;
            }
        }

        // Название плагина: пункт меню Сервис и заголовок окна
        public static string PluginName => Ru ? "Автообновление источников" : "Source Auto Refresh";

        public static string WindowTitle => Ru ? $"{PluginName} — настройки" : $"{PluginName} Settings";
        public static string AddRule => Ru ? "Добавить правило" : "Add rule";
        public static string NoRules => Ru ? "Правил пока нет. Нажмите «Добавить правило»." : "No rules yet. Click \"Add rule\".";
        public static string AutoSaveHint => Ru ? "Изменения сохраняются и применяются сразу" : "Changes are saved and applied right away";
        public static string Saved => Ru ? "Сохранено" : "Saved";
        public static string SaveFailed => Ru
            ? "Не удалось записать настройки в файл (подробности в журнале). Правила применены, но после перезапуска OBS пропадут"
            : "Could not write the settings file (see the log). The rules are applied but will be lost after OBS restarts";
        public static string Ok => Ru ? "ОК" : "OK";
        public static string Cancel => Ru ? "Отмена" : "Cancel";
        public static string ConfirmRemoveTitle => Ru ? "Удалить правило?" : "Remove rule?";
        public static string ConfirmRemoveBody(string name) => Ru
            ? $"Правило «{name}» будет удалено вместе со всеми его настройками. Это действие нельзя отменить."
            : $"The rule \"{name}\" will be removed together with all its settings. This cannot be undone.";
        public static string RemoveRule => Ru ? "Удалить правило" : "Remove rule";
        public static string DuplicateRule => Ru ? "Копировать правило" : "Copy rule";
        public static string CopyName(string name) => Ru ? $"{name} (копия)" : $"{name} (copy)";
        public static string SwitchTip => Ru ? "Включить или выключить правило" : "Turn the rule on or off";
        public static string EditNameTip => Ru ? "Название правила: нажмите, чтобы изменить" : "Rule name: click to edit";
        public static string CopySourceNameTip => Ru ? "Скопировать название источника в название правила" : "Copy the source name to the rule name";
        public static string RuleTitle(int number) => Ru ? $"Правило {number}" : $"Rule {number}";

        // Плашка состояния в заголовке карточки
        public static string ChipOff => Ru ? "Выключено" : "Off";
        public static string ChipNotReady => Ru ? "Не настроено" : "Not set up";
        public static string ChipApplying => Ru ? "Применяется…" : "Applying…";
        public static string ChipNotFound => Ru ? "Источник не найден" : "Source not found";
        public static string ChipSceneInactive => Ru ? "Сцена неактивна" : "Scene is inactive";
        public static string ChipSourceInactive => Ru ? "Источник неактивен" : "Source is inactive";
        public static string ChipWaitingStartup => Ru ? "Обновит при запуске" : "Will refresh at start";
        public static string ChipStartupDone => Ru ? "Обновлено в этой сессии" : "Refreshed this session";
        public static string ChipWaitingSilence => Ru ? "Ждёт тишины" : "Waiting for silence";
        public static string ChipRefreshingSoon => Ru ? "Сейчас обновит" : "Refreshing now";
        public static string ChipCounting(string left) => Ru ? $"Обновит через {left}" : $"Refreshes in {left}";

        // Блок «Что обновлять»
        public static string WhatTitle => Ru ? "Что обновлять" : "What to refresh";
        public static string Scene => Ru ? "Сцена:" : "Scene:";
        public static string Source => Ru ? "Источник:" : "Source:";
        // Пояснение значка в списке источников
        public static string RefreshBadgeLegend => Ru ? "Источник можно обновить" : "The source can be refreshed";
        public static string InactiveLegend => Ru ? "Серым: источник сейчас неактивен" : "Grey: the source is inactive right now";
        public static string NoRefreshWarning => Ru
            ? "Этот источник нельзя обновить: у него нет кнопки обновления страницы"
            : "This source cannot be refreshed: it has no page refresh button";

        // Блок «Когда обновлять»
        public static string WhenTitle => Ru ? "Когда обновлять" : "When to refresh";
        public static string Mode => Ru ? "Режим:" : "Mode:";
        public static string ModeStartup => Ru ? "Только при запуске OBS" : "Only when OBS starts";
        public static string ModeInterval => Ru ? "Каждые…" : "Every…";
        public static string Every => Ru ? "Каждые:" : "Every:";
        public static string UnitHours => Ru ? "ч" : "h";
        public static string UnitMinutes => Ru ? "мин" : "min";
        public static string UnitSeconds => Ru ? "с" : "s";

        // Блок «Условие»
        public static string ConditionTitle => Ru ? "Условие" : "Condition";
        public static string OnlySilent => Ru ? "Только в тишине" : "Only when silent";
        public static string OnlySilentHelp(int seconds) => Ru
            ? $"Когда время выйдет, плагин не обновит источник сразу, а дождётся, пока в нём не будет звука {seconds} с подряд. " +
              "Так обновление не оборвёт играющую музыку. После обновления время отсчитывается заново.\n\n" +
              "Уровень звука не важен: считается только, есть звук в источнике или нет."
            : $"When the time is up, the source is not refreshed right away: the plugin waits until it has been silent for {seconds} s in a row. " +
              "That way a refresh never cuts the music that is playing. After a refresh the time starts over.\n\n" +
              "The loudness does not matter, only whether there is sound or not.";

        // Фраза-сводка. Части, обрамлённые ** , выводятся жирным
        public static string SummaryTitle => Ru ? "Как это сработает" : "What will happen";
        public static string SummaryPick => Ru
            ? "Выберите сцену и источник, и здесь появится описание."
            : "Pick a scene and a source, and a description will appear here.";
        public static string SummaryStartup(string source) => Ru
            ? $"Один раз за запуск OBS источник **{source}** обновляется: при запуске или сразу после добавления правила, если в этой сессии он ещё не обновлялся. Если его сцена неактивна, это случится, когда она станет активной."
            : $"Once per OBS session, **{source}** is refreshed: at start, or right after the rule is added if it has not been refreshed in this session yet. If its scene is inactive, it happens when the scene becomes active.";
        public static string SummaryInterval(string source, string time) => Ru
            ? $"Каждые **{time}** источник **{source}** обновляется, пока его сцена активна. Время идёт заново после каждого обновления."
            : $"Every **{time}**, **{source}** is refreshed while its scene is active. The time starts over after every refresh.";
        public static string SummaryIntervalSilent(string source, string time, string silence) => Ru
            ? $"Через **{time}** источник **{source}** ждёт тишины: когда звука не будет **{silence}**, он обновится, и время пойдёт заново."
            : $"After **{time}**, **{source}** waits for silence: once there is no sound for **{silence}**, it is refreshed and the time starts over.";

        public static string Theme => Ru ? "Тема:" : "Theme:";
        public static string ThemeDarkName => Ru ? "Тёмная" : "Dark";
        public static string ThemeLightName => Ru ? "Светлая" : "Light";

        /// <summary>Длительность словами: «1 ч 30 мин», «5 мин», «45 с»</summary>
        public static string Duration(int totalSeconds)
        {
            int h = totalSeconds / 3600, m = totalSeconds % 3600 / 60, s = totalSeconds % 60;
            var parts = new List<string>(3);
            if (h > 0) parts.Add($"{h} {UnitHours}");
            if (m > 0) parts.Add($"{m} {UnitMinutes}");
            if (s > 0 || parts.Count == 0) parts.Add($"{s} {UnitSeconds}");
            return string.Join(" ", parts);
        }

        /// <summary>Остаток времени для плашки: «04:12» или «1:30:05»</summary>
        public static string Countdown(int totalSeconds)
        {
            int h = totalSeconds / 3600, m = totalSeconds % 3600 / 60, s = totalSeconds % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
        }
    }
}
