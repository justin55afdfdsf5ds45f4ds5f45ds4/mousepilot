using System;
using System.Collections.Generic;
using MousePilot;

internal static class GestureEngineTests
{
    private static int assertions;

    private static GestureRule Rule(string id, int gap, params string[] sequence)
    {
        return new GestureRule { id = id, gapMs = gap, enabled = true, sequence = new List<string>(sequence) };
    }

    private static void Equal(string expected, string actual, string reason)
    {
        assertions++;
        if (expected != actual)
            throw new Exception(reason + ": expected " + (expected ?? "null") + ", got " + (actual ?? "null"));
    }

    public static int Main()
    {
        try { return Run(); }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
    }

    private static int Run()
    {
        GestureMatcher matcher = new GestureMatcher();
        List<GestureRule> rules = new List<GestureRule> { Rule("claude", 500, "left", "left", "left", "right") };
        Equal(null, matcher.Feed("left", 0, rules), "First click stays partial");
        Equal(null, matcher.Feed("left", 200, rules), "Second click stays partial");
        Equal(null, matcher.Feed("left", 400, rules), "Third click stays partial");
        Equal("claude", matcher.Feed("right", 600, rules), "Exact requested sequence completes");
        Equal(null, matcher.Feed("left", 800, rules), "Cooldown ignores prefix");
        Equal(null, matcher.Feed("left", 1000, rules), "Cooldown ignores second click");
        Equal(null, matcher.Feed("left", 1200, rules), "Cooldown ignores third click");
        Equal(null, matcher.Feed("right", 1400, rules), "Cooldown suppresses repeat");
        Equal(null, matcher.Feed("right", 2100, rules), "Cooldown input did not seed a later gesture");

        matcher.Reset();
        rules = new List<GestureRule> { Rule("pair", 500, "left", "right") };
        Equal(null, matcher.Feed("left", 0, rules), "Timeout prefix");
        Equal(null, matcher.Feed("right", 501, rules), "Expired prefix cannot fire");
        Equal(null, matcher.Feed("left", 700, rules), "New prefix after expiry");
        Equal("pair", matcher.Feed("right", 1200, rules), "Gap boundary is inclusive");

        matcher.Reset();
        Equal(null, matcher.Feed("left", 0, rules), "Consecutive prefix");
        Equal(null, matcher.Feed("middle", 100, rules), "Unrelated input interrupts sequence");
        Equal(null, matcher.Feed("right", 200, rules), "Nonconsecutive tokens cannot match");
        Equal(null, matcher.Feed("left", 300, rules), "Fresh prefix after interruption");
        Equal("pair", matcher.Feed("right", 400, rules), "Fresh consecutive sequence matches");

        matcher.Reset();
        rules = new List<GestureRule> { Rule("overlap", 500, "left", "left", "right") };
        matcher.Feed("left", 0, rules);
        matcher.Feed("left", 100, rules);
        Equal(null, matcher.Feed("left", 200, rules), "Repeated prefix remains partial");
        Equal("overlap", matcher.Feed("right", 300, rules), "Overlapping repeated prefix matches suffix");

        matcher.Reset();
        rules = new List<GestureRule> { Rule("short", 500, "right"), Rule("long", 500, "left", "right") };
        matcher.Feed("left", 0, rules);
        Equal("long", matcher.Feed("right", 100, rules), "Longest simultaneous completion wins");
        Equal(null, matcher.Feed("right", 1599, rules), "Global cooldown applies to other rule");
        Equal("short", matcher.Feed("right", 1600, rules), "Cooldown boundary permits new gesture");

        matcher.Reset();
        rules = new List<GestureRule> { Rule("enabled", 500, "middle", "wheelUp") };
        matcher.Feed("middle", 0, rules);
        rules[0].enabled = false;
        Equal(null, matcher.Feed("wheelUp", 100, rules), "Disabled rule cannot fire");
        rules[0].enabled = true;
        Equal(null, matcher.Feed("wheelUp", 200, rules), "Re-enabled rule has no stale prefix");
        matcher.Feed("middle", 300, rules);
        Equal("enabled", matcher.Feed("wheelUp", 400, rules), "Re-enabled rule accepts fresh gesture");
        matcher.Reset();
        matcher.Feed("middle", 500, rules);
        matcher.Reset();
        Equal(null, matcher.Feed("wheelUp", 600, rules), "Reset clears prefixes and cooldown");

        matcher.Reset();
        rules = new List<GestureRule> { Rule("edit", 500, "left", "right") };
        matcher.Feed("left", 0, rules);
        rules[0].sequence = new List<string> { "left", "middle" };
        Equal(null, matcher.Feed("middle", 100, rules), "Editing a sequence clears its earlier prefix");
        matcher.Feed("left", 200, rules);
        Equal("edit", matcher.Feed("middle", 300, rules), "Edited rule accepts a fresh gesture");

        matcher.Reset();
        rules = new List<GestureRule> { Rule("fast", 200, "left", "right"), Rule("slow", 600, "middle", "right") };
        matcher.Feed("left", 0, rules);
        Equal(null, matcher.Feed("right", 300, rules), "Each rule enforces its own timeout");
        matcher.Feed("middle", 400, rules);
        Equal("slow", matcher.Feed("right", 1000, rules), "Longer per-rule timeout remains valid");

        matcher.Reset();
        rules = new List<GestureRule> { Rule("buttons", 500, "back", "forward", "wheelDown", "middle") };
        matcher.Feed("back", 0, rules);
        matcher.Feed("forward", 100, rules);
        matcher.Feed("wheelDown", 200, rules);
        Equal("buttons", matcher.Feed("middle", 300, rules), "Extra buttons and wheel tokens match");

        matcher.Reset();
        rules = new List<GestureRule> { Rule("clock", 500, "left", "right") };
        matcher.Feed("left", 1000, rules);
        Equal(null, matcher.Feed("right", 100, rules), "Backwards clock discards stale prefix");
        matcher.Feed("left", 200, rules);
        Equal("clock", matcher.Feed("right", 300, rules), "Fresh timeline still works");

        matcher.Reset();
        rules = new List<GestureRule> { Rule("too-long", 500, "left", "left", "left", "left", "left", "left", "left", "left", "left") };
        for (int i = 0; i < 10; i++)
            Equal(null, matcher.Feed("left", i * 100, rules), "Rules longer than eight are ignored");
        Console.WriteLine("PASS: " + assertions + " gesture engine assertions.");
        return 0;
    }
}
