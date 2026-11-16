using System.Reflection;
using Xunit;
using Xunit.Sdk;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-008, review finding M3: the parameter hash in every order comment must change when any
/// signal or execution parameter changes, or a restart after a parameter change would adopt orders
/// placed under other rules. The list in <see cref="ParameterHash.Canonical"/> is written by hand;
/// these tests find every public property by reflection, so a new parameter that is not added to
/// the hash fails here instead of silently.
/// </summary>
public class ParameterHashCompletenessTests
{
    public static IEnumerable<object[]> AllParameters()
    {
        foreach (PropertyInfo property in Properties(typeof(SignalParameters)).Concat(Properties(typeof(ExecutionParameters))))
        {
            yield return new object[] { property.DeclaringType!.Name, property.Name };
        }
    }

    private static IEnumerable<PropertyInfo> Properties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name, StringComparer.Ordinal);
    }

    private static object Changed(PropertyInfo property, object value)
    {
        return value switch
        {
            int i => i + 1,
            double d => d + 0.125,
            bool b => !b,
            TimeSpan t => t + TimeSpan.FromMinutes(1.0),
            Enum e => NextEnumValue(e),
            _ => throw new XunitException("No change rule for " + property.Name + " of type " + property.PropertyType.Name + ": add one to this test."),
        };
    }

    private static object NextEnumValue(Enum value)
    {
        Array values = Enum.GetValues(value.GetType());
        int index = Array.IndexOf(values, value);
        return values.GetValue((index + 1) % values.Length)!;
    }

    [Theory]
    [Trait("Req", "EXE-008")]
    [MemberData(nameof(AllParameters))]
    public void Canonical_EveryPublicParameterChanged_ChangesTheTextAndTheHash(string group, string name)
    {
        var signal = new SignalParameters();
        var execution = new ExecutionParameters();
        object target = group == nameof(SignalParameters) ? signal : execution;
        PropertyInfo property = target.GetType().GetProperty(name)!;
        string before = ParameterHash.Canonical(signal, execution);
        string hashBefore = ParameterHash.Compute(signal, execution);

        property.SetValue(target, Changed(property, property.GetValue(target)!));

        Assert.Contains(name + "=", before, StringComparison.Ordinal);
        Assert.NotEqual(before, ParameterHash.Canonical(signal, execution));
        Assert.NotEqual(hashBefore, ParameterHash.Compute(signal, execution));
    }

    [Fact]
    [Trait("Req", "EXE-008")]
    public void Canonical_HasOneEntryPerPublicParameterAndNoOther()
    {
        string[] entries = ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters())
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Substring(0, entry.IndexOf('=')))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] properties = Properties(typeof(SignalParameters)).Concat(Properties(typeof(ExecutionParameters)))
            .Select(p => p.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(properties, entries);
    }
}
