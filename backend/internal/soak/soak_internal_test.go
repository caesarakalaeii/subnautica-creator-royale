package soak

import (
	"strings"
	"testing"
	"time"
)

func TestASuspendIsTheGapBetweenTheTwoClocks(t *testing.T) {
	cases := []struct {
		name      string
		wall      time.Duration
		monotonic time.Duration
		want      time.Duration
	}{
		{
			// The host slept 11m8s during a run: the wall clock moved on while monotonic time barely did.
			name:      "eleven minutes asleep during a four minute run",
			wall:      3*time.Minute + 32*time.Second + 11*time.Minute + 8*time.Second,
			monotonic: 3*time.Minute + 32*time.Second,
			want:      11*time.Minute + 8*time.Second,
		},
		{
			name:      "a healthy run, where the two clocks agree",
			wall:      48 * time.Hour,
			monotonic: 48 * time.Hour,
			want:      0,
		},
		{
			// Scheduling jitter must not read as a suspend.
			name:      "jitter below the floor is not a suspend",
			wall:      time.Minute + 900*time.Millisecond,
			monotonic: time.Minute,
			want:      0,
		},
		{
			name:      "a clock that went backwards is not a negative sleep",
			wall:      time.Minute,
			monotonic: time.Minute + 3*time.Second,
			want:      0,
		},
	}

	for _, one := range cases {
		t.Run(one.name, func(t *testing.T) {
			if got := slept(one.wall, one.monotonic); got != one.want {
				t.Errorf("slept(%s, %s) = %s, want %s", one.wall, one.monotonic, got, one.want)
			}
		})
	}
}

func TestTheReportShoutsOnlyWhenTheHostSlept(t *testing.T) {
	quiet := Report{Elapsed: time.Minute}
	if got := quiet.String(); strings.Contains(got, "SLEPT") {
		t.Errorf("a clean report mentions sleeping: %s", got)
	}

	loud := Report{Elapsed: time.Minute, HostSlept: 11 * time.Minute}
	if got := loud.String(); !strings.Contains(got, "SLEPT=11m0s") {
		t.Errorf("a report of a slept run does not say so: %s", got)
	}
}
