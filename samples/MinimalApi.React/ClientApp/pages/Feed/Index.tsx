import { InfiniteScroll } from '@inertiajs/react'
import Layout from '../../Layout'

export default function Index({ posts }: { posts: { data: { id: number; title: string }[] } }) {
  return (
    <Layout>
      <h1>Feed</h1>
      <InfiniteScroll data="posts">
        {posts.data.map((post) => (
          <article key={post.id} data-testid="post" style={{ height: 160, borderBottom: '1px solid #ccc' }}>{post.title}</article>
        ))}
      </InfiniteScroll>
    </Layout>
  )
}
