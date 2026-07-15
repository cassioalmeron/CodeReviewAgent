using System.Security.Cryptography;
using System.Text;

namespace CodeReviewerAgent.Core.Infra
{
    /// <summary>Content hashing shared by the repositories (diff content-addressing).</summary>
    internal static class Hashing
    {
        /// <summary>Lowercase hex SHA-256 of the exact UTF-8 bytes of <paramref name="content"/>.</summary>
        public static string Sha256(string content) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }
}
