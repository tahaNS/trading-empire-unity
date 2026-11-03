using System.Linq;
using TradingEmpire.Core;
using Xunit;

public class FibLevelTests
{
    static Drawing Fib(string kind = "fib")
    {
        return DrawingKinds.Make(kind, "#6ea8ff", ("a", new Anchor(1000, 110000)), ("b", new Anchor(2000, 110500)));
    }

    [Fact]
    public void A_new_fibonacci_drawing_shows_its_default_levels()
    {
        Assert.Equal(new[] { 0, 0.236, 0.382, 0.5, 0.618, 0.786, 1 }, DrawingKinds.FibLevelsOf(Fib()));
        Assert.Equal(11, DrawingKinds.FibLevelsOf(Fib("fibtime")).Count);
    }

    [Fact]
    public void Levels_can_be_changed_added_switched_off_coloured_and_deleted_and_they_survive_a_round_trip()
    {
        var d = Fib();
        var levels = DrawingKinds.FibLevelDefs(d);
        levels[1].V = 0.3;                      // change a number
        levels[2].On = false;                   // switch one off
        levels[3].Color = "#f2686b";            // its own colour
        levels.RemoveAt(0);                     // delete one
        levels.Add(new DrawingKinds.FibLevel { V = DrawingKinds.NextLevelValue(levels) });  // add a new one
        DrawingKinds.SetFibLevels(d, levels);

        var shown = DrawingKinds.FibLevelsOn(d);
        Assert.DoesNotContain(shown, l => l.V == 0.382);
        Assert.Contains(shown, l => l.V == 0.3);
        Assert.Equal("#f2686b", shown.First(l => l.V == 0.5).Color);
        Assert.Equal(1.5, shown.Last().V);      // one and a half above the highest (1) - the add rule
        Assert.Equal(shown.OrderBy(l => l.V).Select(l => l.V), shown.Select(l => l.V));

        // saved and loaded: the drawing JSON is kept as it is
        var back = new Drawing(Json.Parse(Json.Serialize(d.Data)));
        Assert.Equal(DrawingKinds.FibLevelDefs(d).Select(l => l.V), DrawingKinds.FibLevelDefs(back).Select(l => l.V));
        Assert.Equal(DrawingKinds.FibLevelDefs(d).Select(l => l.On), DrawingKinds.FibLevelDefs(back).Select(l => l.On));
    }

    [Fact]
    public void Deleting_every_level_leaves_none_and_reset_brings_the_defaults_back()
    {
        var d = Fib();
        DrawingKinds.SetFibLevels(d, new DrawingKinds.FibLevel[0]);
        Assert.Empty(DrawingKinds.FibLevelsOf(d));
        DrawingKinds.SetFibLevels(d, DrawingKinds.DefaultFibValues("fib").Select(v => new DrawingKinds.FibLevel { V = v }));
        Assert.Equal(7, DrawingKinds.FibLevelsOf(d).Count);
    }

    [Fact]
    public void Options_are_on_until_switched_off()
    {
        var d = Fib();
        Assert.True(d.Opt("extend") && d.Opt("fill") && d.Opt("showLevel"));
        d.Data.Set("extend", false);
        Assert.False(d.Opt("extend"));
    }
}
