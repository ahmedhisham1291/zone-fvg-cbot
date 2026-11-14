namespace ZoneFvg.Core.Execution.Ports;

/// <summary>
/// Persistent key-value storage port (docs/architecture.md section 3). The adapter implements it
/// with the platform's local storage. The news module keeps its cache and its last fetch
/// attempt here, under keys prefixed <c>news.</c> with JSON values (NWS-006, NWS-007).
/// </summary>
public interface IKeyValueStore
{
    /// <summary>Returns the value stored under the key, or null when there is none (NWS-006, NWS-007).</summary>
    /// <param name="key">The key.</param>
    /// <returns>The stored value, or null.</returns>
    string? Get(string key);

    /// <summary>Stores the value under the key, replacing any earlier value (NWS-006, NWS-007).</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    void Set(string key, string value);
}
