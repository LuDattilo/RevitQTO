namespace QtoRevitPlugin.Search
{
    public class HybridSearchScopeResolver
    {
        public ResolvedHybridSearchScope Resolve(HybridSearchScope scope, bool hasActivePriceList)
        {
            switch (scope)
            {
                case HybridSearchScope.ActivePriceList:
                    return new ResolvedHybridSearchScope
                    {
                        UseActivePriceList = hasActivePriceList
                    };
                case HybridSearchScope.ProjectFavorites:
                    return new ResolvedHybridSearchScope
                    {
                        UseProjectFavorites = true
                    };
                case HybridSearchScope.PersonalFavorites:
                    return new ResolvedHybridSearchScope
                    {
                        UsePersonalFavorites = true
                    };
                default:
                    return new ResolvedHybridSearchScope
                    {
                        UseActivePriceList = hasActivePriceList,
                        UseProjectFavorites = true,
                        UsePersonalFavorites = true
                    };
            }
        }
    }
}
