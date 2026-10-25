using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Combat;

/// <summary>Every weapon has a word, a family and a number, and every refusal has its wire and spoken words.</summary>
[TestClass]
public class StrikeVocabularyTests
{
    private const StrikeWeapon NoSuchWeapon = (StrikeWeapon)99;

    private const StrikeRefusal NoSuchRefusal = (StrikeRefusal)99;

    private static StrikePolicy PolicyFor(StrikeWeapon weapon) =>
        StrikeWeapons.Family(weapon) == WeaponFamily.PrawnArm
            ? new PrawnPolicy(true, clawDamage: 20, drillDamage: 4, reach: 9,
                minInterval: TimeSpan.FromMilliseconds(800))
            : (StrikePolicy)new BladePolicy(true, knifeDamage: 12, heatBladeDamage: 20, reach: 6,
                minInterval: TimeSpan.FromMilliseconds(400));

    [TestMethod]
    public void Every_weapon_has_a_word_a_family_and_a_number()
    {
        foreach (StrikeWeapon weapon in Enum.GetValues<StrikeWeapon>())
        {
            string word = StrikeWeapons.Word(weapon);
            word.Should().NotBeNullOrWhiteSpace(weapon.ToString());

            StrikeWeapons.TryParse(word, out StrikeWeapon read).Should().BeTrue(word);
            read.Should().Be(weapon, "a word that does not read back is a swing this build refuses");

            Enum.IsDefined(typeof(WeaponFamily), StrikeWeapons.Family(weapon)).Should().BeTrue();

            Action number = () => PolicyFor(weapon).Damage(weapon);
            number.Should().NotThrow("the policy the server picks from a weapon's own family has to " +
                                     "have a number for it, and throws rather than answering zero");
        }
    }

    [TestMethod]
    public void No_two_weapons_share_a_word()
    {
        IEnumerable<string> words = Enum.GetValues<StrikeWeapon>().Select(StrikeWeapons.Word);

        words.Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public void No_two_refusals_share_a_word()
    {
        IEnumerable<string> words = Enum.GetValues<StrikeRefusal>().Select(StrikeRefusals.Word);

        words.Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public void Every_refusal_has_a_sentence_of_its_own()
    {
        IEnumerable<string> sentences = Enum.GetValues<StrikeRefusal>().Select(StrikeRefusals.Sentence);

        sentences.Should().OnlyHaveUniqueItems();
        sentences.Should().OnlyContain(sentence => !string.IsNullOrWhiteSpace(sentence));
    }

    [TestMethod]
    public void Every_refusal_has_something_to_put_on_the_attacker_s_screen()
    {
        foreach (StrikeRefusal refusal in Enum.GetValues<StrikeRefusal>())
        {
            StrikeRefusals.Sentence(refusal).Should().NotBeNullOrWhiteSpace(refusal.ToString());
            StrikeRefusals.TryParse(StrikeRefusals.Word(refusal), out StrikeRefusal read).Should().BeTrue();
            read.Should().Be(refusal);
        }
    }

    [TestMethod]
    public void A_weapon_this_build_has_no_word_for_throws_rather_than_defaulting()
    {
        Action word = () => StrikeWeapons.Word(NoSuchWeapon);

        word.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("weapon");
    }

    [TestMethod]
    public void A_weapon_with_no_family_throws_rather_than_guessing_one()
    {
        Action family = () => StrikeWeapons.Family(NoSuchWeapon);

        family.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("weapon");
    }

    [TestMethod]
    public void A_policy_asked_for_a_weapon_that_is_not_its_own_throws()
    {
        Action prawnAsked = () => PrawnPolicy.Default.Damage(NoSuchWeapon);
        Action bladeAsked = () => BladePolicy.Default.Damage(NoSuchWeapon);

        prawnAsked.Should().Throw<ArgumentOutOfRangeException>();
        bladeAsked.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void A_refusal_this_build_cannot_say_throws_rather_than_going_out_blank()
    {
        Action word = () => StrikeRefusals.Word(NoSuchRefusal);
        Action sentence = () => StrikeRefusals.Sentence(NoSuchRefusal);

        word.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("refusal");
        sentence.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("refusal");
    }

    [TestMethod]
    public void A_word_this_build_has_never_heard_of_is_declined_rather_than_defaulted()
    {
        StrikeWeapons.TryParse("torpedo", out _).Should().BeFalse();
        StrikeWeapons.TryParse(null, out _).Should().BeFalse();
        StrikeWeapons.TryParse(string.Empty, out _).Should().BeFalse();

        StrikeRefusals.TryParse("because-i-said-so", out _).Should().BeFalse();
        StrikeRefusals.TryParse(null, out _).Should().BeFalse();
    }
}
