using Autodesk.Revit.DB;
using System;
using System.IO;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Servizio per aggiungere Shared Parameters al progetto Revit attivo, con binding
    /// alla categoria <c>OST_ProjectInformation</c> come <see cref="InstanceBinding"/>.
    /// Usato (in futuro) dalla scheda Informazioni Progetto quando l'utente clicca
    /// "+ Aggiungi parametro condiviso" per creare un nuovo campo di intestazione
    /// legato al ProjectInformation.
    ///
    /// <para>Scelta file SP:</para>
    /// <list type="bullet">
    ///   <item><b>Project SP</b>: usa il file corrente di
    ///   <see cref="RevitApp.SharedParametersFilename"/>.
    ///   Se il progetto non ha ancora un file impostato, fallback sul CME dedicato.</item>
    ///   <item><b>CME dedicated</b>: usa un file
    ///   <c>%AppData%\QtoPlugin\CME_SharedParameters.txt</c> creato al primo uso.
    ///   Consigliato se più progetti condividono i campi CME.</item>
    /// </list>
    ///
    /// <para><b>Nota conflict namespace</b>: <c>Autodesk.Revit.ApplicationServices.Application</c>
    /// entra in conflitto col namespace <c>QtoRevitPlugin.Application</c>. Uso un alias
    /// <c>RevitApp</c> per rendere esplicito che stiamo parlando della Application Revit.</para>
    /// </summary>
    public static class SharedParameterWriterService
    {
        /// <summary>Nome del DefinitionGroup creato/riusato per tutti i parametri CME.</summary>
        public const string CmeGroupName = "CME";

        /// <summary>
        /// Risolve il path del file SP in base all'input:
        /// - Se <paramref name="explicitPath"/> è non-vuoto → usa quello.
        /// - Altrimenti se il progetto ha un SharedParametersFilename impostato → usa quello.
        /// - Altrimenti fallback sul file dedicato CME (<see cref="SharedParameterFileHelper.GetCmeSpFilePath"/>).
        /// </summary>
        public static string ResolveSpFilePath(RevitApp app, string? explicitPath)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (!string.IsNullOrWhiteSpace(explicitPath)) return explicitPath!;

            var projectSp = app.SharedParametersFilename;
            if (!string.IsNullOrWhiteSpace(projectSp) && File.Exists(projectSp)) return projectSp;

            return SharedParameterFileHelper.GetCmeSpFilePath();
        }

        /// <summary>
        /// Crea (o riusa se già esiste) uno Shared Parameter di tipo Text nel gruppo "CME"
        /// del file SP specificato, e lo lega a <see cref="BuiltInCategory.OST_ProjectInformation"/>
        /// come Instance Parameter nel gruppo <see cref="GroupTypeId.IdentityData"/>.
        ///
        /// <para>Idempotente: se il parametro è già bindato, non throw. Ritorna il nome
        /// del parametro su successo così il chiamante può chiamare subito
        /// <c>doc.ProjectInformation.LookupParameter(name)</c>.</para>
        /// </summary>
        /// <param name="doc">Documento Revit attivo (non null).</param>
        /// <param name="spFilePath">
        /// Path del file SP. Se null/vuoto, usa <see cref="ResolveSpFilePath"/>.
        /// </param>
        /// <param name="paramName">Nome esatto del parametro (es. "CME_RUP"). Case-sensitive.</param>
        /// <param name="description">Descrizione opzionale visibile in Revit UI (&lt;=255 char).</param>
        /// <returns>Nome del parametro creato/bindato.</returns>
        /// <exception cref="InvalidOperationException">Se il file SP non è valido o la transazione fallisce.</exception>
        public static string CreateAndBindProjectInfoParam(
            Document doc,
            string? spFilePath,
            string paramName,
            string? description = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(paramName))
                throw new ArgumentException("Il nome del parametro non può essere vuoto.", nameof(paramName));

            var app = doc.Application;

            // 1. Risolvi il file SP da usare e garantiscine l'esistenza
            var resolvedPath = ResolveSpFilePath(app, spFilePath);
            SharedParameterFileHelper.EnsureSpFileExists(resolvedPath);
            app.SharedParametersFilename = resolvedPath;

            var spFile = app.OpenSharedParameterFile();
            if (spFile == null)
            {
                throw new InvalidOperationException(
                    $"Impossibile aprire il file Shared Parameters '{resolvedPath}'. " +
                    "Verifica che il file abbia l'header corretto (#This is a Revit shared parameter file.).");
            }

            // 2. Trova o crea il DefinitionGroup "CME"
            DefinitionGroup group;
            try
            {
                group = spFile.Groups.get_Item(CmeGroupName) ?? spFile.Groups.Create(CmeGroupName);
            }
            catch
            {
                group = spFile.Groups.Create(CmeGroupName);
            }

            // 3. Trova o crea la ExternalDefinition
            var existingDef = group.Definitions.get_Item(paramName) as ExternalDefinition;
            Definition definition;
            if (existingDef != null)
            {
                definition = existingDef;
            }
            else
            {
                var opts = new ExternalDefinitionCreationOptions(paramName, SpecTypeId.String.Text)
                {
                    Visible = true,
                    UserModifiable = true,
                    Description = description ?? $"Campo CME — {paramName}"
                };
                definition = group.Definitions.Create(opts);
            }

            // 4. Binding Instance su OST_ProjectInformation dentro una transazione
            using var tx = new Transaction(doc, $"CME — binding SP {paramName}");
            tx.Start();
            try
            {
                var catSet = app.Create.NewCategorySet();
                var projInfoCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_ProjectInformation);
                catSet.Insert(projInfoCat);

                var binding = app.Create.NewInstanceBinding(catSet);

                var groupId = GroupTypeId.IdentityData;
                bool ok = doc.ParameterBindings.Insert(definition, binding, groupId);
                if (!ok)
                {
                    // Se già presente, ReInsert aggiorna il binding (eventualmente estende
                    // a nuove categorie, nel nostro caso mantiene solo ProjectInformation).
                    ok = doc.ParameterBindings.ReInsert(definition, binding, groupId);
                }

                if (!ok)
                {
                    throw new InvalidOperationException(
                        $"Revit ha rifiutato il binding del parametro '{paramName}' a ProjectInformation.");
                }

                tx.Commit();
            }
            catch
            {
                if (tx.HasStarted()) tx.RollBack();
                throw;
            }

            return paramName;
        }
    }
}
