using System.Collections;
using System.Reflection;

namespace OmenGamingHubUnlocker.UI;

/// <summary>
/// Renders flexible status tables from loosely typed snapshot objects.
/// </summary>
public static class ConsoleTable
{
    public enum StatusIntent
    {
        Neutral = 0,
        AfterActivate = 1,
        AfterDisable = 2
    }

    /// <summary>
    /// Renders a table of status snapshots while adapting to the actual object shape at runtime.
    /// </summary>
    public static bool PrintStatusTable(
        object snapshots,
        StatusIntent intent = StatusIntent.Neutral,
        bool showResultColumn = true,
        bool predictive = false)
    {
        var tableRows = ExtractRows(snapshots);
        if (tableRows.Count == 0)
        {
            ConsoleHelpers.WriteHint("No status table data.");
            return false;
        }

        var members = GetMembers(tableRows[0]);
        if (members.Count == 0)
        {
            PrintFallbackToString(tableRows);
            return true;
        }

        // The renderer uses reflection so report types can stay small and task-focused.
        var areaMember = PickBestMember(members, AreaScore);
        var itemMember = PickBestMember(members, ItemScore, exclude: areaMember);
        var currentMember = PickBestMember(members, CurrentScore, exclude: areaMember, exclude2: itemMember);

        var levelMember = PickBestMember(members, LevelScore);
        var successMember = PickBestMember(members, SuccessBoolScore);
        var expectedMember = PickBestMember(members, ExpectedScore);
        var errorMember = PickBestMember(members, ErrorScore);

        areaMember ??= members.FirstOrDefault();
        itemMember ??= members.FirstOrDefault(member => member != areaMember);
        currentMember ??= members.FirstOrDefault(member => member != areaMember && member != itemMember);

        var areaValues = tableRows.Select(row => SafeGetString(areaMember, row)).ToList();
        var itemValues = tableRows.Select(row => SafeGetString(itemMember, row)).ToList();
        var currentValues = tableRows.Select(row => SafeGetString(currentMember, row)).ToList();

        var resultValues = new List<string>(tableRows.Count);
        if (showResultColumn)
        {
            for (var index = 0; index < tableRows.Count; index++)
            {
                var row = tableRows[index];

                var levelRaw = SafeGetString(levelMember, row);
                var successRaw = SafeGetString(successMember, row);
                var expectedRaw = SafeGetString(expectedMember, row);
                var errorRaw = SafeGetString(errorMember, row);

                var area = areaValues[index];
                var item = itemValues[index];
                var current = currentValues[index];

                var result = predictive
                    ? ComputePrediction(area, item, current, intent)
                    : ComputeEvaluation(area, item, current, intent, levelRaw, successRaw, expectedRaw, errorRaw);

                resultValues.Add(result);
            }
        }

        for (var index = 0; index < tableRows.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(areaValues[index]))
                areaValues[index] = "General";

            if (string.IsNullOrWhiteSpace(itemValues[index]))
                itemValues[index] = tableRows[index].ToString() ?? "";

            currentValues[index] ??= string.Empty;
        }

        const string colAName = "Area";
        const string colIName = "Item";
        const string colCName = "Current";
        const string colRName = "Result";

        var widthA = Math.Clamp(Math.Max(colAName.Length, areaValues.Max(value => value.Length)), 8, 22);
        var widthI = Math.Clamp(Math.Max(colIName.Length, itemValues.Max(value => value.Length)), 12, 62);
        var widthC = Math.Clamp(Math.Max(colCName.Length, currentValues.Max(value => value.Length)), 10, 70);
        var widthR = showResultColumn
            ? Math.Clamp(Math.Max(colRName.Length, resultValues.Max(value => value.Length)), 8, 22)
            : 0;

        var consoleWidth = Console.WindowWidth;
        if (consoleWidth > 0)
        {
            var total = showResultColumn
                ? (widthA + widthI + widthC + widthR + 9)
                : (widthA + widthI + widthC + 6);

            if (total > consoleWidth - 1)
            {
                var overflow = total - (consoleWidth - 1);
                widthC = Math.Max(24, widthC - overflow);
            }
        }

