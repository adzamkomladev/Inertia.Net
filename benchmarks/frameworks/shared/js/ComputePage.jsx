import Layout from './Layout.jsx'

export default function ComputePage({ n, checksum, parallel }) {
  return (
    <Layout title="Compute" page="compute">
      <p>{n} records{parallel ? ', split across cores' : ''}</p>
      <p>Checksum <code data-checksum={checksum}>{checksum}</code></p>
    </Layout>
  )
}
