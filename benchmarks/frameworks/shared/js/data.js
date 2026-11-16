// Static data and request parsing shared by the JavaScript apps (the C# and PHP apps have the same values).
export const features = [
  { title: 'Lists', body: 'Group todos by project, tag and due date.' },
  { title: 'Reports', body: 'See what got done this week and what slipped.' },
  { title: 'Teams', body: 'Share lists and assign todos to people.' },
]

const int = (value, fallback, min, max) => {
  const n = Number.parseInt(value ?? '', 10)
  return Number.isNaN(n) ? fallback : Math.min(Math.max(n, min), max)
}

export const todosQuery = (params) => ({ perPage: int(params.get('perPage'), 25, 1, 100), page: int(params.get('page'), 1, 1, 400) })

export const computeN = (params) => int(params.get('n'), 20000, 16, 1000000)
