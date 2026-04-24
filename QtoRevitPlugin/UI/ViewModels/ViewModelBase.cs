using CommunityToolkit.Mvvm.ComponentModel;
using QtoRevitPlugin.Services;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace QtoRevitPlugin.UI.ViewModels
{
    public abstract partial class ViewModelBase : ObservableObject
    {
        [ObservableProperty] private bool _isBusy;

        protected async Task<bool> SetBusy(Func<Task> action, string context = "")
        {
            if (IsBusy) return false;
            IsBusy = true;
            try
            {
                await action();
                return true;
            }
            catch (Exception ex)
            {
                HandleError(ex, string.IsNullOrEmpty(context) ? GetType().Name : context);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        protected static void HandleError(Exception ex, string context)
        {
            CrashLogger.WriteException(context, ex);
            try
            {
                MessageBox.Show(
                    $"Errore: {ex.Message}\n\nDettagli salvati in %AppData%\\QtoPlugin\\startup.log",
                    "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        }

        protected static void RunOnUi(Action action)
        {
            var app = Application.Current;
            if (app?.Dispatcher == null) { action(); return; }
            if (app.Dispatcher.CheckAccess()) action();
            else app.Dispatcher.Invoke(action);
        }
    }
}
