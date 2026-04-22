using QtoRevitPlugin.Models;
using System;
using TimersTimer = System.Timers.Timer;
using ElapsedEventArgs = System.Timers.ElapsedEventArgs;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Autosalvataggio periodico della sessione attiva. Intervallo letto da CmeSettings
    /// (minimo 30 minuti, default 30). Il flush è &lt; 5ms grazie a singolo UPDATE su SQLite.
    /// Reagisce a SettingsChanged per ripicking l'intervallo nuovo senza restart.
    /// </summary>
    public class AutoSaveService : IDisposable
    {
        private readonly SessionManager _sessionManager;
        private readonly TimersTimer _timer;
        private bool _disposed;

        public AutoSaveService(SessionManager sessionManager)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));

            _timer = new TimersTimer(GetIntervalMs()) { AutoReset = true };
            _timer.Elapsed += OnTimerElapsed;

            SettingsService.SettingsChanged += OnSettingsChanged;
        }

        public event EventHandler? AutoSaved;

        public void Start()
        {
            var settings = SettingsService.Load();
            if (!settings.AutoSaveEnabled) return;
            _timer.Interval = GetIntervalMs();
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        /// <summary>Flush esplicito — chiamato da OnSave del menu e dopo ogni scrittura modello.</summary>
        public void FlushNow()
        {
            if (!_sessionManager.HasActiveSession) return;
            _sessionManager.Flush();
            AutoSaved?.Invoke(this, EventArgs.Empty);
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            try { FlushNow(); }
            catch { /* l'autosave non deve mai crashare */ }
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            // Riconfigura timer senza perdere la sessione
            var settings = SettingsService.Load();
            _timer.Interval = GetIntervalMs();
            if (settings.AutoSaveEnabled)
            {
                if (!_timer.Enabled) _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        }

        private static double GetIntervalMs()
        {
            var settings = SettingsService.Load();
            return settings.NormalizedAutoSaveIntervalMinutes * 60_000.0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            SettingsService.SettingsChanged -= OnSettingsChanged;
            _timer.Stop();
            _timer.Elapsed -= OnTimerElapsed;
            _timer.Dispose();
            _disposed = true;
        }
    }
}
