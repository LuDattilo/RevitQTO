using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit.Async;
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Scrive il timestamp heartbeat <see cref="RecoveryService.LastSyncParamName"/> su
    /// <c>ProjectInformation</c>. Ad ogni scrittura riuscita di un QtoHandler, chiamare:
    ///
    ///   await QtoLastSyncWriter.TouchAsync(uiApp);
    ///
    /// Il param Shared viene creato al primo uso in modo idempotente. Il thread Revit è
    /// garantito da <see cref="RevitTask.RunAsync"/> (no API calls from VM thread).
    /// </summary>
    public static class QtoLastSyncWriter
    {
        private const string GroupName = "QTO_Parameters";
        private const string TempSpFile = "QTO_SharedParams.txt";

        public static Task TouchAsync(UIApplication uiApp)
        {
            return RevitTask.RunAsync(app =>
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                using var tx = new Transaction(doc, "QTO LastSync heartbeat");
                tx.Start();
                EnsureParameterBound(app.Application, doc);
                var param = doc.ProjectInformation.LookupParameter(RecoveryService.LastSyncParamName);
                param?.Set(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                tx.Commit();
            });
        }

        private static void EnsureParameterBound(Autodesk.Revit.ApplicationServices.Application app, Document doc)
        {
            var projInfo = doc.ProjectInformation;
            if (projInfo == null) return;

            // Se già legato, niente da fare
            var existing = projInfo.LookupParameter(RecoveryService.LastSyncParamName);
            if (existing != null) return;

            var spFile = EnsureSharedParameterFile(app);
            if (spFile == null) return;

            var group = spFile.Groups.get_Item(GroupName) ?? spFile.Groups.Create(GroupName);

            Definition def;
            var existingDef = group.Definitions.get_Item(RecoveryService.LastSyncParamName);
            if (existingDef != null)
            {
                def = existingDef;
            }
            else
            {
                var opts = new ExternalDefinitionCreationOptions(
                    RecoveryService.LastSyncParamName, SpecTypeId.String.Text);
                def = group.Definitions.Create(opts);
            }

            var catSet = app.Create.NewCategorySet();
            catSet.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_ProjectInformation));
            var binding = app.Create.NewInstanceBinding(catSet);
            doc.ParameterBindings.Insert(def, binding, GroupTypeId.Data);
        }

        private static DefinitionFile? EnsureSharedParameterFile(
            Autodesk.Revit.ApplicationServices.Application app)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "QtoPlugin");
            Directory.CreateDirectory(folder);
            var spPath = Path.Combine(folder, TempSpFile);

            if (!File.Exists(spPath))
                File.WriteAllText(spPath, "# QTO Shared Parameters\r\n");

            app.SharedParametersFilename = spPath;
            return app.OpenSharedParameterFile();
        }
    }
}
