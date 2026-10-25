using FluentAssertions;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Configuration;

/// <summary>The <c>caster:</c> config section.</summary>
[TestClass]
public class CasterConfigTests
{
    private const string Minimum = @"
match:
  cycleLength: 5m
  warningLead: 1m
zone:
  damageInterval: 2s
  opening:
    centre: [0, -100, 0]
    radius: 1400
    damagePerTick: 1
  phases:
    - centre: [0, -100, 0]
      radius: 900
      shrinkFor: 90s
      damagePerTick: 3
";

    private static TournamentConfig Load(string extra = "")
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(Minimum + extra);

        result.Problems.Should().BeEmpty();

        return result.Config!;
    }

    private static TournamentConfigLoadResult Refused(string extra) =>
        TournamentConfigLoader.Load(Minimum + extra);

    [TestMethod]
    public void A_file_that_says_nothing_gets_the_whole_caster_build()
    {
        CasterPolicy caster = Load().Caster;

        caster.Nameplates.Should().BeTrue();
        caster.FollowDistance.Should().Be(CasterShot.OpeningDistance);
        caster.FollowHeight.Should().Be(CasterShot.OpeningHeight);
        caster.FollowClosest.Should().Be(CasterShot.ClosestKeep);
        caster.FollowFurthest.Should().Be(CasterShot.FurthestKeep);
    }

    [TestMethod]
    public void Every_knob_is_read()
    {
        CasterPolicy caster = Load(@"
caster:
  nameplates: false
  followDistance: 20
  followHeight: 7
  followClosest: 5
  followFurthest: 60
").Caster;

        caster.Nameplates.Should().BeFalse();
        caster.FollowDistance.Should().Be(20);
        caster.FollowHeight.Should().Be(7);
        caster.FollowClosest.Should().Be(5);
        caster.FollowFurthest.Should().Be(60);
    }

    [TestMethod]
    public void One_key_does_not_reset_the_others()
    {
        CasterPolicy caster = Load(@"
caster:
  nameplates: false
").Caster;

        caster.Nameplates.Should().BeFalse();
        caster.FollowDistance.Should().Be(CasterShot.OpeningDistance);
    }

    [TestMethod]
    public void The_shot_may_be_framed_from_below()
    {
        Load(@"
caster:
  followHeight: -6
").Caster.FollowHeight.Should().Be(-6);
    }

    [TestMethod]
    public void A_band_that_is_the_wrong_way_round_is_refused_against_the_field_to_change()
    {
        TournamentConfigLoadResult result = Refused(@"
caster:
  followClosest: 50
  followFurthest: 10
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle()
            .Which.Path.Should().Be("caster.followFurthest");
    }

    [TestMethod]
    public void A_distance_of_nothing_is_refused()
    {
        Refused(@"
caster:
  followDistance: 0
").Problems.Should().ContainSingle().Which.Path.Should().Be("caster.followDistance");
    }

    [TestMethod]
    public void A_key_this_build_does_not_know_is_refused()
    {
        Refused(@"
caster:
  nameplate: true
").Problems.Should().ContainSingle().Which.Path.Should().Be("caster.nameplate");
    }

    [TestMethod]
    public void A_number_that_is_not_one_is_refused_rather_than_read_as_zero()
    {
        Refused(@"
caster:
  followHeight: sideways
").Problems.Should().ContainSingle().Which.Path.Should().Be("caster.followHeight");
    }
}
