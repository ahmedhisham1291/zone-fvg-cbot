using System.Text.RegularExpressions;
using Xunit;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The dependency direction between the namespaces of ZoneFvg.Core (docs/architecture.md section 1,
/// review finding M6). <c>tools/check_architecture.py</c> checks forbidden APIs only; this test reads
/// the core's source files and fails on any namespace dependency that is not in
/// <see cref="Allowed"/>, so a new edge, a cycle or a dependency on App needs a deliberate change
/// here that a reviewer sees.
/// </summary>
/// <remarks>
/// A file depends on a namespace when it has a <c>using</c> of it, names it in full, or (for the
/// namespaces that enclose its own, which C# finds without a <c>using</c>) mentions one of its type
/// names. Comments and string literals are ignored. An unused <c>using</c> counts as a dependency.
/// </remarks>
public class NamespaceDependencyTests
{
    private const string Root = "ZoneFvg.Core";

    /// <summary>
    /// Allowed dependencies, as found on main on 2026-11-16 (review of a4dd2fd, section 1, plus Ops
    /// to Simulation from pull request #31). Root is the namespace of <see cref="BuildInfo"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Allowed = new SortedDictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Root"] = Array.Empty<string>(),
        ["Domain"] = Array.Empty<string>(),
        ["Indicators"] = new[] { "Domain" },
        ["Signals"] = new[] { "Domain", "Indicators" },
        ["Execution.Ports"] = new[] { "Domain" },
        ["Risk"] = new[] { "Domain", "Execution.Ports" },
        ["News"] = new[] { "Domain", "Execution.Ports" },
        ["Simulation"] = new[] { "Domain", "Risk" },
        ["Execution"] = new[] { "Domain", "Execution.Ports", "News", "Risk", "Signals" },
        ["Recovery"] = new[] { "Root", "Domain", "Execution", "Execution.Ports", "Risk", "Signals" },
        ["Ops"] = new[] { "Domain", "Execution", "Execution.Ports", "News", "Risk", "Simulation" },
        ["App"] = new[] { "Root", "Domain", "Execution", "Execution.Ports", "News", "Ops", "Recovery", "Risk", "Signals" },
    };

    private static readonly Regex NamespaceLine = new(@"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex UsingLine = new(@"^\s*using\s+(ZoneFvg\.Core(?:\.\w+)*)\s*;", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex Qualified = new(@"\bZoneFvg\.Core((?:\.\w+)+)", RegexOptions.CultureInvariant);
    private static readonly Regex Literals = new(@"/\*.*?\*/|@""(?:[^""]|"""")*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])'|//[^\n]*", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static string CoreSourceFolder()
    {
        for (DirectoryInfo? folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
        {
            string candidate = Path.Combine(folder.FullName, "src", "ZoneFvg.Core");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("src/ZoneFvg.Core was not found above " + AppContext.BaseDirectory);
    }

    private static string Short(string name)
    {
        return name == Root ? "Root" : name.Substring(Root.Length + 1);
    }

    /// <summary>Short namespace name to the type names declared in it, from the core assembly.</summary>
    private static Dictionary<string, HashSet<string>> TypeNames()
    {
        var names = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (Type type in typeof(BuildInfo).Assembly.GetTypes().Where(t => !t.IsNested && t.Namespace != null && t.Namespace.StartsWith(Root, StringComparison.Ordinal)))
        {
            string key = Short(type.Namespace!);
            if (!names.TryGetValue(key, out HashSet<string>? set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                names.Add(key, set);
            }

            int tick = type.Name.IndexOf('`');
            set.Add(tick < 0 ? type.Name : type.Name.Substring(0, tick));
        }

        return names;
    }

    /// <summary>Every dependency found, as "From -> To", sorted.</summary>
    private static SortedSet<string> FoundEdges()
    {
        Dictionary<string, HashSet<string>> types = TypeNames();
        var edges = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(CoreSourceFolder(), "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            if (path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            string code = Literals.Replace(File.ReadAllText(path), " ");
            Match declared = NamespaceLine.Match(code);
            if (!declared.Success)
            {
                continue;
            }

            string from = Short(declared.Groups[1].Value);
            foreach (string to in Dependencies(code, from, types))
            {
                edges.Add(from + " -> " + to);
            }
        }

        return edges;
    }

    private static IEnumerable<string> Dependencies(string code, string from, Dictionary<string, HashSet<string>> types)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match match in UsingLine.Matches(code))
        {
            found.Add(Short(match.Groups[1].Value));
        }

        foreach (Match match in Qualified.Matches(code))
        {
            string[] parts = match.Groups[1].Value.TrimStart('.').Split('.');
            for (int length = parts.Length; length > 0; length--)
            {
                string candidate = string.Join(".", parts.Take(length));
                if (types.ContainsKey(candidate))
                {
                    found.Add(candidate);
                    break;
                }
            }
        }

        // Enclosing namespaces are visible without a using: Execution.Ports sees Execution and Root.
        string enclosing = from;
        while (enclosing != "Root")
        {
            int dot = enclosing.LastIndexOf('.');
            enclosing = dot < 0 ? "Root" : enclosing.Substring(0, dot);
            if (types.TryGetValue(enclosing, out HashSet<string>? names) && names.Any(name => Regex.IsMatch(code, @"\b" + Regex.Escape(name) + @"\b", RegexOptions.CultureInvariant)))
            {
                found.Add(enclosing);
            }
        }

        found.Remove(from);
        return found;
    }

    [Fact]
    public void CoreNamespaces_DependOnlyInTheAllowedDirection()
    {
        SortedSet<string> edges = FoundEdges();
        string[] unexpected = edges
            .Where(edge =>
            {
                string[] ends = edge.Split(" -> ");
                return !Allowed.TryGetValue(ends[0], out string[]? targets) || !targets.Contains(ends[1], StringComparer.Ordinal);
            })
            .ToArray();

        Assert.True(edges.Count > 20, "Too few dependencies found (" + edges.Count + "); the source scan is broken.");
        Assert.True(unexpected.Length == 0, "Namespace dependencies not allowed by docs/architecture.md section 1: " + string.Join("; ", unexpected));
    }

    [Fact]
    public void CoreNamespaces_EachIsClassified()
    {
        string[] unknown = TypeNames().Keys.Where(name => !Allowed.ContainsKey(name)).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.True(unknown.Length == 0, "New core namespaces need a row in Allowed: " + string.Join(", ", unknown));
    }

    [Fact]
    public void Allowed_HasNoCycleAndNothingDependsOnApp()
    {
        Assert.DoesNotContain(Allowed, pair => pair.Value.Contains("App", StringComparer.Ordinal));
        Assert.Empty(Allowed["Domain"]);
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (string start in Allowed.Keys)
        {
            Visit(start, new List<string>(), done);
        }
    }

    private static void Visit(string node, List<string> path, HashSet<string> done)
    {
        Assert.False(path.Contains(node, StringComparer.Ordinal), "Cycle: " + string.Join(" -> ", path) + " -> " + node);
        if (done.Contains(node))
        {
            return;
        }

        path.Add(node);
        foreach (string next in Allowed[node])
        {
            Visit(next, path, done);
        }

        path.RemoveAt(path.Count - 1);
        done.Add(node);
    }

    [Fact]
    public void FoundEdges_IncludeTheKnownOnes()
    {
        SortedSet<string> edges = FoundEdges();

        foreach (string known in new[] { "Signals -> Indicators", "Recovery -> Root", "App -> Ops", "Risk -> Execution.Ports" })
        {
            Assert.True(edges.Contains(known), "Expected dependency not found: " + known);
        }
    }
}
