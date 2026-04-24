using System;

namespace QtoRevitPlugin.Services.Computi
{
    /// <summary>
    /// Eccezione per violazioni delle regole di dominio (es. parent di livello sbagliato,
    /// codice duplicato, riferimento orfano). Da NON usare per errori di persistenza
    /// (DB/SQL) che restano SqliteException.
    /// </summary>
    public class DomainValidationException : Exception
    {
        public string EntityType { get; }
        public string RuleCode { get; }

        public DomainValidationException(string entityType, string ruleCode, string message)
            : base(message)
        {
            EntityType = entityType;
            RuleCode = ruleCode;
        }
    }
}
