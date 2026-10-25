package tournament

import (
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"testing"
)

// boothTables is every table a booth owns; every statement touching one must filter by booth_id.
var boothTables = []string{
	"attributions", "adjustments", "groups", "group_members", "final_series", "people", "overrides", "commands", "configs",
}

// Reads the package's own source, since a database is not always available to tests. When this
// fails, fix the query: `schema_migrations` is the only unscoped table and is not in the list.
func TestEveryStatementAgainstABoothsTablesIsScopedToABooth(t *testing.T) {
	t.Parallel()

	for _, name := range sourceFiles(t) {
		body, err := os.ReadFile(filepath.Clean(name))
		if err != nil {
			t.Fatalf("read %s: %v", name, err)
		}

		for _, statement := range statementsIn(string(body)) {
			if !touchesABoothTable(statement) {
				continue
			}

			if !strings.Contains(statement, "booth_id") {
				t.Errorf("%s: this statement names a booth's table and does not scope to a booth:\n%s",
					name, statement)
			}
		}
	}
}

// sourceFiles is the package's non-test Go files; tests may write unscoped rows on purpose.
func sourceFiles(t *testing.T) []string {
	t.Helper()

	found, err := filepath.Glob("*.go")
	if err != nil {
		t.Fatalf("list the package: %v", err)
	}

	kept := make([]string, 0, len(found))
	for _, name := range found {
		if !strings.HasSuffix(name, "_test.go") {
			kept = append(kept, name)
		}
	}

	if len(kept) == 0 {
		t.Fatal("no source files found, so this test proves nothing")
	}

	return kept
}

// statementsIn is every raw string literal in a file. All SQL here is written as raw strings, so
// a statement and its predicates share one literal.
func statementsIn(body string) []string {
	var held []string

	for {
		open := strings.Index(body, "`")
		if open < 0 {
			return held
		}

		body = body[open+1:]

		close := strings.Index(body, "`")
		if close < 0 {
			return held
		}

		held = append(held, body[:close])
		body = body[close+1:]
	}
}

func touchesABoothTable(statement string) bool {
	for _, table := range boothTables {
		// The word boundary matters: `groups` must not match `group_members` or `group_number`, and
		// `people` must not match `people_name_idx`.
		if regexp.MustCompile(`\b(FROM|JOIN|INTO|UPDATE|TABLE)\s+` + table + `\b`).MatchString(statement) {
			return true
		}
	}

	return false
}
