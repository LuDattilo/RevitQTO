using System;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Tipo di cambiamento notificato da <c>SessionManager.SessionChanged</c>.
    /// Estratto nel Core per consentire test unitari senza dipendenze Revit —
    /// ViewModel e subscriber consumano questo enum via <see cref="SessionChangedEventArgs"/>.
    /// </summary>
    public enum SessionChangeKind
    {
        Created,
        Resumed,
        Forked,
        Renamed,
        Closed,
        Deleted,
        /// <summary>Fase Revit attiva cambiata (contesto soft-switch phase-bound).</summary>
        PhaseChanged
    }

    /// <summary>
    /// Args dell'evento SessionChanged: fornisce la sessione attiva e il tipo
    /// di cambiamento per permettere ai subscriber di reagire selettivamente
    /// (es. il tagging si aggiorna solo su PhaseChanged, non su Renamed).
    /// </summary>
    public class SessionChangedEventArgs : EventArgs
    {
        public WorkSession Session { get; }
        public SessionChangeKind Kind { get; }

        public SessionChangedEventArgs(WorkSession session, SessionChangeKind kind)
        {
            Session = session;
            Kind = kind;
        }
    }
}
