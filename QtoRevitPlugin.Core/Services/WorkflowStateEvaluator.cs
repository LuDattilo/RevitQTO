using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.Services
{
    public class WorkflowStateEvaluator
    {
        public WorkflowAvailability Evaluate(bool hasActiveSession, bool hasActivePriceList)
        {
            if (!hasActiveSession)
            {
                return new WorkflowAvailability
                {
                    PrimaryMessage = "Per iniziare serve un computo attivo",
                    SecondaryMessage = "Crea o apri un file .cme per attivare il workflow CME"
                };
            }

            return new WorkflowAvailability
            {
                CanOpenSetup = true,
                CanOpenListino = true,
                CanOpenSelection = hasActivePriceList,
                PrimaryMessage = hasActivePriceList
                    ? "Workflow pronto per selezione e tagging"
                    : "Attiva un listino per procedere con la selezione",
                SecondaryMessage = hasActivePriceList
                    ? "Puoi procedere con selezione, tagging e verifica"
                    : "Importa o attiva un listino prima di selezionare gli elementi"
            };
        }
    }
}
