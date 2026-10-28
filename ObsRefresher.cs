using System.Runtime.InteropServices;

namespace SourceRefresher
{
    internal enum RefreshSupport
    {
        /// <summary>Источника с таким именем нет</summary>
        NotFound,
        /// <summary>Источник есть, но обновлять его нечем</summary>
        Unsupported,
        /// <summary>У источника есть кнопка обновления страницы, либо это медиа-источник</summary>
        Supported
    }

    /// <summary>
    /// Обновление источника OBS. Браузерному источнику нажимается кнопка «Refresh cache of current page» из его свойств
    /// (так делает и obs-websocket), медиа-источнику (видео, VLC) перезапускается воспроизведение.
    /// </summary>
    internal static class ObsRefresher
    {
        // Имя кнопки в свойствах браузерного источника (obs-browser)
        private const string RefreshButton = "refreshnocache";
        // Тип браузерного источника (obs-browser)
        private const string BrowserSourceId = "browser_source";

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr obs_get_source_by_name([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern void obs_source_release(IntPtr source);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr obs_source_get_id(IntPtr source);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr obs_source_properties(IntPtr source);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern void obs_properties_destroy(IntPtr properties);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr obs_properties_get(IntPtr properties, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool obs_property_button_clicked(IntPtr property, IntPtr obj);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern void obs_source_media_restart(IntPtr source);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool obs_source_active(IntPtr source);

        /// <summary>Подмена проверки активности в проверках вне OBS</summary>
        internal static Func<string, bool>? ActiveOverride { get; set; }

        /// <summary>
        /// Источник или сцена сейчас в эфире. У источника это значит, что его сцена в эфире и он не скрыт в ней (глаз включён).
        /// Не найден или ошибка: false
        /// </summary>
        public static bool IsActive(string sourceName)
        {
            if (ActiveOverride != null) return ActiveOverride(sourceName);
            if (string.IsNullOrEmpty(sourceName)) return false;

            IntPtr source = IntPtr.Zero;
            try
            {
                source = obs_get_source_by_name(sourceName);
                return source != IntPtr.Zero && obs_source_active(source);
            }
            catch
            {
                // нет obs.dll (проверки вне OBS) или источник пропал посреди вызова
                return false;
            }
            finally
            {
                if (source != IntPtr.Zero)
                {
                    try { obs_source_release(source); } catch { }
                }
            }
        }

        /// <summary>Подмена проверки в проверках вне OBS (настоящая работает только внутри OBS)</summary>
        internal static Func<string, RefreshSupport>? SupportOverride { get; set; }

        /// <summary>Можно ли обновить источник: это браузерный или медиа-источник (видео, VLC). Проверка быстрая, источник не трогает</summary>
        public static RefreshSupport GetSupport(string sourceName)
        {
            if (SupportOverride != null) return SupportOverride(sourceName);
            return Run(sourceName, click: false, out _);
        }

        /// <summary>Обновляет источник. false, если источника нет или обновлять его нечем. method - чем обновили (для журнала)</summary>
        public static bool TryRefresh(string sourceName, out string method)
        {
            return Run(sourceName, click: true, out method) == RefreshSupport.Supported;
        }

        private static RefreshSupport Run(string sourceName, bool click, out string method)
        {
            method = "";
            if (string.IsNullOrEmpty(sourceName)) return RefreshSupport.NotFound;

            IntPtr source = IntPtr.Zero;
            try
            {
                source = obs_get_source_by_name(sourceName);
                if (source == IntPtr.Zero) return RefreshSupport.NotFound;

                string? id = Marshal.PtrToStringUTF8(obs_source_get_id(source));
                bool media = id == "ffmpeg_source" || id == "vlc_source";

                // Проверка без обновления идёт по типу источника: свойства некоторых источников (камеры, захват окон)
                // перечисляют устройства и окна, а проверять приходится каждый источник списка сразу
                if (!click) return media || id == BrowserSourceId ? RefreshSupport.Supported : RefreshSupport.Unsupported;

                IntPtr properties = obs_source_properties(source);
                if (properties != IntPtr.Zero)
                {
                    try
                    {
                        IntPtr button = obs_properties_get(properties, RefreshButton);
                        if (button != IntPtr.Zero)
                        {
                            // возвращаемое значение («свойства изменились») не показывает, получилось ли обновление
                            obs_property_button_clicked(button, source);
                            method = "кнопка обновления страницы";
                            return RefreshSupport.Supported;
                        }
                    }
                    finally
                    {
                        obs_properties_destroy(properties);
                    }
                }

                if (media)
                {
                    obs_source_media_restart(source);
                    method = "перезапуск воспроизведения";
                    return RefreshSupport.Supported;
                }

                return RefreshSupport.Unsupported;
            }
            catch (Exception ex)
            {
                // нет obs.dll (проверки вне OBS) или источник пропал посреди вызова
                Plugin.LogError($"Не удалось проверить или обновить источник '{sourceName}': {ex.Message}");
                return RefreshSupport.NotFound;
            }
            finally
            {
                if (source != IntPtr.Zero)
                {
                    try { obs_source_release(source); } catch { }
                }
            }
        }
    }
}
