<script lang="ts">
  type Row = { name: string; extra?: number }

  let { rows }: { rows: Row[] } = $props()
</script>

<ul>
  {#each rows as row, index (index)}
    <li data-testid="array-row">{row.name}: {row.extra ?? 'loading'}</li>
  {/each}
</ul>
