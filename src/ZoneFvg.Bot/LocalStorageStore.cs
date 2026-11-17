using System;
using cAlgo.API;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Bot
{
    /// <summary>
    /// Key-value port backed by cTrader local storage (architecture section 3). Values are kept in
    /// the Instance scope, so the four symbol instances never read each other's values, and every
    /// write is flushed at once. Local storage works with AccessRights.None; in backtests it is kept
    /// in memory only. The core treats the store as a convenience, never as a source of truth
    /// (spec 8.1).
    /// </summary>
    internal sealed class LocalStorageStore : IKeyValueStore
    {
        // Local storage guide (works with AccessRights.None; scopes; saved every minute and on stop;
        // in memory only in backtests): https://help.ctrader.com/ctrader-algo/guides/local-storage/
        // LocalStorage reference: https://help.ctrader.com/ctrader-algo/references/Localstorage/LocalStorage
        private const LocalStorageScope Scope = LocalStorageScope.Instance;

        private readonly LocalStorage _storage;

        /// <summary>Creates the store over the cBot's local storage (NWS-006, NWS-007).</summary>
        /// <param name="storage">The cBot's <c>LocalStorage</c>.</param>
        public LocalStorageStore(LocalStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        /// <summary>Returns the value stored under the key in the Instance scope, or null (NWS-006, NWS-007).</summary>
        /// <param name="key">The key.</param>
        /// <returns>The stored value, or null.</returns>
        public string? Get(string key)
        {
            // GetString(string, LocalStorageScope): reads only the given scope. Keys may hold only
            // Latin letters, digits and inner spaces, so the port key is encoded (StorageKey).
            return _storage.GetString(StorageKey.Encode(key), Scope);
        }

        /// <summary>Stores the value in the Instance scope and flushes it to disk at once (NWS-006, NWS-007).</summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        public void Set(string key, string value)
        {
            _storage.SetString(StorageKey.Encode(key), value, Scope);
            _storage.Flush(Scope);
        }
    }
}
