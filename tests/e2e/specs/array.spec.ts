import { expect, test } from '@playwright/test'

test('indexed deferred reload preserves both array elements and sibling fields', async ({ page }) => {
  test.skip((process.env.SAMPLE ?? 'react') !== 'react', 'React hosts the array regression fixture')
  const secondGroup = page.waitForResponse(response =>
    response.request().headers()['x-inertia-partial-data']?.includes('rows.1.extra') === true)
  await page.goto('/__e2e/array')
  const response = await secondGroup
  expect((await response.json()).props.rows).toEqual([{ name: 'first', extra: 10 }, { name: 'second', extra: 20 }])
  await expect(page.getByTestId('array-row')).toHaveText(['first: 10', 'second: 20'])
})
