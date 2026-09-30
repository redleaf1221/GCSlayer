using Acornima;
using System.Text.RegularExpressions;

namespace GCSlayer.Services;

/// <summary>
/// Removes the compiled copies of the editor generated parameter API files from the game script.
///
/// The IDE writes <c>Game/system/*Params.ts</c> from the custom extension documents and compiles
/// them into <c>Game/GCMain.ts</c> together with the rest of the project. They are pure type
/// declarations: the compiled classes are empty and nothing in the script ever instantiates,
/// extends or otherwise mentions them, so the bundle carries dead weight that the IDE regenerates
/// anyway. After the extension documents are repaired their parameters changed, so the bundled
/// copies are also stale.
///
/// A class is only removed when its name occurs exactly three times in the script, which is the
/// definition itself (var, function, return). A name that occurs anywhere else, or that has a base
/// class (the fourth occurrence is then the __extends call), is left alone.
/// </summary>
public static class GeneratedParameterApiStripper {
    /// <summary>A class the script defines once and never mentions again.</summary>
    private record struct Stripped(string Name, int Start, int End);

    /// <summary>
    /// Class name prefixes of the parameter API files the editor generates. They are taken from the
    /// project's own <c>Game/system/*Params.ts</c> plus the names the editor script declares in
    /// <c>CodeIDELocal</c>, because a project only carries the files the editor has generated so far
    /// (a freshly recovered one carries the template stubs, which do not cover every family).
    /// </summary>
    public static IReadOnlyList<string> ReadParameterApiPrefixes(string projectPath) {
        var prefixes = new SortedSet<string>(StringComparer.Ordinal);
        var systemPath = Path.Combine(projectPath, "Game", "system");
        if (Directory.Exists(systemPath)) {
            foreach (var file in Directory.GetFiles(systemPath, "*Params.ts")) {
                prefixes.Add(Path.GetFileNameWithoutExtension(file) + "_");
                foreach (Match match in Regex.Matches(File.ReadAllText(file), @"^\s*class\s+(\w+)", RegexOptions.Multiline))
                    prefixes.Add(Regex.Replace(match.Groups[1].Value, @"\d+$", string.Empty));
            }
        }

        // CodeIDELocal.CUSTOM_CONDITION_PARAMS = "CustomConditionParams.ts";
        var editorScript = Path.Combine(projectPath, "GameCreator", "script.js");
        if (File.Exists(editorScript)) {
            foreach (Match match in Regex.Matches(File.ReadAllText(editorScript),
                @"""(?<name>\w+Params)\.ts"""))
                prefixes.Add(match.Groups["name"].Value + "_");
        }
        return prefixes.Where(prefix => prefix.Length > 0).ToArray();
    }

    /// <summary>
    /// Returns the script without the compiled classes and the number of removed definitions.
    /// <paramref name="skipped"/> lists names that are still referenced, which are kept.
    /// </summary>
    public static (string Script, int Removed, IReadOnlyList<string> Skipped) Strip(string script,
        IReadOnlyList<string> prefixes) {
        var found = new List<Stripped>();
        var skipped = new List<string>();
        foreach (var name in Members(script, prefixes)) {
            if (CountOccurrences(script, name) != 3) {
                // Defined elsewhere, referenced, or not in this bundle at all.
                if (CountOccurrences(script, name) > 0)
                    skipped.Add(name);
                continue;
            }
            if (FindDefinition(script, name) is { } definition)
                found.Add(definition);
            else
                skipped.Add(name);
        }

        var result = script;
        foreach (var definition in found.OrderByDescending(item => item.Start))
            result = result.Remove(definition.Start, definition.End - definition.Start);

        // Never hand back a script that no longer parses: a wrong cut would break the game.
        if (found.Count > 0 && !Parses(result))
            return (script, 0, prefixes);

        return (result, found.Count, skipped);
    }

    /// <summary>
    /// Every class of the declared families. A bundle compiled from an earlier list state can hold a
    /// class whose number the current generated file no longer declares (an entry that was removed
    /// afterwards), and that one is stale too.
    /// </summary>
    private static IEnumerable<string> Members(string script, IReadOnlyList<string> prefixes) {
        var members = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var prefix in prefixes) {
            foreach (Match match in Regex.Matches(script, @"\b" + Regex.Escape(prefix) + @"\d+\b"))
                members.Add(match.Value);
        }
        return members;
    }

    private static int CountOccurrences(string script, string name) =>
        Regex.Matches(script, @"\b" + Regex.Escape(name) + @"\b").Count;

    /// <summary>Finds "var Name = (function ... ());" including the whole line it starts on.</summary>
    private static Stripped? FindDefinition(string script, string name) {
        var marker = "var " + name + " =";
        var index = script.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return null;

        var open = script.IndexOf('(', index + marker.Length);
        if (open < 0)
            return null;

        var depth = 0;
        for (var i = open; i < script.Length; i++) {
            if (script[i] == '(')
                depth++;
            else if (script[i] == ')' && --depth == 0) {
                var end = i + 1;
                if (end < script.Length && script[end] == ';')
                    end++;
                // Take the indentation and the line break of the definition with it.
                var start = index;
                while (start > 0 && script[start - 1] is ' ' or '\t')
                    start--;
                while (end < script.Length && script[end] is ' ' or '\t')
                    end++;
                if (end < script.Length && script[end] == '\r')
                    end++;
                if (end < script.Length && script[end] == '\n')
                    end++;
                return new Stripped(name, start, end);
            }
        }
        return null;
    }

    /// <summary>Checks the result is still a syntactically valid game script.</summary>
    private static bool Parses(string script) {
        try {
            new Parser(new ParserOptions { EcmaVersion = EcmaVersion.Latest, Tolerant = true }).ParseScript(script);
            return true;
        }
        catch (SyntaxErrorException) {
            return false;
        }
    }
}
