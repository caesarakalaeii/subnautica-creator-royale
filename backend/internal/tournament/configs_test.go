package tournament

import (
	"context"
	"testing"
	"time"
)

const someRules = "match:\n  cycleLength: 60s\n"

func TestARevisionIsKeptAndHandedToAServerThatIsBehind(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	written, err := store.WriteConfig(ctx, someRules, "ops", "a shorter cycle")
	if err != nil {
		t.Fatalf("write config: %v", err)
	}

	if written.Revision < 1 {
		t.Fatalf("a revision counts from one: %+v", written)
	}

	owed, has, err := store.ConfigOwed(ctx, 0)
	if err != nil {
		t.Fatalf("owed: %v", err)
	}

	if !has || owed.Revision != written.Revision || owed.Yaml != someRules {
		t.Fatalf("owed = %+v, want the revision just written", owed)
	}

	if owed.By != "ops" {
		t.Errorf("who wrote it travels to the game server's audit log: %+v", owed)
	}
}

func TestAServerAlreadyRunningTheNewestIsOwedNothing(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	written, _ := store.WriteConfig(ctx, someRules, "ops", "")

	if _, has, err := store.ConfigOwed(ctx, written.Revision); err != nil || has {
		t.Fatalf("owed something (%v) or errored: %v", has, err)
	}
}

func TestOnlyTheNewestRevisionIsHandedOver(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	store.WriteConfig(ctx, "match:\n  cycleLength: 10s\n", "ops", "") //nolint:errcheck // checked below
	store.WriteConfig(ctx, "match:\n  cycleLength: 20s\n", "ops", "") //nolint:errcheck // checked below
	last, err := store.WriteConfig(ctx, "match:\n  cycleLength: 30s\n", "ops", "")
	if err != nil {
		t.Fatalf("write config: %v", err)
	}

	owed, has, err := store.ConfigOwed(ctx, 0)
	if err != nil || !has {
		t.Fatalf("owed nothing: %v", err)
	}

	if owed.Revision != last.Revision {
		t.Fatalf("owed revision %d, want the newest %d: walking a server through the earlier ones "+
			"would move the wall twice for nothing", owed.Revision, last.Revision)
	}

	// Earlier revisions are marked superseded, so a refusal of the newest cannot fall back to them.
	list, _ := store.Configs(ctx, 10)
	for _, row := range list[1:] {
		if row.State != ConfigSuperseded {
			t.Errorf("revision %d = %q, want %q", row.Revision, row.State, ConfigSuperseded)
		}
	}
}

func TestARefusedRevisionDoesNotFallBackToAnAbandonedOne(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	store.WriteConfig(ctx, "match:\n  cycleLength: 10s\n", "ops", "one") //nolint:errcheck // superseded
	store.WriteConfig(ctx, "match:\n  cycleLength: 20s\n", "ops", "two") //nolint:errcheck // superseded
	last, _ := store.WriteConfig(ctx, "nonsense", "ops", "three")

	if err := store.ConfigSettled(ctx, last.Revision, false, true, "2 problems",
		[]string{"zone.opening: is required"}, time.Now()); err != nil {
		t.Fatalf("settle: %v", err)
	}

	owed, has, err := store.ConfigOwed(ctx, 0)
	if err != nil {
		t.Fatalf("owed: %v", err)
	}

	if has {
		t.Fatalf("owed %+v, want nothing: the newest revision is the intent, and it was refused",
			owed)
	}
}

func TestARevisionTheServerHasSettledIsNotOfferedAgain(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	written, _ := store.WriteConfig(ctx, someRules, "ops", "")
	at := time.Date(2026, 10, 24, 12, 0, 0, 0, time.UTC)

	if err := store.ConfigSettled(ctx, written.Revision, false, true,
		"that config has 2 problem(s)", []string{"zone.phases[1].radius: bad", "match.cycleLength: missing"},
		at); err != nil {
		t.Fatalf("settle: %v", err)
	}

	if _, has, _ := store.ConfigOwed(ctx, 0); has {
		t.Fatalf("a refused revision must not be resent")
	}
}

func TestWaitingForTheLobbyIsNotRefused(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	written, _ := store.WriteConfig(ctx, someRules, "ops", "")
	at := time.Date(2026, 10, 24, 12, 0, 0, 0, time.UTC)

	if err := store.ConfigSettled(ctx, written.Revision, false, false,
		"written to this server's own config file. A match is running.", nil, at); err != nil {
		t.Fatalf("settle: %v", err)
	}

	newest, has, err := store.NewestConfig(ctx)
	if err != nil || !has {
		t.Fatalf("newest: %v", err)
	}

	if newest.State != ConfigWaiting {
		t.Fatalf("state = %q, want %q: an operator must not read this as refused",
			newest.State, ConfigWaiting)
	}

	// Still owed, so a game server that restarts and reports zero gets it back.
	if _, owedIt, _ := store.ConfigOwed(ctx, 0); !owedIt {
		t.Errorf("a revision nobody has finished with is still the newest one to offer")
	}
}

