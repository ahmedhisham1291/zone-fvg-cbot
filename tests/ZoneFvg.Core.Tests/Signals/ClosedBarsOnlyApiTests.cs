using System.Reflection;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-001: no API of the signal layer accepts or exposes a forming bar. There is no test that
/// can prove the absence of something directly, so this class scans the public surface of the
/// Signals and Indicators namespaces by reflection and fails on any member that looks like an
/// entry point for unfinished data: a tick handler, a parameter called forming, current or
/// partial, or a type or member whose name says Forming, Partial or Tick. The scan runs on
/// whatever the assembly contains, so types added by later work packages are checked too.
/// </summary>
public class ClosedBarsOnlyApiTests
{
    private const BindingFlags PublicDeclared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string[] ScannedNamespaces = { "ZoneFvg.Core.Signals", "ZoneFvg.Core.Indicators" };
    private static readonly string[] ForbiddenParameterNames = { "forming", "current", "partial", "tick" };
    private static readonly string[] ForbiddenNameParts = { "Forming", "Partial", "Tick" };

    [Fact]
    [Trait("Req", "SIG-001")]
    public void PublicApi_OfSignalsAndIndicators_HasNoEntryPointForAFormingBar()
    {
        IReadOnlyList<Type> types = ScannedTypes();

        Assert.Empty(Violations(types));
    }

    [Fact]
    [Trait("Req", "SIG-001")]
    public void PublicApi_Scan_CoversTheTypesThatTakeBars()
    {
        // Guards against a scan that passes because it looked at nothing.
        IReadOnlyList<Type> types = ScannedTypes();

        Assert.Contains(typeof(BarStore), types);
        Assert.Contains(typeof(SignalParameters), types);
        Assert.Contains(typeof(WarmupCalculator), types);
        Assert.Contains(typeof(Atr), types);
        Assert.Contains(typeof(Ema), types);
        Assert.Contains(typeof(SwingDetector), types);
        Assert.Contains(typeof(BiasCalculator), types);
        Assert.Contains(AllParameters(types), parameter => parameter.ParameterType == typeof(Bar));
        Assert.Contains(AllParameters(types), parameter => parameter.ParameterType == typeof(IReadOnlyList<Bar>));
    }

    [Fact]
    [Trait("Req", "SIG-001")]
    public void PublicApi_Scan_ReportsEveryKindOfLeak()
    {
        // The scan is run against a deliberately wrong API to show that it would catch each leak.
        List<string> violations = Violations(new[] { typeof(LeakyApi) });

        Assert.Contains(violations, violation => violation.Contains("OnTick", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("Update(forming)", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("Refresh(currentBar)", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("Apply(partial)", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("Peek(current)", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("FormingBar", StringComparison.Ordinal));
        Assert.Equal(6, violations.Count);
    }

    [Fact]
    [Trait("Req", "SIG-001")]
    public void PublicApi_Scan_AcceptsACleanApi()
    {
        Assert.Empty(Violations(new[] { typeof(CleanApi) }));
    }

    private static IReadOnlyList<Type> ScannedTypes()
    {
        return typeof(BuildInfo).Assembly
            .GetExportedTypes()
            .Where(type => IsScanned(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsScanned(Type type)
    {
        string? typeNamespace = type.Namespace;
        if (typeNamespace is null)
        {
            return false;
        }

        foreach (string scanned in ScannedNamespaces)
        {
            if (typeNamespace == scanned || typeNamespace.StartsWith(scanned + ".", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static List<MethodBase> PublicMethods(Type type)
    {
        var methods = new List<MethodBase>();
        foreach (MethodInfo method in type.GetMethods(PublicDeclared))
        {
            if (!method.IsSpecialName)
            {
                methods.Add(method);
            }
        }

        foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            methods.Add(constructor);
        }

        return methods;
    }

    private static List<ParameterInfo> AllParameters(IEnumerable<Type> types)
    {
        var parameters = new List<ParameterInfo>();
        foreach (Type type in types)
        {
            foreach (MethodBase method in PublicMethods(type))
            {
                parameters.AddRange(method.GetParameters());
            }
        }

        return parameters;
    }

    private static List<string> Violations(IEnumerable<Type> types)
    {
        var found = new List<string>();
        foreach (Type type in types)
        {
            if (HasForbiddenPart(type.Name))
            {
                found.Add(type.Name);
            }

            foreach (MethodBase method in PublicMethods(type))
            {
                if (HasForbiddenPart(method.Name))
                {
                    found.Add(type.Name + "." + method.Name);
                }

                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    string name = parameter.Name ?? string.Empty;
                    if (IsForbiddenParameterName(name) || HasForbiddenPart(parameter.ParameterType.ToString()))
                    {
                        found.Add(type.Name + "." + method.Name + "(" + name + ")");
                    }
                }
            }

            foreach (PropertyInfo property in type.GetProperties(PublicDeclared))
            {
                if (HasForbiddenPart(property.Name) || HasForbiddenPart(property.PropertyType.ToString()))
                {
                    found.Add(type.Name + "." + property.Name);
                }
            }
        }

        return found;
    }

    private static bool HasForbiddenPart(string name)
    {
        foreach (string part in ForbiddenNameParts)
        {
            if (name.Contains(part, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsForbiddenParameterName(string name)
    {
        if (name.Contains("forming", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string forbidden in ForbiddenParameterNames)
        {
            bool exact = name.Equals(forbidden, StringComparison.OrdinalIgnoreCase);
            bool withBarSuffix = name.Equals(forbidden + "Bar", StringComparison.OrdinalIgnoreCase);
            if (exact || withBarSuffix)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An API that breaks SIG-001 in six ways. It exists only to test the scan.</summary>
    private sealed class LeakyApi
    {
        public Bar? FormingBar => null;

        public void OnTick(double bid)
        {
        }

        public void Update(Bar forming)
        {
        }

        public void Refresh(Bar currentBar)
        {
        }

        public void Apply(Bar @partial)
        {
        }

        public Bar? Peek(int current)
        {
            return null;
        }
    }

    /// <summary>An API in the style of the real one: closed bars in, values out.</summary>
    private sealed class CleanApi
    {
        public int Count => 0;

        public void Add(TimeFrame timeFrame, Bar bar)
        {
        }

        public double? Value(IReadOnlyList<Bar> bars, int endIndex, int period)
        {
            return null;
        }
    }
}
