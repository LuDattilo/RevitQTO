namespace QtoRevitPlugin.Search
{
    public enum HybridSearchScope
    {
        All,
        ActivePriceList,
        ProjectFavorites,
        PersonalFavorites
    }

    public class ResolvedHybridSearchScope
    {
        public bool UseActivePriceList { get; set; }

        public bool UseProjectFavorites { get; set; }

        public bool UsePersonalFavorites { get; set; }
    }
}
