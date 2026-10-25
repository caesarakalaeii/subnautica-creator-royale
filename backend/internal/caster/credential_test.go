package caster

import "testing"

func TestACredentialRecognisesItsOwnTokenAndNothingElse(t *testing.T) {
	t.Parallel()

	held := hashToken("the-push-secret")

	if !held.matches("the-push-secret") {
		t.Error("a credential does not recognise the token it was made from")
	}

	// A prefix catches a comparison that stops at the shorter length.
	for _, wrong := range []string{"the-push-secre", "the-push-secret ", "", "THE-PUSH-SECRET"} {
		if held.matches(wrong) {
			t.Errorf("a credential took %q", wrong)
		}
	}
}

func TestAnUnsetCredentialMatchesNothingAtAll(t *testing.T) {
	t.Parallel()

	unset := hashToken("")

	if unset.set {
		t.Error("an empty token made a credential that is set")
	}

	for _, presented := range []string{"", " ", "anything"} {
		if unset.matches(presented) {
			t.Errorf("an unset credential took %q", presented)
		}
	}
}

func TestACredentialSurvivesTheRoundTripThroughTheDatabase(t *testing.T) {
	t.Parallel()

	written := hashToken("the-push-secret").stored()

	if written == "the-push-secret" {
		t.Fatal("what is written down is the token itself")
	}

	read := storedCredential(written)

	if !read.matches("the-push-secret") {
		t.Error("a credential read back from the database does not recognise its own token")
	}

	if read.matches("something-else") {
		t.Error("a credential read back from the database takes any token at all")
	}
}

func TestADigestThatCannotBeReadRefusesEveryToken(t *testing.T) {
	t.Parallel()

	for what, stored := range map[string]string{
		"not hex":     "zzzz",
		"too short":   "abcd",
		"empty":       "",
		"nearly, but": "00112233445566778899aabbccddeeff00112233445566778899aabbccddee",
	} {
		held := storedCredential(stored)

		if held.set {
			t.Errorf("%s made a credential that is set", what)
		}

		if held.matches("anything") || held.matches("") {
			t.Errorf("%s made a credential that takes a token", what)
		}
	}
}
