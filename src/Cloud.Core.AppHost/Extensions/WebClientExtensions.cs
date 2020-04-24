using System.Net;

namespace Cloud.Core.AppHost.Extensions
{
    /// <summary>
    /// Class WebClientExtensions.
    /// </summary>
    public static class WebClientExtensions
    {
        /// <summary>
        /// Gets the external ip address.
        /// </summary>
        /// <value>The external ip address.</value>
        internal static string ExternalIpAddress { get; private set; }

        /// <summary>
        /// Gets the external ip address.
        /// </summary>
        /// <returns>System.String.</returns>
        public static string GetExternalIpAddress()
        {
            // This ensures the address is only looked up once.
            if (ExternalIpAddress == null)
            {
                string result = string.Empty;

                string[] checkIpUrl =
                {
                    "https://ipinfo.io/ip", "https://checkip.amazonaws.com/", "https://api.ipify.org",
                    "https://icanhazip.com", "https://wtfismyip.com/text"
                };

                using (var client = new WebClient())
                {
                    client.Headers["User-Agent"] = "Mozilla/4.0 (Compatible; Windows NT 5.1; MSIE 6.0) " +
                                                   "(compatible; MSIE 6.0; Windows NT 5.1; .NET CLR 1.1.4322; .NET CLR 2.0.50727)";

                    foreach (var url in checkIpUrl)
                    {
                        try
                        {
                            result = client.DownloadString(url);
                        }
                        catch
                        {
                            // Do nothing on exception.
                        }

                        if (!string.IsNullOrEmpty(result))
                            break;
                    }
                }

                ExternalIpAddress = result.Replace("\n", "").Trim();
            }

            return ExternalIpAddress;
        }
    }
}
