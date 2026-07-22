import { expect, test } from '@playwright/test'

// First Playwright e2e test in this repo (docs/web-parity-gaps-2026-07.md, Task 6):
// exercises the "Split" inspector section end to end against a real browser and a
// real server, which Vitest (jsdom-free, lib-only per E26) can't do.

const SERVER_URL = 'http://localhost:5240'

async function apiRequest(path: string, init: RequestInit = {}) {
  const response = await fetch(`${SERVER_URL}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', 'X-Dev-User': 'alice', ...init.headers },
  })
  if (!response.ok) throw new Error(`${path} -> ${response.status}: ${await response.text()}`)
  return response.status === 204 ? null : ((await response.json()) as unknown)
}

test('split and unsplit a task from the inspector', async ({ page }) => {
  const project = (await apiRequest('/api/projects', {
    method: 'POST',
    body: JSON.stringify({ name: `e2e-split-${Date.now()}` }),
  })) as { id: string }
  await apiRequest(`/api/projects/${project.id}/checkout`, { method: 'POST' })
  await apiRequest(`/api/projects/${project.id}/commands`, {
    method: 'POST',
    body: JSON.stringify([{ op: 'addTask', name: 'Build the thing', duration: '10d' }]),
  })

  await page.goto('/')
  await page.getByRole('textbox', { name: 'User' }).fill('alice')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByPlaceholder('New project name').waitFor()
  await page.goto(`/#/p/${project.id}`)

  const row = page.getByRole('row', { name: /Build the thing/ })
  await row.click()

  const inspector = page.getByRole('complementary', { name: /details/ })
  await inspector.getByRole('button', { name: 'Split' }).click()

  await inspector.getByLabel('Split offset from task start').fill('2d')
  await inspector.getByLabel('Split gap length').fill('1d')
  await inspector.getByRole('button', { name: 'Split task' }).click()

  await expect(inspector.getByText('Segment 1')).toBeVisible()
  await expect(inspector.getByText('Segment 2')).toBeVisible()

  await inspector.getByRole('button', { name: 'Remove all splits' }).click()
  await expect(inspector.getByText('Segment 1')).toHaveCount(0)

  await apiRequest(`/api/projects/${project.id}`, { method: 'DELETE' })
})
