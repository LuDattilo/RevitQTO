using FluentAssertions;
using QtoRevitPlugin.Models;
using Xunit;

namespace QtoRevitPlugin.Tests.Data
{
    /// <summary>
    /// Test per la classe statica <see cref="ProjectInfoFieldKeys"/>.
    /// Guardrail contro rename accidentali dei FieldKey (che romperebbero le
    /// mapping persistite nella tabella RevitParamMapping del .cme).
    /// </summary>
    public class ProjectInfoFieldKeysTests
    {
        [Fact]
        public void All_ContainsElevenFields()
        {
            // La scheda Informazioni Progetto ha 11 campi testuali. Se il conto cambia,
            // aggiornare questo test INSIEME allo schema DB (potenziale migration).
            ProjectInfoFieldKeys.All.Should().HaveCount(11);
        }

        [Fact]
        public void All_FieldKeysAreDistinct()
        {
            ProjectInfoFieldKeys.All.Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public void All_FieldKeysMatchConstants()
        {
            // Protegge dal drift tra le costanti public const string e l'array All.
            ProjectInfoFieldKeys.All.Should().Contain(new[]
            {
                ProjectInfoFieldKeys.DenominazioneOpera,
                ProjectInfoFieldKeys.Committente,
                ProjectInfoFieldKeys.Impresa,
                ProjectInfoFieldKeys.Rup,
                ProjectInfoFieldKeys.DirettoreLavori,
                ProjectInfoFieldKeys.Luogo,
                ProjectInfoFieldKeys.Comune,
                ProjectInfoFieldKeys.Provincia,
                ProjectInfoFieldKeys.Cig,
                ProjectInfoFieldKeys.Cup,
                ProjectInfoFieldKeys.RiferimentoPrezzario
            });
        }

        [Fact]
        public void DisplayNameFor_KnownKeys_ReturnsItalianLabel()
        {
            ProjectInfoFieldKeys.DisplayNameFor(ProjectInfoFieldKeys.DenominazioneOpera)
                .Should().Be("Denominazione opera");
            ProjectInfoFieldKeys.DisplayNameFor(ProjectInfoFieldKeys.DirettoreLavori)
                .Should().Be("Direttore dei Lavori");
            ProjectInfoFieldKeys.DisplayNameFor(ProjectInfoFieldKeys.RiferimentoPrezzario)
                .Should().Be("Riferimento prezzario");
        }

        [Fact]
        public void DisplayNameFor_UnknownKey_ReturnsKeyItself()
        {
            // Fallback sicuro: se arriva un FieldKey non mappato (forse aggiunto in
            // futuro ma dimenticato nello switch), l'UI mostra la stringa grezza
            // invece di crashare o ritornare null.
            ProjectInfoFieldKeys.DisplayNameFor("UnknownField").Should().Be("UnknownField");
        }

        [Fact]
        public void SuggestedSharedParamNameFor_PrefixesWithCme()
        {
            ProjectInfoFieldKeys.SuggestedSharedParamNameFor(ProjectInfoFieldKeys.Rup)
                .Should().Be("CME_RUP");
            ProjectInfoFieldKeys.SuggestedSharedParamNameFor(ProjectInfoFieldKeys.Cig)
                .Should().Be("CME_CIG");
        }

        [Fact]
        public void FieldKeyValues_AreStableStrings()
        {
            // QUESTI VALORI SONO PERSISTITI NEL DB (.cme RevitParamMapping.FieldKey).
            // Cambiare un valore stringa qui sotto richiede una MIGRATION.
            // Se un test deve cambiare, PRIMA aggiorna lo schema + migration, POI il test.
            ProjectInfoFieldKeys.DenominazioneOpera.Should().Be("DenominazioneOpera");
            ProjectInfoFieldKeys.Committente.Should().Be("Committente");
            ProjectInfoFieldKeys.Impresa.Should().Be("Impresa");
            ProjectInfoFieldKeys.Rup.Should().Be("RUP");
            ProjectInfoFieldKeys.DirettoreLavori.Should().Be("DirettoreLavori");
            ProjectInfoFieldKeys.Luogo.Should().Be("Luogo");
            ProjectInfoFieldKeys.Comune.Should().Be("Comune");
            ProjectInfoFieldKeys.Provincia.Should().Be("Provincia");
            ProjectInfoFieldKeys.Cig.Should().Be("CIG");
            ProjectInfoFieldKeys.Cup.Should().Be("CUP");
            ProjectInfoFieldKeys.RiferimentoPrezzario.Should().Be("RiferimentoPrezzario");
        }
    }
}
