using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace QtoRevitPlugin.Services
{
    /// <summary>
    /// Pure static utility for computing a deterministic SHA256-based hash
    /// from a Revit element's uniqueId and a set of parameter values.
    /// No Revit API dependency — usable in tests without Revit.
    /// </summary>
    public static class ElementHasher
    {
        /// <summary>
        /// SHA256 of uniqueId + paramValues, truncated to 12 uppercase hex chars.
        /// </summary>
        public static string ComputeHash(string uniqueId, List<(string paramName, double value)> paramValues)
        {
            var sb = new StringBuilder();
            sb.Append(uniqueId);
            foreach (var (name, value) in paramValues)
            {
                sb.Append(name);
                sb.Append(value.ToString("F6"));
            }

#if NETSTANDARD2_0 || NET48
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return BitConverter.ToString(bytes).Replace("-", "").Substring(0, 12).ToUpperInvariant();
#else
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(bytes)[..12];
#endif
        }
    }
}
