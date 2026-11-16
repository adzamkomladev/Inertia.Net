// The validation rules of the new-todo form. Every back end checks the same rules and answers with these exact messages
// (C#, PHP and Go repeat them; run.sh's parity check compares them byte for byte).
export const ASSIGNEES = ['ada', 'barbara', 'dennis', 'grace', 'linus'] as const

export const MESSAGES = {
  title: 'Enter a title between 3 and 120 characters.',
  priority: 'Choose a priority from 0 to 4.',
  assignee: 'Choose someone from the team.',
  due: 'Enter a due date in 2026.',
} as const

// title: 3-120 characters after trimming. priority: an integer 0-4. assignee: one of ASSIGNEES.
// due: a real calendar date written YYYY-MM-DD, from 2026-01-01 to 2026-12-31.
