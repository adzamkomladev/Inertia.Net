import { expect, test } from '@playwright/test'

test('signs in, sees the dashboard, signs out and is sent back to the login page', async ({ page }) => {
  await page.goto('/dashboard')
  await expect(page).toHaveURL(/\/login\?ReturnUrl=%2Fdashboard$/)

  // Precognition checks a field when it is left.
  await page.getByLabel('Email').fill('not-an-email')
  await page.getByLabel('Password').focus()
  await expect(page.getByRole('alert')).toContainText('Email')

  await page.getByLabel('Email').fill('demo@example.com')
  await page.getByLabel('Password').fill('wrong')
  await page.getByRole('button', { name: 'Log in' }).click()
  await expect(page.getByRole('alert')).toHaveText('These credentials do not match our records.')

  await page.getByLabel('Password').fill('password')
  await page.getByRole('button', { name: 'Log in' }).click()
  await expect(page).toHaveURL(/\/dashboard$/)
  await expect(page.getByText('You are signed in as Demo User.')).toBeVisible()

  await page.getByRole('button', { name: 'Log out' }).click()
  await expect(page.getByRole('link', { name: 'Log in' })).toBeVisible()
  await page.goto('/dashboard')
  await expect(page).toHaveURL(/\/login\?ReturnUrl=%2Fdashboard$/)
})

test('returns to the page that asked for the sign-in, only on this site', async ({ page }) => {
  const signIn = async () => {
    await page.getByLabel('Email').fill('demo@example.com')
    await page.getByLabel('Password').fill('password')
    await page.getByRole('button', { name: 'Log in' }).click()
  }

  await page.goto('/login?ReturnUrl=%2Ftodos')
  await signIn()
  await expect(page).toHaveURL(/\/todos$/)
  await page.getByRole('button', { name: 'Log out' }).click()
  await expect(page.getByRole('link', { name: 'Log in' })).toBeVisible()

  await page.goto('/login?ReturnUrl=%2F%2Fevil.example')
  await signIn()
  await expect(page).toHaveURL(/\/dashboard$/)
})
