﻿namespace MFTTransfer.Infrastructure
{
    /// <summary>
    /// Represents key for caching objects
    /// </summary>
    public partial class CacheKey
    {
        #region Ctor

        /// <summary>
        /// Initialize a new instance with key and prefixes
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="prefixes">Prefixes for remove by prefix functionality</param>
        public CacheKey(string key, params string[] prefixes)
        {
            Key = key;
            Prefixes.AddRange(prefixes.Where(prefix => !string.IsNullOrEmpty(prefix)));
        }

        #endregion Ctor

        #region Methods

        /// <summary>
        /// Create a new instance from the current one and fill it with passed parameters
        /// </summary>
        /// <param name="createCacheKeyParameters">Function to create parameters</param>
        /// <param name="keyObjects">Objects to create parameters</param>
        /// <returns>Cache key</returns>
        public virtual CacheKey Create(Func<object, object> createCacheKeyParameters, params object[] keyObjects)
        {
            var cacheKey = new CacheKey(Key, Prefixes.ToArray());

            if (!keyObjects.Any())
                return cacheKey;

            cacheKey.Key = string.Format(cacheKey.Key, keyObjects.Select(createCacheKeyParameters).ToArray());

            for (var i = 0; i < cacheKey.Prefixes.Count; i++)
                cacheKey.Prefixes[i] = string.Format(cacheKey.Prefixes[i], keyObjects.Select(createCacheKeyParameters).ToArray());

            return cacheKey;
        }

        #endregion Methods

        #region Properties

        /// <summary>
        /// Gets or sets a cache key
        /// </summary>
        public string Key { get; protected set; }

        /// <summary>
        /// Gets or sets prefixes for remove by prefix functionality
        /// </summary>
        public List<string> Prefixes { get; protected set; } = new List<string>();

        /// <summary>
        /// Gets or sets a cache time in minutes
        /// </summary>
        public int CacheTime { get; set; } = 30;

        #endregion Properties

        #region Nested classes

        public class CacheKeyEqualityComparer : IEqualityComparer<CacheKey>
        {
            public bool Equals(CacheKey x, CacheKey y)
            {
                if (x == null && y == null)
                    return true;

                return x?.Key.Equals(y?.Key, StringComparison.OrdinalIgnoreCase) ?? false;
            }

            public int GetHashCode(CacheKey obj)
            {
                return obj.Key.GetHashCode();
            }
        }

        #endregion Nested classes
    }
}
