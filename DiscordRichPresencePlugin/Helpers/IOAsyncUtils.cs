using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DiscordRichPresencePlugin.Helpers
{
    /// <summary>
    /// Async file I/O helpers compatible with .NET Framework 4.7.2 (no File.WriteAllTextAsync).
    /// </summary>
    public static class IOAsyncUtils
    {
        /// <summary>
        /// Asynchronously writes text to a file using StreamWriter. UTF-8 by default.
        /// </summary>
        public static async Task WriteAllTextAsync(string path, string contents, Encoding encoding = null)
        {
            // Avoid C# 8 '??=' to stay compatible with C# 7.3 / net472
            if (encoding == null)
            {
                encoding = Encoding.UTF8;
            }
            // Ensure directory exists
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            using (var writer = new StreamWriter(path, false, encoding))
            {
                await writer.WriteAsync(contents).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Asynchronously reads all text from a file using StreamReader. UTF-8 by default.
        /// </summary>
        public static async Task<string> ReadAllTextAsync(string path, Encoding encoding = null)
        {
            if (encoding == null)
            {
                encoding = Encoding.UTF8;
            }
            using (var reader = new StreamReader(path, encoding))
            {
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
        }
    }
}