        Console.WriteLine();

        if (showResultColumn)
        {
            ConsoleHelpers.WithColor(ConsoleColor.Cyan, () =>
            {
                Console.WriteLine($"{colAName.PadRight(widthA)} | {colIName.PadRight(widthI)} | {colCName.PadRight(widthC)} | {colRName.PadRight(widthR)}");
                Console.WriteLine($"{new string('-', widthA)}-+-{new string('-', widthI)}-+-{new string('-', widthC)}-+-{new string('-', widthR)}");
            });

            for (var index = 0; index < tableRows.Count; index++)
            {
                var area = Trunc(areaValues[index], widthA);
                var item = Trunc(itemValues[index], widthI);
                var current = Trunc(currentValues[index], widthC);
                var result = Trunc(resultValues[index], widthR);

                Console.Write(area.PadRight(widthA));
                Console.Write(" | ");
                Console.Write(item.PadRight(widthI));
                Console.Write(" | ");
                Console.Write(current.PadRight(widthC));
                Console.Write(" | ");

                ConsoleHelpers.WithColor(ResultColor(result), () => Console.Write(result.PadRight(widthR)));
                Console.WriteLine();
            }
        }
        else
        {
            ConsoleHelpers.WithColor(ConsoleColor.Cyan, () =>
            {
                Console.WriteLine($"{colAName.PadRight(widthA)} | {colIName.PadRight(widthI)} | {colCName.PadRight(widthC)}");
                Console.WriteLine($"{new string('-', widthA)}-+-{new string('-', widthI)}-+-{new string('-', widthC)}");
            });

            for (var index = 0; index < tableRows.Count; index++)
            {
                var area = Trunc(areaValues[index], widthA);
                var item = Trunc(itemValues[index], widthI);
                var current = Trunc(currentValues[index], widthC);

                Console.Write(area.PadRight(widthA));
                Console.Write(" | ");
                Console.Write(item.PadRight(widthI));
                Console.Write(" | ");
                Console.Write(current.PadRight(widthC));
                Console.WriteLine();
            }
        }

