using System;
using TimersTimer = System.Timers.Timer;
using ElapsedEventArgs = System.Timers.ElapsedEventArgs;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Autosalvataggio periodico della sessione attiva (timer 5 min) + flush immediato
    /// triggerabile da ogni operazione di tagging (InsertCompleted → Flush esplicito).
    /// Il flush su SessionManager è &lt; 5ms grazie al singolo UPDATE sulla sessione.
    /// </summary>
    public class AutoSaveService : IDisposable
    {
        private const double DefaultIntervalMs = 5 * 60 * 1000; // 5 minuti

        private readonly SessionManager _sessionManager;
        private readonly TimersTimer _timer;
        private bool _disposed;

        public AutoSaveService(SessionManager sessionManager, double intervalMs = DefaultIntervalMs)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _timer = new TimersTimer(intervalMs) { AutoReset = true };
            _timer.Elapsed += OnTimerElapsed;
        }

        public event EventHandler? AutoSaved;

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        /// <summary>Flush esplicito da chiamare dopo ogni operazione di scrittura modello.</summary>
        public void FlushNow()
        {
            if (!_sessionManager.HasActiveSession) return;
            _sessionManager.Flush();
            AutoSaved?.Invoke(this, EventArgs.Empty);
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                FlushNow();
            }
            catch
            {
                // L'AutoSave non deve crashare il plugin. Errori loggati ma non propagati.
                // TODO Sprint 0.1: integrare con logger centrale
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _timer.Stop();
            _timer.Elapsed -= OnTimerElapsed;
            _timer.Dispose();
            _disposed = true;
        }
    }
}
