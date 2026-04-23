namespace QtoRevitPlugin.Models
{
    public class WorkflowAvailability
    {
        public bool CanOpenSetup { get; set; }

        public bool CanOpenListino { get; set; }

        public bool CanOpenSelection { get; set; }

        public string PrimaryMessage { get; set; } = "";

        public string SecondaryMessage { get; set; } = "";
    }
}