        Console.WriteLine();
        return true;
    }

    // ------------------------------------------------------------
    // Prediction (Dry run) -> "Will ..."
    // ------------------------------------------------------------

    private static string ComputePrediction(string area, string item, string current, StatusIntent intent)
    {
        var eff = intent == StatusIntent.Neutral ? StatusIntent.AfterActivate : intent;

        var a = (area ?? "").Trim().ToLowerInvariant();
        var cur = (current ?? "").Trim();

        if (a.Contains("service"))
        {
            if (eff == StatusIntent.AfterActivate)
                return ContainsAny(cur, "manual") ? "Will keep Manual" : "Will set Manual";

            return ContainsAny(cur, "auto", "automatic", "delayed") ? "Will keep Auto" : "Will set Auto";
        }

        if (a.Contains("task"))
        {
            if (eff == StatusIntent.AfterActivate)
                return ContainsAny(cur, "disabled") ? "Will keep Disabled" : "Will disable";

            return ContainsAny(cur, "enabled") ? "Will keep Enabled" : "Will enable";
        }

        if (a.Contains("firewall"))
        {
            if (TryExtractInt(cur, out var n))
            {
                if (eff == StatusIntent.AfterActivate)
                    return n > 0 ? "Will refresh rules" : "Will create rules";
                return n > 0 ? "Will remove rules" : "No rules to remove";
            }

            return eff == StatusIntent.AfterActivate ? "Will (re)create rules" : "Will remove rules";
        }

        if (a.Contains("hosts"))
        {
            if (eff == StatusIntent.AfterActivate)
                return ContainsAny(cur, "not blocked") ? "Will block" : "Will keep blocked";

            return ContainsAny(cur, "not blocked") ? "No entries to remove" : "Will remove entries";
        }

        if (a.Contains("run") || a.Contains("autostart") || a.Contains("startup"))
        {
            if (eff == StatusIntent.AfterActivate)
                return "Will remove autostart";
            return "Cannot restore (manual)";
        }

        return "Will check";
    }

    // ------------------------------------------------------------
    // Evaluation (Activate/Disable) -> OK/WARN/ERR/INFO
    // ------------------------------------------------------------

    private static string ComputeEvaluation(
        string area,
        string item,
        string current,
        StatusIntent intent,
        string levelRaw,
        string successRaw,
        string expectedRaw,
        string errorRaw)
    {
        // 1) Hard error -> ERR
        if (!string.IsNullOrWhiteSpace(errorRaw))
            return "ERR";

        // Normalize optional fields
        var lv = NormalizeLevel(levelRaw);

        // 2) In AfterActivate/AfterDisable we RE-COMPUTE by intent.
        //    Snapshot "Level/Result" often describes a neutral status (INFO/WARN),
        //    and would incorrectly override Disable=OK. We only respect explicit failure.
        if (intent != StatusIntent.Neutral)
        {
            if (lv == "ERR")
                return "ERR";

            if (TryParseBool(successRaw, out var okFromBool) && !okFromBool)
                return "ERR";

            var byIntent = EvaluateByIntent(area, current, intent);
            if (byIntent is not null)
                return byIntent;

            // If we can't classify by intent, then fall back to snapshot meta
            if (lv is not null)
                return lv;

            if (TryParseBool(successRaw, out okFromBool))
                return okFromBool ? "OK" : "ERR";

            // Expected vs Current (if present)
            var expNorm = NormalizeComparable(expectedRaw);
            var curNorm = NormalizeComparable(current);

            if (!string.IsNullOrWhiteSpace(expNorm) && !string.IsNullOrWhiteSpace(curNorm))
                return string.Equals(expNorm, curNorm, StringComparison.OrdinalIgnoreCase) ? "OK" : "WARN";

            return !string.IsNullOrWhiteSpace(current) ? "INFO" : "INFO";
        }

        // 3) Neutral mode: snapshot meta should win (status screen)
        if (lv is not null)
            return lv;

        if (TryParseBool(successRaw, out var okBool))
            return okBool ? "OK" : "ERR";

        // 4) Expected vs Current — compare (if available)
        var exp = NormalizeComparable(expectedRaw);
        var cur2 = NormalizeComparable(current);

        if (!string.IsNullOrWhiteSpace(exp) && !string.IsNullOrWhiteSpace(cur2))
            return string.Equals(exp, cur2, StringComparison.OrdinalIgnoreCase) ? "OK" : "WARN";

        // 5) Neutral fallback
        return !string.IsNullOrWhiteSpace(current) ? "INFO" : "INFO";
    }

    private static string? EvaluateByIntent(string area, string current, StatusIntent intent)
    {
        var a = (area ?? "").Trim().ToLowerInvariant();
        var cur = (current ?? "").Trim();

        if (a.Contains("service"))
        {
            return intent == StatusIntent.AfterActivate
                ? (ContainsAny(cur, "manual") ? "OK" : "WARN")
                : (ContainsAny(cur, "auto", "automatic", "delayed") ? "OK" : "WARN");
        }

        if (a.Contains("task"))
        {
            return intent == StatusIntent.AfterActivate
                ? (ContainsAny(cur, "disabled") ? "OK" : "WARN")
                : (ContainsAny(cur, "enabled") ? "OK" : "WARN");
        }

        if (a.Contains("firewall"))
        {
            if (TryExtractInt(cur, out var n))
            {
                return intent == StatusIntent.AfterActivate
                    ? (n > 0 ? "OK" : "WARN")
                    : (n == 0 ? "OK" : "WARN");
            }

            return intent == StatusIntent.AfterActivate
                ? (ContainsAny(cur, "blocked", "rule") ? "OK" : "WARN")
                : (ContainsAny(cur, "0", "none", "not found", "no rules", "not blocked") ? "OK" : "WARN");
        }

        if (a.Contains("hosts"))
        {
            return intent == StatusIntent.AfterActivate
                ? (ContainsAny(cur, "blocked", "127.0.0.1", "mapped", "present") ? "OK" : "WARN")
                : (ContainsAny(cur, "not blocked", "absent", "clear", "removed") ? "OK" : "WARN");
        }

        if (a.Contains("run") || a.Contains("autostart") || a.Contains("startup"))
        {
            // Activation removes Run entries, Disable cannot restore -> INFO always (honest).
            return "INFO";
        }

        return null;
    }

    private static bool ContainsAny(string s, params string[] needles)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        foreach (var n in needles)
        {
            if (s.Contains(n, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool TryExtractInt(string s, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;

        if (int.TryParse(s.Trim(), out value))
            return true;

        var digits = new string(s.Where(char.IsDigit).ToArray());
        return digits.Length > 0 && int.TryParse(digits, out value);
    }

    private static string? NormalizeLevel(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim().ToUpperInvariant();

        if (s is "OK" or "SUCCESS" or "PASSED" or "PASS")
            return "OK";
        if (s is "WARN" or "WARNING" or "PARTIAL")
            return "WARN";
        if (s is "INFO" or "NOTE")
            return "INFO";
        if (s is "ERR" or "ERROR" or "FAIL" or "FAILED")
            return "ERR";

        if (s.Contains("ERROR") || s.Contains("FAIL"))
            return "ERR";
        if (s.Contains("WARN"))
            return "WARN";
        if (s.Contains("OK") || s.Contains("SUCCESS"))
            return "OK";

        return null;
    }

    private static bool TryParseBool(string raw, out bool value)
    {
        value = false;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var s = raw.Trim().ToLowerInvariant();

        if (s is "true" or "yes" or "1")
        {
            value = true;
            return true;
        }

        if (s is "false" or "no" or "0")
        {
            value = false;
            return true;
        }

        return bool.TryParse(s, out value);
    }

    private static string NormalizeComparable(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var s = raw.Trim();
        var x = s.ToLowerInvariant();

        if (x is "yes") return "true";
        if (x is "no") return "false";

        return s;
    }

    private static ConsoleColor ResultColor(string res)
    {
        var s = (res ?? "").Trim();

        if (s.StartsWith("Will ", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("No ", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("Cannot ", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Cyan;

        var u = s.ToUpperInvariant();

        return u switch
        {
            "OK" => ConsoleColor.Green,
            "WARN" => ConsoleColor.Yellow,
            "ERR" => ConsoleColor.Red,
            "INFO" => ConsoleColor.Gray,
            _ => ConsoleColor.Gray
        };
    }

    // ------------------------------------------------------------
    // Reflection helpers
    // ------------------------------------------------------------

    private static List<object> ExtractRows(object snapshots)
    {
        if (snapshots is null) return new List<object>();

        if (snapshots is string) return new List<object> { snapshots };

        if (snapshots is IEnumerable enumerable)
        {
            var list = new List<object>();
            foreach (var item in enumerable)
            {
                if (item is null) continue;
                list.Add(item);
            }
            return list;
        }

        return new List<object> { snapshots };
    }

    private sealed record MemberAccessor(string Name, Type Type, Func<object, object?> Get);

    private static List<MemberAccessor> GetMembers(object sample)
    {
        var t = sample.GetType();
        var list = new List<MemberAccessor>();

        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead) continue;
            if (p.GetIndexParameters().Length > 0) continue;

            list.Add(new MemberAccessor(
                p.Name,
                p.PropertyType,
                o =>
                {
                    try { return p.GetValue(o); }
                    catch { return null; }
                }));
        }

        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            list.Add(new MemberAccessor(
                f.Name,
                f.FieldType,
                o =>
                {
                    try { return f.GetValue(o); }
                    catch { return null; }
                }));
        }

        return list;
    }

    private static string SafeGetString(MemberAccessor? m, object row)
    {
        if (m is null) return "";

        try
        {
            var v = m.Get(row);
            if (v is null) return "";

            if (v is bool b) return b ? "True" : "False";
            return (v.ToString() ?? "").Trim();
        }
        catch
        {
            return "";
        }
    }

    private static MemberAccessor? PickBestMember(
        List<MemberAccessor> members,
        Func<string, int> score,
        MemberAccessor? exclude = null,
        MemberAccessor? exclude2 = null)
    {
        MemberAccessor? best = null;
        var bestScore = 0;

        foreach (var m in members)
        {
            if (exclude is not null && ReferenceEquals(m, exclude)) continue;
            if (exclude2 is not null && ReferenceEquals(m, exclude2)) continue;

            var s = score(m.Name);
            if (s > bestScore)
            {
                bestScore = s;
                best = m;
            }
        }

        return bestScore >= 3 ? best : null;
    }

    // Column scoring
    private static int AreaScore(string name)
    {
        var n = name.ToLowerInvariant();
        var score = 0;

        if (n == "area") score += 100;
        if (n.Contains("area")) score += 20;
        if (n.Contains("group")) score += 16;
        if (n.Contains("category")) score += 16;
        if (n.Contains("section")) score += 14;
        if (n.Contains("kind")) score += 12;
        if (n.Contains("scope")) score += 12;

        return score;
    }

    private static int ItemScore(string name)
    {
        var n = name.ToLowerInvariant();
        var score = 0;

        if (n == "item") score += 100;
        if (n.Contains("item")) score += 20;
        if (n.Contains("target")) score += 18;
        if (n.Contains("name")) score += 16;
        if (n.Contains("key")) score += 14;

        if (n.Contains("service")) score += 10;
        if (n.Contains("task")) score += 10;
        if (n.Contains("rule")) score += 10;
        if (n.Contains("domain")) score += 10;
        if (n.Contains("path")) score += 8;

        return score;
    }

    private static int CurrentScore(string name)
    {
        var n = name.ToLowerInvariant();
        var score = 0;

        if (n == "current") score += 100;
        if (n.Contains("current")) score += 20;
        if (n.Contains("state")) score += 18;
        if (n.Contains("status")) score += 18;
        if (n.Contains("value")) score += 16;
        if (n.Contains("mode")) score += 14;
        if (n.Contains("starttype")) score += 12;
        if (n.Contains("enabled")) score += 10;
        if (n.Contains("count")) score += 10;

        return score;
    }

    private static int LevelScore(string name)
    {
        var n = name.ToLowerInvariant();
        var score = 0;

        if (n is "level" or "result" or "severity" or "outcome") score += 100;
        if (n.Contains("level")) score += 25;
        if (n.Contains("result")) score += 25;
        if (n.Contains("severity")) score += 20;

        return score;
    }

    private static int SuccessBoolScore(string name)
    {
        var n = name.ToLowerInvariant();
        var score = 0;

        if (n is "success" or "isok" or "ok" or "compliant" or "matches") score += 100;
        if (n.Contains("success")) score += 22;
        if (n.Contains("isok")) score += 22;
        if (n == "ok") score += 22;

        return score;
    }

    private static int ExpectedScore(string name)
    {
        var n = name.ToLowerInvariant();
        var score = 0;

        if (n is "expected" or "desired" or "shouldbe" or "targetstate") score += 100;
        if (n.Contains("expected")) score += 22;
        if (n.Contains("desired")) score += 20;
        if (n.Contains("should")) score += 18;

        return score;
    }

    private static int ErrorScore(string name)
    {
        var n = name.ToLowerInvariant();
        var score = 0;

        if (n is "error" or "exception") score += 100;
        if (n.Contains("error")) score += 25;
        if (n.Contains("exception")) score += 25;
        if (n.Contains("fail")) score += 20;

        return score;
    }

    private static void PrintFallbackToString(List<object> rows)
    {
        Console.WriteLine();
        ConsoleHelpers.WithColor(ConsoleColor.Cyan, () =>
        {
            Console.WriteLine("Item");
            Console.WriteLine(new string('-', 64));
        });

        foreach (var r in rows)
            Console.WriteLine(r.ToString() ?? "");

        Console.WriteLine();
    }

    private static string Trunc(string s, int width)
    {
        s ??= "";
        if (s.Length <= width) return s;
        if (width <= 3) return s.Substring(0, width);
        return s.Substring(0, width - 3) + "...";
    }
}
