import { expect, test } from '@playwright/test'

// InertiaUI Modal has React and Vue clients only: the Svelte sample has no modal.
test.skip((process.env.SAMPLE ?? 'react') === 'svelte', 'InertiaUI Modal has no Svelte client')

test.beforeEach(async ({ request }) => {
  await request.post('/__e2e/reset')
})

test('opens over the list, keeps validation errors in the modal, saves and closes', async ({ page }) => {
  await page.goto('/users')
  await page.getByTestId('edit-user-1').click()
  const modal = page.getByTestId('user-modal')
  await expect(modal).toBeVisible()
  await expect(page.getByTestId('user-row').first()).toBeVisible() // the list stays under the modal

  await page.getByTestId('user-name').fill('')
  await page.getByTestId('user-save').click()
  await expect(page.getByTestId('error-user-name')).toContainText(/required/i)
  await expect(modal).toBeVisible()

  await page.getByTestId('user-name').fill('Ada King')
  await page.getByTestId('user-save').click()
  await expect(modal).toBeHidden()
  await expect(page.getByTestId('flash')).toHaveText('User updated')
  await expect(page.getByTestId('user-row').first()).toContainText('Ada King')
  await expect(page).toHaveURL(/\/users$/)
})

test('a direct visit renders the list with the modal open, and closing it returns to the list', async ({ page }) => {
  await page.goto('/users/2/edit')
  await expect(page.getByTestId('user-modal')).toBeVisible()
  await expect(page.getByTestId('user-name')).toHaveValue('Grace Hopper')
  await expect(page.getByTestId('user-row')).toHaveCount(3)
  await expect(page).toHaveURL(/\/users\/2\/edit$/)
  // The modal client skips its visit back to the list while another visit runs: let the list's deferred props load first.
  await expect(page.getByTestId('stats')).toHaveText('Total: 3')

  await page.getByTestId('user-cancel').click()
  await expect(page.getByTestId('user-modal')).toBeHidden()
  await expect(page).toHaveURL(/\/users$/)
})

test('the back button leaves a modal visited directly', async ({ page }) => {
  await page.goto('/users')
  await page.goto('/users/3/edit')
  await expect(page.getByTestId('user-modal')).toBeVisible()

  await page.goBack()
  await expect(page.getByTestId('user-modal')).toBeHidden()
  await expect(page).toHaveURL(/\/users$/)
  await expect(page.getByTestId('user-row')).toHaveCount(3)
})
