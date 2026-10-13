type Row = { name: string; extra?: number }

export default function ArrayRegression({ rows }: { rows: Row[] }) {
  return <ul>{rows.map((row, index) => <li key={index} data-testid="array-row">{row.name}: {row.extra ?? 'loading'}</li>)}</ul>
}
