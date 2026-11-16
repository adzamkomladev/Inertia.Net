import Layout from './Layout'

export default function ComputePage({ n, checksum, parallel }: { n: number; checksum: string; parallel: boolean }) {
  return (
    <Layout title="Compute" page="compute">
      <p>{n} records{parallel ? ', split across cores' : ''}</p>
      <p>Checksum <code data-checksum={checksum}>{checksum}</code></p>
    </Layout>
  )
}
