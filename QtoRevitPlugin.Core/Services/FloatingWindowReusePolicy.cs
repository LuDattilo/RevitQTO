namespace QtoRevitPlugin.Services
{
    public enum FloatingWindowReuseAction
    {
        Create,
        ShowHidden,
        ActivateVisible
    }

    public static class FloatingWindowReusePolicy
    {
        public static FloatingWindowReuseAction Decide(bool hasWindow, bool isVisible)
        {
            if (!hasWindow)
                return FloatingWindowReuseAction.Create;

            return isVisible
                ? FloatingWindowReuseAction.ActivateVisible
                : FloatingWindowReuseAction.ShowHidden;
        }
    }
}
