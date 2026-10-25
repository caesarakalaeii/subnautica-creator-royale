using FluentAssertions;
using Lms.Core.Biomes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Biomes;

/// <summary>The survey of <c>overrideBiome</c> values held by atmosphere prefabs, which cannot be extracted offline.</summary>
[TestClass]
public class AtmospherePrefabSurveyTests
{
    private const string Path = "WorldEntities/Atmosphere/FloatingIslands/Islands.prefab";
    private const string ClassId = "f6a02723-61cf-4048-9dac-543b5aa241d3";

    private const string AuroraPath = "WorldEntities/Atmosphere/Ship/CrashedShip_Interior_Cargo.prefab";
    private const string AuroraClassId = "faef77dc-65e2-4fe5-9099-bc3980aba4dd";

    [TestMethod]
    public void A_prefab_naming_a_biome_the_resolver_knows_is_recorded_as_resolved()
    {
        AtmospherePrefabBiome record = AtmospherePrefabBiome.From(ClassId, Path, "kelpForest");

        record.Outcome.Should().Be(AtmospherePrefabOutcome.Resolved);
        record.Resolution!.Biome!.Identifier.Should().Be("kelpForest");
    }

    [TestMethod]
    public void A_biome_string_nothing_can_name_keeps_the_string_rather_than_dropping_it()
    {
        AtmospherePrefabBiome record = AtmospherePrefabBiome.From(AuroraClassId, AuroraPath, "CrashedShip_Interior_Cargo");

        record.Outcome.Should().Be(AtmospherePrefabOutcome.Unresolved);
        record.OverrideBiome.Should().Be("CrashedShip_Interior_Cargo");
    }

    [TestMethod]
    public void The_string_that_started_all_this_resolves_now()
    {
        AtmospherePrefabBiome record = AtmospherePrefabBiome.From(ClassId, Path, "FloatingIsland");

        record.Outcome.Should().Be(AtmospherePrefabOutcome.Resolved);
        record.Resolution!.Biome!.Identifier.Should().Be("floatingIsland");
    }

    [TestMethod]
    public void A_prefab_with_no_biome_of_its_own_is_recorded_as_such_rather_than_skipped()
    {
        AtmospherePrefabBiome.From(ClassId, Path, "").Outcome.Should().Be(AtmospherePrefabOutcome.NoBiome);
        AtmospherePrefabBiome.From(ClassId, Path, null).Outcome.Should().Be(AtmospherePrefabOutcome.NoBiome);
        AtmospherePrefabBiome.From(ClassId, Path, "   ").Outcome.Should().Be(AtmospherePrefabOutcome.NoBiome);
    }

    [TestMethod]
    public void A_prefab_without_an_atmosphere_volume_leaves_the_batch_alone()
    {
        AtmospherePrefabBiome record = AtmospherePrefabBiome.WithoutVolume(ClassId, Path);

        record.Outcome.Should().Be(AtmospherePrefabOutcome.NoVolume);
        record.OverrideBiome.Should().BeNull();
    }

    [TestMethod]
    public void A_prefab_that_would_not_load_is_recorded_rather_than_silently_missing()
    {
        AtmospherePrefabBiome.Missing(ClassId, Path).Outcome.Should().Be(AtmospherePrefabOutcome.PrefabMissing);
    }

    [TestMethod]
    public void A_record_is_one_tab_separated_line_naming_the_prefab_and_the_string()
    {
        string row = AtmospherePrefabBiome.From(AuroraClassId, AuroraPath, "CrashedShip_Interior_Cargo").ToString();

        row.Split('\t').Should().HaveCount(6);
        row.Should().StartWith($"{AuroraPath}\t{AuroraClassId}\tCrashedShip_Interior_Cargo\t");
        row.Should().EndWith("UnknownName");
    }

    [TestMethod]
    public void The_survey_keeps_every_prefab_in_the_order_it_was_read()
    {
        AtmospherePrefabSurvey survey = new AtmospherePrefabSurvey();

        survey.Record(AtmospherePrefabBiome.From(ClassId, "a.prefab", "kelpForest"));
        survey.Record(AtmospherePrefabBiome.From(ClassId, "b.prefab", "CrashedShip_Interior_Cargo"));

        survey.Records.Should().HaveCount(2);
        survey.Records[1].PrefabPath.Should().Be("b.prefab");
    }

    [TestMethod]
    public void The_survey_names_the_biome_strings_nothing_could_resolve()
    {
        AtmospherePrefabSurvey survey = new AtmospherePrefabSurvey();

        survey.Record(AtmospherePrefabBiome.From(ClassId, "a.prefab", "kelpForest"));
        survey.Record(AtmospherePrefabBiome.From(ClassId, "b.prefab", "CrashedShip_Interior_Cargo"));
        survey.Record(AtmospherePrefabBiome.From(ClassId, "c.prefab", "CrashedShip_Interior_Cargo"));

        survey.UnresolvedValues.Should().Equal("CrashedShip_Interior_Cargo");
    }

    [TestMethod]
    public void The_summary_counts_every_outcome_so_it_can_be_read_off_the_log()
    {
        AtmospherePrefabSurvey survey = new AtmospherePrefabSurvey();

        survey.Record(AtmospherePrefabBiome.From(ClassId, "a.prefab", "kelpForest"));
        survey.Record(AtmospherePrefabBiome.From(ClassId, "b.prefab", "CrashedShip_Interior_Cargo"));
        survey.Record(AtmospherePrefabBiome.From(ClassId, "c.prefab", ""));
        survey.Record(AtmospherePrefabBiome.WithoutVolume(ClassId, "d.prefab"));
        survey.Record(AtmospherePrefabBiome.Missing(ClassId, "e.prefab"));

        survey.Summary.Should().Contain("5 atmosphere prefabs")
            .And.Contain("1 resolved")
            .And.Contain("1 unresolved")
            .And.Contain("CrashedShip_Interior_Cargo");
        survey.NoBiomeCount.Should().Be(1);
    }

    [TestMethod]
    public void An_empty_survey_says_so_rather_than_reading_as_a_clean_result()
    {
        new AtmospherePrefabSurvey().Summary.Should().Contain("0 atmosphere prefabs");
    }
}
