package tournament

import (
	"context"
	"testing"
	"time"
)

func TestACommandIsQueuedAndOwedToTheMod(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	first, err := store.Queue(ctx, "status", "ops")
	if err != nil {
		t.Fatalf("queue: %v", err)
	}

	second, err := store.Queue(ctx, "ghost Caster", "ops")
	if err != nil {
		t.Fatalf("queue: %v", err)
	}

	if second.ID <= first.ID {
		t.Fatalf("ids have to climb, or the mod's watermark cannot dedupe: %d then %d",
			first.ID, second.ID)
	}

	owed, err := store.Owed(ctx, 0)
	if err != nil {
		t.Fatalf("owed: %v", err)
	}

	if len(owed) != 2 || owed[0].ID != first.ID || owed[1].ID != second.ID {
		t.Fatalf("oldest first, both of them; got %+v", owed)
	}

	if owed[1].Verb != "ghost Caster" || owed[1].By != "ops" {
		t.Fatalf("the verb and who asked have to survive the round trip: %+v", owed[1])
	}
}

func TestTheWatermarkIsWhatStopsACommandRunningTwice(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	first, _ := store.Queue(ctx, "start", "ops")
	second, _ := store.Queue(ctx, "skip", "ops")

	owed, err := store.Owed(ctx, first.ID)
	if err != nil {
		t.Fatalf("owed: %v", err)
	}

	if len(owed) != 1 || owed[0].ID != second.ID {
		t.Fatalf("a mod that has run %d is owed only what came after it; got %+v", first.ID, owed)
	}
}

func TestSendingIsStampedOnceAndAResendIsTheSameAsking(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	row, _ := store.Queue(ctx, "pause", "ops")

	first := time.Date(2026, 10, 24, 12, 0, 0, 0, time.UTC)
	if err := store.MarkSent(ctx, []int64{row.ID}, first); err != nil {
		t.Fatalf("mark sent: %v", err)
	}

	// The game server restarted, so its watermark is zero and the same row goes out again.
	if err := store.MarkSent(ctx, []int64{row.ID}, first.Add(time.Minute)); err != nil {
		t.Fatalf("mark sent again: %v", err)
	}

	rows, err := store.Commands(ctx, 10)
	if err != nil {
		t.Fatalf("commands: %v", err)
	}

	if len(rows) != 1 || rows[0].SentAt == nil {
		t.Fatalf("one row, sent; got %+v", rows)
	}

	if !rows[0].SentAt.Equal(first) {
		t.Fatalf("sent_at answers when the booth first offered it, not when it last did: %v",
			rows[0].SentAt)
	}

	if rows[0].State != StateSent {
		t.Fatalf("state should be %q, not %q", StateSent, rows[0].State)
	}
}

func TestAnUnansweredCommandExpiresAndIsNotOwedAfterThat(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	row, _ := store.Queue(ctx, "start", "ops")

	gone, err := store.Expire(ctx, time.Now().Add(time.Hour))
	if err != nil {
		t.Fatalf("expire: %v", err)
	}

	if gone != 1 {
		t.Fatalf("one command should have expired, not %d", gone)
	}

	owed, err := store.Owed(ctx, 0)
	if err != nil {
		t.Fatalf("owed: %v", err)
	}

	if len(owed) != 0 {
		t.Fatalf("an expired command must never fire when the game server comes back: %+v", owed)
	}

	rows, _ := store.Commands(ctx, 10)
	if len(rows) != 1 || rows[0].ID != row.ID || rows[0].State != StateExpired {
		t.Fatalf("the row stays as evidence, marked expired; got %+v", rows)
	}
}

func TestAFreshCommandSurvivesAnExpirySweep(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	store.Queue(ctx, "status", "ops") //nolint:errcheck // checked by the read below

	if _, err := store.Expire(ctx, time.Now().Add(-time.Hour)); err != nil {
		t.Fatalf("expire: %v", err)
	}

	owed, _ := store.Owed(ctx, 0)
	if len(owed) != 1 {
		t.Fatalf("a command queued a moment ago is not two minutes old: %+v", owed)
	}
}

func TestTheModsAnswerIsKeptVerbatimAndOnlyTheFirstOneCounts(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	row, _ := store.Queue(ctx, "round", "ops")
	at := time.Date(2026, 10, 24, 12, 0, 5, 0, time.UTC)

	answer := "a group is 5 and this counted 4: Ada, Bea, Cy, Dov. Run `lms round force` to prepare it anyway."
	if err := store.MarkRan(ctx, row.ID, answer, at); err != nil {
		t.Fatalf("mark ran: %v", err)
	}

	// A duplicate report, as a resent console push produces.
	if err := store.MarkRan(ctx, row.ID, "something else", at.Add(time.Minute)); err != nil {
		t.Fatalf("mark ran twice: %v", err)
	}

	rows, _ := store.Commands(ctx, 10)
	if len(rows) != 1 || rows[0].State != StateRan {
		t.Fatalf("one row, run; got %+v", rows)
	}

	if rows[0].Answer != answer {
		t.Fatalf("the sentence the mod wrote is the one the console shows:\n  want %q\n   got %q",
			answer, rows[0].Answer)
	}

	if rows[0].RanAt == nil || !rows[0].RanAt.Equal(at) {
		t.Fatalf("ran_at is when it ran, not when the second report arrived: %v", rows[0].RanAt)
	}
}

func TestAnAnswerThatArrivesAfterTheExpiryStillCounts(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	row, _ := store.Queue(ctx, "skip", "ops")

	if _, err := store.Expire(ctx, time.Now().Add(time.Hour)); err != nil {
		t.Fatalf("expire: %v", err)
	}

	at := time.Date(2026, 10, 24, 12, 0, 0, 0, time.UTC)
	if err := store.MarkRan(ctx, row.ID, "phase 2 of 6", at); err != nil {
		t.Fatalf("mark ran: %v", err)
	}

	rows, _ := store.Commands(ctx, 10)
	if len(rows) != 1 || rows[0].State != StateRan {
		t.Fatalf("an expiry that fired while the command was in flight is not the last word: %+v", rows)
	}
}

func TestOneReplyIsBoundedSoARestartedServerIsNotHandedTheWholeBacklog(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	for i := 0; i < MaxCommandsPerReply+3; i++ {
		if _, err := store.Queue(ctx, "status", "ops"); err != nil {
			t.Fatalf("queue: %v", err)
		}
	}

	owed, err := store.Owed(ctx, 0)
	if err != nil {
		t.Fatalf("owed: %v", err)
	}

	if len(owed) != MaxCommandsPerReply {
		t.Fatalf("one reply carries %d, not %d", MaxCommandsPerReply, len(owed))
	}
}

func TestTheCommandsAPageDrawsAreTheNewestFirst(t *testing.T) {
	store := scratch(t)
	ctx := context.Background()

	first, _ := store.Queue(ctx, "start", "ops")
	last, _ := store.Queue(ctx, "pause", "ops")

	rows, err := store.Commands(ctx, 10)
	if err != nil {
		t.Fatalf("commands: %v", err)
	}

	if len(rows) != 2 || rows[0].ID != last.ID || rows[1].ID != first.ID {
		t.Fatalf("newest first, because that is the end of the list somebody is watching: %+v", rows)
	}
}
