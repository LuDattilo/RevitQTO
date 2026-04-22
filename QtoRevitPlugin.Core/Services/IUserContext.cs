using System;

namespace QtoRevitPlugin.Services
{
    public interface IUserContext
    {
        string UserId { get; }
    }

    public class WindowsUserContext : IUserContext
    {
        public string UserId => Environment.UserName;
    }
}