func TestTheLoadersOwnProblemsSurviveTheRoundTrip(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	written, _ := store.WriteConfig(ctx, "nonsense", "ops", "")
	problems := []string{
		"zone.phases[1].radius: must be a number of metres",
		"match.cycleLength: is required",
	}

	if err := store.ConfigSettled(ctx, written.Revision, false, true,
		"that config has 2 problem(s) and was not written or adopted.", problems,
		time.Now()); err != nil {
		t.Fatalf("settle: %v", err)
	}

	newest, _, err := store.NewestConfig(ctx)
	if err != nil {
		t.Fatalf("newest: %v", err)
	}

	if len(newest.Problems) != 2 || newest.Problems[0] != problems[0] {
		t.Fatalf("problems = %v, want the loader's own field paths word for word", newest.Problems)
	}
}

func TestAnAdoptedRevisionStaysAdopted(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	written, _ := store.WriteConfig(ctx, someRules, "ops", "")
	at := time.Date(2026, 10, 24, 12, 0, 0, 0, time.UTC)

	if err := store.ConfigSettled(ctx, written.Revision, true, true, "adopted", nil, at); err != nil {
		t.Fatalf("settle: %v", err)
	}

	if err := store.ConfigSettled(ctx, written.Revision, false, true, "a match is running", nil,
		at.Add(time.Minute)); err != nil {
		t.Fatalf("settle again: %v", err)
	}

	newest, _, _ := store.NewestConfig(ctx)
	if newest.State != ConfigAdopted {
		t.Fatalf("state = %q, want %q", newest.State, ConfigAdopted)
	}
}

func TestTheNewestIsShownEvenWhenItWasRefused(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	store.WriteConfig(ctx, "match:\n  cycleLength: 60s\n", "ops", "") //nolint:errcheck // checked below
	bad, _ := store.WriteConfig(ctx, "nonsense", "ops", "")

	if err := store.ConfigSettled(ctx, bad.Revision, false, true, "no", []string{"x: y"},
		time.Now()); err != nil {
		t.Fatalf("settle: %v", err)
	}

	newest, has, _ := store.NewestConfig(ctx)
	if !has || newest.Revision != bad.Revision {
		t.Fatalf("newest = %+v, want the one being worked on", newest)
	}
}

func TestAPreviousRevisionCanBeReadBackWhole(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	first, _ := store.WriteConfig(ctx, "match:\n  cycleLength: 60s\n", "ops", "the good one")
	store.WriteConfig(ctx, "match:\n  cycleLength: 5s\n", "ops", "a mistake") //nolint:errcheck // not read

	back, has, err := store.ConfigAt(ctx, first.Revision)
	if err != nil || !has {
		t.Fatalf("read back: %v", err)
	}

	if back.Yaml != "match:\n  cycleLength: 60s\n" || back.Note != "the good one" {
		t.Fatalf("back = %+v", back)
	}
}

func TestTheConsolesListLeavesTheDocumentsOut(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	store.WriteConfig(ctx, someRules, "ops", "one") //nolint:errcheck // read below
	store.WriteConfig(ctx, someRules, "ops", "two") //nolint:errcheck // read below

	list, err := store.Configs(ctx, 10)
	if err != nil {
		t.Fatalf("configs: %v", err)
	}

	if len(list) != 2 || list[0].Note != "two" {
		t.Fatalf("list = %+v, want newest first", list)
	}

	if list[0].Yaml != "" {
		t.Errorf("a list of revisions does not carry twenty kilobytes each: %q", list[0].Yaml)
	}
}

func TestSendingIsStampedOnceLikeACommands(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	written, _ := store.WriteConfig(ctx, someRules, "ops", "")
	first := time.Date(2026, 10, 24, 12, 0, 0, 0, time.UTC)

	if err := store.ConfigSentTo(ctx, written.Revision, first); err != nil {
		t.Fatalf("sent: %v", err)
	}

	if err := store.ConfigSentTo(ctx, written.Revision, first.Add(time.Minute)); err != nil {
		t.Fatalf("sent again: %v", err)
	}

	newest, _, _ := store.NewestConfig(ctx)
	if newest.State != ConfigSent || newest.SentAt == nil || !newest.SentAt.Equal(first) {
		t.Fatalf("newest = %+v, want sent_at at the first offer", newest)
	}
}
