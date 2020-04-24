namespace Microsoft.Extensions.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Class Configuration extensions.
    /// </summary>
    public static class ConfigurationExtensions
    {
        /// <summary>
        /// Gets all key values as a list of KeyValuePairs.
        /// </summary>
        /// <param name="rootConfig">The root configuration to get flattened configuration for.</param>
        /// <param name="providersToSkip">Types pf IConfigProviders to skip.</param>
        /// <returns>KeyValuePair&lt;System.String, System.String&gt;[].</returns>
        public static KeyValuePair<string, string>[] GetAllSettings(this IConfiguration rootConfig, Type[] providersToSkip = null)
        {
            return rootConfig.InternalGetAllKeyValues(providersToSkip);
        }

        /// <summary>
        /// Gets the configuration keys and values represented as a string.
        /// Provider name is also shown and if no settings exist within a provider, it is not appended to the string.
        /// </summary>
        /// <param name="rootConfig">The root configuration to generate a string for.</param>
        /// <param name="providersToSkip">Types pf IConfigProviders to skip.</param>
        /// <returns>System.String representation of the configuration.</returns>
        public static string GetAllSettingsAsString(this IConfiguration rootConfig, Type[] providersToSkip = null)
        {
            var keys = rootConfig.InternalGetAllKeyValues(providersToSkip, true).Select(s =>
            {
                // If this is the provider node, format appropriately.
                if (s.Key == "PROV")
                {
                    return s.Value;
                }

                // Otherwise, tab the KeyValue format and return.
                return $"   [{s.Key}]: {s.Value}";
            });

            // All keys are returned with a newline between each.
            return string.Join(Environment.NewLine, keys);
        }

        /// <summary>
        /// Internal method for generating all key values.
        /// </summary>
        /// <param name="rootConfig">The root configuration to get flattened configuration for.</param>
        /// <param name="providersToSkip">Types pf IConfigProviders to skip.</param>
        /// <param name="includeProviders">if set to <c>true</c> [include providers] the provider nodes are added to the output (used for string geneartion).</param>
        /// <returns>KeyValuePair&lt;System.String, System.String&gt;[].</returns>
        private static KeyValuePair<string, string>[] InternalGetAllKeyValues(this IConfiguration rootConfig,
            Type[] providersToSkip = null, bool includeProviders = false)
        {
            var prov = new List<KeyValuePair<string, string>>();

            // If providers have been configured, then build the flattened list of settings.
            if (rootConfig is ConfigurationRoot configRoot && configRoot.Providers != null)
            {
                foreach (var provider in configRoot.Providers.Where(p => providersToSkip == null || !providersToSkip.Contains(p.GetType())))
                {
                    var settingKeys = GetKeyNames(new List<string>(), provider, null);

                    // If providers are to be included AND there are settings, add the provider node.
                    if (includeProviders && settingKeys.Count > 0)
                    {
                        prov.Add(new KeyValuePair<string, string>("PROV",
                            $"{Environment.NewLine}{provider.GetType().Name} [{settingKeys.Count} setting(s)]"));
                    }

                    // Append each config value, using the keys to identity each.
                    foreach (var settingKey in settingKeys)
                    {
                        provider.TryGet(settingKey, out var val);
                        prov.Add(new KeyValuePair<string, string>(settingKey, val));
                    }
                }
            }
            return prov.ToArray();
        }

        /// <summary>
        /// Pulls the config from the provider, using the path passed in.
        /// </summary>
        /// <param name="keyList">The key list built with the unique keys.</param>
        /// <param name="provider">The provider to build config from.</param>
        /// <param name="path">The path to find the config for.</param>
        /// <returns>List&lt;System.String&gt; of all config keys.</returns>
        private static List<string> GetKeyNames(List<string> keyList, IConfigurationProvider provider, string path)
        {
            // Grab distinct keys to parse for children.
            var distinctKeys = provider.GetChildKeys(new List<string>(), path).Distinct();

            foreach (var key in distinctKeys)
            {
                // Full path of key.
                var fullPath = GetFullKeyPath(path, key);

                // If there are children of this config node, then recursively call this method again, otherwise add to key path.
                var hasChildren = provider.GetChildKeys(new List<string>(), fullPath).Any();

                if (hasChildren)
                {
                    AddChildKeys(keyList, provider, fullPath);
                }
                else
                {
                    keyList.Add(fullPath);
                }
            }

            return keyList;
        }

        /// <summary>
        /// Adds the child keys.
        /// </summary>
        /// <param name="keyList">The key list.</param>
        /// <param name="provider">The provider.</param>
        /// <param name="path">The path.</param>
        private static void AddChildKeys(List<string> keyList, IConfigurationProvider provider, string path)
        {
            // Ensure keys are unique before adding new key.
            if (!keyList.Contains(path))
            {
                var kvConfigs = GetKeyNames(keyList, provider, path);
                foreach (var kv in kvConfigs)
                {
                    if (!keyList.Contains(kv))
                    {
                        keyList.Add(kv);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the full key path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="key">The key.</param>
        /// <returns>System.String.</returns>
        private static string GetFullKeyPath(string path, string key)
        {
            return (string.IsNullOrEmpty(path) ? null : path) +
                           (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(key) ? ":" : null) +
                           (string.IsNullOrEmpty(key) ? null : key);
        }
    }
}
