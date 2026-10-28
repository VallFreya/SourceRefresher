using System.Runtime.InteropServices;

namespace SourceRefresher
{
    /// <summary>
    /// Живой уровень источника OBS. Подключает собственный volmeter к источнику и показывает, найден ли он и активен ли (участвует ли в выводе).
    /// Колбэк volmeter вызывается из аудиопотока OBS, поэтому он только запоминает пик под отдельной блокировкой.
    /// </summary>
    internal sealed class ObsLevelProbe : ISourceProbe
    {
        private const float SilenceDb = -96.0f;
        private const int MaxChannels = 8; // MAX_AUDIO_CHANNELS в libobs
        private const int RetryAttachMs = 1000;

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr obs_get_source_by_name([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern void obs_source_release(IntPtr source);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool obs_source_active(IntPtr source);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern float obs_source_get_volume(IntPtr source);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr obs_volmeter_create(int type);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern void obs_volmeter_destroy(IntPtr volmeter);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool obs_volmeter_attach_source(IntPtr volmeter, IntPtr source);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern void obs_volmeter_detach_source(IntPtr volmeter);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern void obs_volmeter_add_callback(IntPtr volmeter, ProbeCallback callback, IntPtr param);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern void obs_volmeter_remove_callback(IntPtr volmeter, ProbeCallback callback, IntPtr param);

        [DllImport("obs", CallingConvention = CallingConvention.Cdecl)]
        private static extern int obs_volmeter_get_nr_channels(IntPtr volmeter);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ProbeCallback(IntPtr param, IntPtr mag, IntPtr peak, IntPtr inputPeak);

        private static readonly ProbeCallback Callback = OnLevels;

        // Все открытые измерители: при выгрузке плагина их нужно отключить от источников
        private static readonly List<ObsLevelProbe> Open = new List<ObsLevelProbe>();

        private readonly object _stateLock = new object();   // нативные объекты и флаг Disposed
        private readonly object _peakLock = new object();    // пик из колбэка OBS
        private IntPtr _volmeter = IntPtr.Zero;
        private IntPtr _source = IntPtr.Zero;
        private GCHandle _handle;
        private float _peakDb = SilenceDb;
        private long _nextAttachMs;
        private bool _disposed;

        public string SourceName { get; }

        public ObsLevelProbe(string sourceName)
        {
            SourceName = sourceName;
            lock (Open) Open.Add(this);
            lock (_stateLock) TryAttach(Environment.TickCount64);
        }

        /// <summary>Источник найден и к нему подключён измеритель</summary>
        public bool Attached
        {
            get { lock (_stateLock) return _volmeter != IntPtr.Zero; }
        }

        /// <summary>Источник сейчас участвует в выводе (находится в активной сцене). Если источник не найден - false</summary>
        public bool IsActive
        {
            get
            {
                lock (_stateLock)
                {
                    if (_source == IntPtr.Zero) return false;
                    try { return obs_source_active(_source); }
                    catch { return false; }
                }
            }
        }

        /// <summary>Громкость источника в OBS (фейдер) в дБ; тишина или не найден - -96</summary>
        public float VolumeDb
        {
            get
            {
                lock (_stateLock)
                {
                    if (_source == IntPtr.Zero) return SilenceDb;
                    try
                    {
                        float volume = obs_source_get_volume(_source);
                        return volume <= 0.0f ? SilenceDb : 20.0f * MathF.Log10(volume);
                    }
                    catch { return SilenceDb; }
                }
            }
        }

        /// <summary>Самый громкий уровень с прошлого вызова, дБ. Заодно повторяет подключение, если источника ещё не было</summary>
        public float TakePeakDb()
        {
            lock (_stateLock)
            {
                if (_disposed) return SilenceDb;
                if (_volmeter == IntPtr.Zero) TryAttach(Environment.TickCount64);
            }

            lock (_peakLock)
            {
                float peak = _peakDb;
                _peakDb = SilenceDb;
                return peak;
            }
        }

        private void TryAttach(long now)
        {
            if (_disposed || _volmeter != IntPtr.Zero || now < _nextAttachMs) return;
            _nextAttachMs = now + RetryAttachMs;

            IntPtr source = IntPtr.Zero;
            IntPtr volmeter = IntPtr.Zero;
            try
            {
                source = obs_get_source_by_name(SourceName);
                if (source == IntPtr.Zero) return;

                volmeter = obs_volmeter_create(0);
                if (volmeter == IntPtr.Zero || !obs_volmeter_attach_source(volmeter, source))
                {
                    if (volmeter != IntPtr.Zero) obs_volmeter_destroy(volmeter);
                    obs_source_release(source);
                    return;
                }

                _handle = GCHandle.Alloc(this);
                _volmeter = volmeter;
                _source = source;
                obs_volmeter_add_callback(volmeter, Callback, GCHandle.ToIntPtr(_handle));
            }
            catch
            {
                // нет obs.dll (проверка вне OBS) или источник пропал во время подключения
                if (volmeter != IntPtr.Zero && _volmeter == IntPtr.Zero)
                {
                    try { obs_volmeter_destroy(volmeter); } catch { }
                }
                if (source != IntPtr.Zero && _source == IntPtr.Zero)
                {
                    try { obs_source_release(source); } catch { }
                }
            }
        }

        // Из аудиопотока OBS: блокировку состояния брать нельзя (Dispose держит её, ожидая этот колбэк)
        private static unsafe void OnLevels(IntPtr param, IntPtr mag, IntPtr peak, IntPtr inputPeak)
        {
            IntPtr levels = peak != IntPtr.Zero ? peak : mag;
            if (levels == IntPtr.Zero || param == IntPtr.Zero) return;
            if (GCHandle.FromIntPtr(param).Target is not ObsLevelProbe probe) return;

            IntPtr volmeter = probe._volmeter;
            if (volmeter == IntPtr.Zero) return;

            int channels = obs_volmeter_get_nr_channels(volmeter);
            if (channels <= 0 || channels > MaxChannels) channels = 2;

            // OBS отдаёт уровни в дБ, тишина приходит как -inf
            float* values = (float*)levels;
            float maxDb = SilenceDb;
            for (int i = 0; i < channels; i++)
            {
                float db = float.IsFinite(values[i]) ? values[i] : SilenceDb;
                if (db > maxDb) maxDb = db;
            }

            lock (probe._peakLock)
            {
                if (maxDb > probe._peakDb) probe._peakDb = maxDb;
            }
        }

        public void Dispose()
        {
            lock (Open) Open.Remove(this);

            lock (_stateLock)
            {
                if (_disposed) return;
                _disposed = true;

                try
                {
                    if (_volmeter != IntPtr.Zero)
                    {
                        // после remove_callback колбэк гарантированно не работает, handle можно освобождать
                        obs_volmeter_remove_callback(_volmeter, Callback, GCHandle.ToIntPtr(_handle));
                        obs_volmeter_detach_source(_volmeter);
                        obs_volmeter_destroy(_volmeter);
                    }
                }
                catch { }
                finally
                {
                    _volmeter = IntPtr.Zero;
                    if (_handle.IsAllocated) _handle.Free();
                }

                try
                {
                    if (_source != IntPtr.Zero) obs_source_release(_source);
                }
                catch { }
                finally
                {
                    _source = IntPtr.Zero;
                }
            }
        }

        /// <summary>Отключает все измерители (выгрузка плагина при открытом окне настроек)</summary>
        public static void DisposeAll()
        {
            ObsLevelProbe[] probes;
            lock (Open) probes = Open.ToArray();
            foreach (var probe in probes) probe.Dispose();
        }
    }
}
