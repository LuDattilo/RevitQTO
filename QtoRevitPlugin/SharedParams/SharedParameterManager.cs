using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QtoRevitPlugin.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QtoRevitPlugin.SharedParams
{
    /// <summary>
    /// Crea, bindo e riconcilia i Shared Parameter del plugin CME su un documento Revit.
    /// Sprint 3 task 3 — risolve anche Sprint 2 task 7 garantendo che <c>QTO_AltezzaLocale</c>
    /// sia presente sui Rooms/Spaces prima che <c>RoomExtractor</c> ne legga il valore.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Verifica</b>: questa classe è deliberatamente <b>senza unit test</b> — l'intera superficie
    /// API richiede mock non triviali di <see cref="Document"/> / <see cref="Autodesk.Revit.ApplicationServices.Application"/>
    /// che non portano valore rispetto al costo di mantenimento. Verifica empirica a deploy:
    /// aprire Revit, aprire un .rvt, chiamare <see cref="EnsureAllQtoParametersBound"/> via
    /// un external command ad hoc, controllare Manage → Project Parameters.
    /// </para>
    /// <para>
    /// <b>Thread safety</b>: tutta la logica gira in <see cref="Transaction"/> Revit e DEVE essere
    /// invocata dal thread Revit (idealmente via <c>RevitTask.RunAsync</c>). Il chiamante è responsabile.
    /// </para>
    /// <para>
    /// <b>Idempotenza</b>: <see cref="EnsureAllQtoParametersBound"/> può essere chiamata N volte senza effetti
    /// collaterali. Se il SP è già bindato sul documento (match su GUID), viene skippato. Se manca nel file
    /// .txt SP, viene creato. Se il file .txt SP non esiste, viene creato con il template header.
    /// </para>
    /// </remarks>
    public class SharedParameterManager
    {
        /// <summary>
        /// Nome di default del file shared parameter gestito dal plugin.
        /// Si trova in <c>%APPDATA%\QtoPlugin\SharedParams\QTO_SharedParams.txt</c>.
        /// </summary>
        public const string SharedParamFileName = "QTO_SharedParams.txt";

        private readonly UIApplication _uiApp;
        private readonly string _spFilePath;

        /// <summary>
        /// Costruisce il manager usando il path di default
        /// (<c>%APPDATA%\QtoPlugin\SharedParams\QTO_SharedParams.txt</c>).
        /// </summary>
        public SharedParameterManager(UIApplication uiApp)
            : this(uiApp, GetDefaultSharedParamFilePath())
        {
        }

        /// <summary>Override per test o per path custom (es. file SP condiviso su rete aziendale).</summary>
        public SharedParameterManager(UIApplication uiApp, string sharedParamFilePath)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            if (string.IsNullOrWhiteSpace(sharedParamFilePath))
                throw new ArgumentException("Path SP file non valido.", nameof(sharedParamFilePath));
            _spFilePath = sharedParamFilePath;
        }

        /// <summary>
        /// Path assoluto del file .txt shared parameter usato dal manager.
        /// </summary>
        /// <remarks>
        /// Per ora il file è salvato solo su filesystem locale. In Sprint 3 (task ES) verrà
        /// salvato anche dentro l'Extensible Storage del documento (schema <see cref="QtoConstants.EsSchemaV1"/>)
        /// per portabilità cross-PC: al primo apertura su un nuovo PC, il plugin rigenera il .txt
        /// dal blob ES e lo scrive in AppData.
        /// </remarks>
        public string SharedParameterFilePath => _spFilePath;

        /// <summary>
        /// Idempotente. Crea (se serve) il file .txt SP, popola con tutte le definizioni in
        /// <see cref="QtoParameterDefinitions.All"/> e le binda al documento con
        /// <see cref="InstanceBinding"/> sulle <see cref="QtoSharedParam.TargetCategories"/> di ogni param.
        /// </summary>
        /// <param name="doc">Documento Revit attivo. Transaction gestita internamente.</param>
        /// <returns>Elenco dei <c>name</c> dei parametri effettivamente creati o bindati in questa invocazione
        /// (esclude quelli già presenti). Vuoto = tutto già OK, no-op.</returns>
        public IReadOnlyList<string> EnsureAllQtoParametersBound(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var touched = new List<string>();
            var app = _uiApp.Application;

            // Salva e ripristina il SP file attivo dell'utente per non sporcare il suo workflow.
            var previousSpFile = app.SharedParametersFilename;

            try
            {
                EnsureSharedParameterFileExists(_spFilePath);
                app.SharedParametersFilename = _spFilePath;

                var spFile = app.OpenSharedParameterFile();
                if (spFile == null)
                {
                    CrashLogger.Warn($"SharedParameterManager: OpenSharedParameterFile ritorna null per {_spFilePath}");
                    return touched;
                }

                using var tx = new Transaction(doc, "CME · Bind shared parameters");
                tx.Start();

                foreach (var qtoParam in QtoParameterDefinitions.All)
                {
                    if (BindSingleParameter(app, doc, spFile, qtoParam))
                    {
                        touched.Add(qtoParam.Name);
                    }
                }

                tx.Commit();
            }
            finally
            {
                // Ripristina il SP file precedente (se c'era) — evita di modificare lo stato dell'app.
                try
                {
                    app.SharedParametersFilename = previousSpFile ?? string.Empty;
                }
                catch
                {
                    // best effort
                }
            }

            return touched;
        }

        /// <summary>Ritorna true se un SP con questo GUID è già bindato al documento.</summary>
        public bool IsParameterBound(Document doc, Guid paramGuid)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var iter = doc.ParameterBindings.ForwardIterator();
            iter.Reset();
            while (iter.MoveNext())
            {
                if (iter.Key is ExternalDefinition ext && ext.GUID == paramGuid)
                    return true;
            }
            return false;
        }

        // =====================================================================
        // Internals
        // =====================================================================

        /// <summary>
        /// Binda un singolo <see cref="QtoSharedParam"/> al documento. Crea
        /// l'<see cref="ExternalDefinition"/> nel file .txt se mancante.
        /// Ritorna true se il parametro è stato creato o bindato (false = già presente).
        /// </summary>
        private bool BindSingleParameter(
            Autodesk.Revit.ApplicationServices.Application app,
            Document doc,
            DefinitionFile spFile,
            QtoSharedParam qtoParam)
        {
            // 1) ExternalDefinition nel .txt — reuse by GUID se già presente, altrimenti create.
            var definition = GetOrCreateExternalDefinition(spFile, qtoParam);
            if (definition == null)
            {
                CrashLogger.Warn($"SharedParameterManager: definition null per {qtoParam.Name}");
                return false;
            }

            // 2) Skip se già bindato (riconoscimento via GUID — nome può collidere con altri SP).
            if (IsParameterBound(doc, qtoParam.Guid))
            {
                return false;
            }

            // 3) CategorySet dalle TargetCategories.
            var catSet = app.Create.NewCategorySet();
            foreach (var bic in qtoParam.TargetCategories)
            {
                var cat = GetCategorySafe(doc, bic);
                if (cat != null)
                    catSet.Insert(cat);
            }

            if (catSet.IsEmpty)
            {
                CrashLogger.Warn($"SharedParameterManager: nessuna categoria valida per {qtoParam.Name}");
                return false;
            }

            // 4) Binding: Instance vs Type.
            Binding binding = qtoParam.IsInstance
                ? (Binding)app.Create.NewInstanceBinding(catSet)
                : (Binding)app.Create.NewTypeBinding(catSet);

            // 5) Insert nel documento. Uso ReInsert come fallback per robustezza:
            //    Insert ritorna false se il param (per nome/GUID) esisteva già ma con binding diverso.
            var ok = doc.ParameterBindings.Insert(definition, binding, qtoParam.BindGroup);
            if (!ok)
            {
                ok = doc.ParameterBindings.ReInsert(definition, binding, qtoParam.BindGroup);
            }

            if (!ok)
            {
                CrashLogger.Warn($"SharedParameterManager: Insert/ReInsert fallito per {qtoParam.Name}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Restituisce la <see cref="ExternalDefinition"/> corrispondente al <paramref name="qtoParam"/>.
        /// Se manca il gruppo, lo crea. Se manca la definition, la crea con il GUID stabile.
        /// </summary>
        private static ExternalDefinition? GetOrCreateExternalDefinition(
            DefinitionFile spFile,
            QtoSharedParam qtoParam)
        {
            var group = spFile.Groups.get_Item(qtoParam.Group) ?? spFile.Groups.Create(qtoParam.Group);
            if (group == null) return null;

            // La ricerca per nome è l'unica strada esposta dall'API. Il GUID viene
            // scritto nel file .txt solo con ExternalDefinitionCreationOptions.GUID.
            var existing = group.Definitions.get_Item(qtoParam.Name) as ExternalDefinition;
            if (existing != null && existing.GUID == qtoParam.Guid)
            {
                return existing;
            }

            // Se esiste un omonimo con GUID diverso (es. da setup precedente broken), restituiamo
            // quello trovato: Revit non ci permette di riscrivere lo stesso nome con GUID diverso
            // nella stessa sessione, e un nuovo GUID orfanerebbe i dati già taggati.
            if (existing != null) return existing;

#if REVIT2025_OR_LATER
            var opts = new ExternalDefinitionCreationOptions(qtoParam.Name, qtoParam.SpecTypeId)
            {
                GUID = qtoParam.Guid,
                Description = qtoParam.Description,
                Visible = true,
            };
#else
            var opts = new ExternalDefinitionCreationOptions(qtoParam.Name, qtoParam.ParameterType)
            {
                GUID = qtoParam.Guid,
                Description = qtoParam.Description,
                Visible = true,
            };
#endif
            return group.Definitions.Create(opts) as ExternalDefinition;
        }

        /// <summary>
        /// Ricava la <see cref="Category"/> corrispondente al <see cref="BuiltInCategory"/>, gestendo le
        /// categorie non disponibili nel documento corrente (es. discipline non attive) senza crash.
        /// </summary>
        private static Category? GetCategorySafe(Document doc, BuiltInCategory bic)
        {
            try
            {
                return doc.Settings.Categories.get_Item(bic);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Assicura che la directory esista e che il file .txt SP sia presente (vuoto con header se nuovo).
        /// </summary>
        private static void EnsureSharedParameterFileExists(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(path))
            {
                // Header minimale compatibile con il formato file SP Revit.
                // Revit popolerà il corpo automaticamente al primo Definitions.Create.
                File.WriteAllText(path,
                    "# This is a Revit shared parameter file.\r\n" +
                    "# Generated by CME plugin — do not edit manually.\r\n" +
                    "# Author: Luigi Dattilo\r\n");
            }
        }

        /// <summary>Path di default: <c>%APPDATA%\QtoPlugin\SharedParams\QTO_SharedParams.txt</c>.</summary>
        public static string GetDefaultSharedParamFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "QtoPlugin", "SharedParams", SharedParamFileName);
        }
    }
}
