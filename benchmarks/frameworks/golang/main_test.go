package main

import (
	"net/http"
	"testing"
)

func TestComputeChecksum(t *testing.T) {
	// The checksum the shared TypeScript and C# implementations produce for /compute?n=20000.
	const want = "ee0d57b583a7c9c83be2def56effb6778ffd61d3e594e09ae2066c867f47f00b"
	if got := computeChecksum(20000, false); got != want {
		t.Fatalf("sequential checksum = %s, want %s", got, want)
	}
	if got := computeChecksum(20000, true); got != want {
		t.Fatalf("parallel checksum = %s, want %s", got, want)
	}
}

func TestValidateAcceptsTheForm(t *testing.T) {
	title, priority, assignee, due := "Buy milk", 2, "ada", "2026-06-01"
	input := newTodoInput{Title: &title, Priority: &priority, Assignee: &assignee, Due: &due}

	errors, date, trimmed := input.validate()
	if len(errors) != 0 {
		t.Fatalf("errors = %v, want none", errors)
	}
	if date.Format("2006-01-02") != due {
		t.Fatalf("due = %s, want %s", date.Format("2006-01-02"), due)
	}
	if trimmed != title {
		t.Fatalf("title = %q, want %q", trimmed, title)
	}
}

func TestValidateRejectsEveryField(t *testing.T) {
	title, priority, assignee, due := "x", 9, "bob", "2027-02-30"
	input := newTodoInput{Title: &title, Priority: &priority, Assignee: &assignee, Due: &due}

	errors, _, _ := input.validate()
	if len(errors) != 4 {
		t.Fatalf("errors = %v, want four", errors)
	}
	for _, field := range []string{"title", "priority", "assignee", "due"} {
		if errors[field] == nil {
			t.Errorf("missing error for %s", field)
		}
	}
}

func TestIntParam(t *testing.T) {
	request := func(query string) string { return "http://example.test/?" + query }
	tests := []struct {
		query string
		want  int
	}{
		{"", 25},
		{"perPage=100", 100},
		{"perPage=1000", 100},
		{"perPage=0", 1},
		{"perPage=nope", 25},
	}
	for _, test := range tests {
		t.Run(test.query, func(t *testing.T) {
			httpRequest, _ := http.NewRequest(http.MethodGet, request(test.query), nil)
			if got := intParam(httpRequest, "perPage", 25, 1, 100); got != test.want {
				t.Fatalf("intParam(%q) = %d, want %d", test.query, got, test.want)
			}
		})
	}
}
