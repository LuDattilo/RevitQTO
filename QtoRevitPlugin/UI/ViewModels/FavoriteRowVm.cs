using CommunityToolkit.Mvvm.ComponentModel;
using QtoRevitPlugin.Models;

namespace QtoRevitPlugin.UI.ViewModels
{
    /// <summary>
    /// Riga UI della sezione Preferiti. Espone <c>IsFavorite</c> osservabile per
    /// sincronizzare l'icona ★/☆ nella griglia risultati di ricerca quando
    /// l'utente aggiunge/rimuove un preferito.
    /// </summary>
    public partial class FavoriteRowVm : ObservableObject
    {
        public UserFavorite Model { get; }
        public int Id => Model.Id;
        public string Code => Model.Code;
        public string Description => Model.Description;
        public string Unit => Model.Unit;
        public double UnitPrice => Model.UnitPrice;
        public string UnitPriceFormatted => Model.UnitPrice.ToString("#,##0.00 €");
        public string ListName => Model.ListName;
        public string AddedAtShort => Model.AddedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        [ObservableProperty] private bool _isFavorite = true;

        public FavoriteRowVm(UserFavorite model) => Model = model;
    }
}
