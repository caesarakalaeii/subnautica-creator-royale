package caster

import (
	"sync"
	"testing"
)

// noop is a subscriber that is never expected to be dropped.
func noop() {}

func TestAHubAtCapacityRefusesTheNextClient(t *testing.T) {
	t.Parallel()

	hub := NewHub(2)

	for slot := 1; slot <= 2; slot++ {
		if _, _, ok := hub.Subscribe(noop); !ok {
			t.Fatalf("client %d was refused, want 2 accepted", slot)
		}
	}

	if _, _, ok := hub.Subscribe(noop); ok {
		t.Error("a third client was accepted against a cap of 2")
	}

	if hub.Count() != 2 {
		t.Errorf("Count() = %d, want 2 — a refused client must not be counted or held", hub.Count())
	}
}

func TestARefusedClientDoesNotDisturbTheOnesConnected(t *testing.T) {
	t.Parallel()

	hub := NewHub(1)

	messages, _, ok := hub.Subscribe(func() { t.Error("the connected client was dropped") })
	if !ok {
		t.Fatal("the first client was refused")
	}

	if _, _, ok := hub.Subscribe(noop); ok {
		t.Fatal("the second client was accepted against a cap of 1")
	}

	hub.Broadcast([]byte(`{"v":1}`))

	select {
	case payload := <-messages:
		if string(payload) != `{"v":1}` {
			t.Errorf("got %q", payload)
		}
	default:
		t.Error("the connected client got nothing after a refusal")
	}
}

func TestLeavingFreesASlot(t *testing.T) {
	t.Parallel()

	hub := NewHub(1)

	_, unsubscribe, ok := hub.Subscribe(noop)
	if !ok {
		t.Fatal("the first client was refused")
	}

	unsubscribe()

	if _, _, ok := hub.Subscribe(noop); !ok {
		t.Error("a client was refused after the only slot was freed")
	}
}

func TestACapOfZeroIsNoCapAtAll(t *testing.T) {
	t.Parallel()

	hub := NewHub(0)

	for slot := 0; slot < 500; slot++ {
		if _, _, ok := hub.Subscribe(noop); !ok {
			t.Fatalf("client %d was refused by an uncapped hub", slot)
		}
	}
}

func TestOneSlowClientIsDroppedAndTheRestKeepReceiving(t *testing.T) {
	t.Parallel()

	hub := NewHub(0)

	const readers = 8
	channels := make([]<-chan []byte, 0, readers)
	for reader := 0; reader < readers; reader++ {
		messages, _, ok := hub.Subscribe(func() { t.Error("a client that was keeping up got dropped") })
		if !ok {
			t.Fatal("a reader was refused by an uncapped hub")
		}
		channels = append(channels, messages)
	}

	var dropped sync.WaitGroup
	dropped.Add(1)

	// The wedged client: subscribed, never read.
	_, _, ok := hub.Subscribe(func() { dropped.Done() })
	if !ok {
		t.Fatal("the slow client was refused by an uncapped hub")
	}

	// One more than the buffer holds; the readers drain as it goes, so only the wedged one falls behind.
	for push := 0; push <= SubscriberBuffer; push++ {
		hub.Broadcast([]byte(`{"v":1}`))

		for _, messages := range channels {
			select {
			case <-messages:
			default:
				t.Fatalf("a reader missed push %d", push)
			}
		}
	}

	dropped.Wait()

	if hub.SlowDrops() != 1 {
		t.Errorf("SlowDrops() = %d, want 1", hub.SlowDrops())
	}
}

func TestAWedgedClientIsDroppedOnceRatherThanOncePerPush(t *testing.T) {
	t.Parallel()

	hub := NewHub(0)

	drops := 0
	if _, _, ok := hub.Subscribe(func() { drops++ }); !ok {
		t.Fatal("refused by an uncapped hub")
	}

	for push := 0; push < SubscriberBuffer*3; push++ {
		hub.Broadcast([]byte(`{"v":1}`))
	}

	if drops != 1 {
		t.Errorf("dropSlow was called %d times for one wedged client, want 1", drops)
	}
	if hub.SlowDrops() != 1 {
		t.Errorf("SlowDrops() = %d, want 1", hub.SlowDrops())
	}
}
