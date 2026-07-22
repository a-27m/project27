import { expect, test } from '@playwright/test'

// docs/web-parity-gaps-2026-07.md Task 1: predecessor lag is now editable from the
// task inspector. Exercises the real setLink wire path (a partial command that must
// leave the link type untouched) and confirms the edit survives a reload.

const SERVER_URL = 'http://localhost:5240'

async function apiRequest(path: string, init: RequestInit = {}) {
  const response = await fetch(`${SERVER_URL}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', 'X-Dev-User': 'alice', ...init.headers },
  })
  if (!response.ok) throw new Error(`${path} -> ${response.status}: ${await response.text()}`)
  return response.status === 204 ? null : ((await response.json()) as unknown)
}

test('edit a predecessor lag from the inspector', async ({ page }) => {
  const project = (await apiRequest('/api/projects', {
    method: 'POST',
    body: JSON.stringify({ name: `e2e-lag-${Date.now()}` }),
  })) as { id: string }
  await apiRequest(`/api/projects/${project.id}/checkout`, { method: 'POST' })
  const added = (await apiRequest(`/api/projects/${project.id}/commands`, {
    method: 'POST',
    body: JSON.stringify([
      { op: 'addTask', name: 'Task A', duration: '2d' },
      { op: 'addTask', name: 'Task B', duration: '2d' },
    ]),
  })) as { createdUids: (number | null)[] }
  const [predecessorUid, successorUid] = added.createdUids
  await apiRequest(`/api/projects/${project.id}/commands`, {
    method: 'POST',
    body: JSON.stringify([{ op: 'link', predecessorUid, successorUid, lag: { kind: 'working', value: 480 } }]),
  })

  await page.goto('/')
  await page.getByRole('textbox', { name: 'User' }).fill('alice')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByPlaceholder('New project name').waitFor()
  await page.goto(`/#/p/${project.id}`)

  const row = page.getByRole('row', { name: /Task B/ })
  await row.click()

  const inspector = page.getByRole('complementary', { name: /details/ })
  await inspector.getByRole('button', { name: 'Links' }).click()

  const typeSelect = inspector.getByLabel('Link type')
  const lagInput = inspector.getByLabel('Lag')
  await expect(lagInput).toHaveValue('1d')
  await expect(typeSelect).toHaveValue('finishToStart')

  await lagInput.fill('3d')
  await lagInput.press('Enter')
  await expect(lagInput).toHaveValue('3d')
  // Editing lag alone must not reset the link type (a partial setLink regression).
  await expect(typeSelect).toHaveValue('finishToStart')

  await page.reload()
  const rowAfterReload = page.getByRole('row', { name: /Task B/ })
  await rowAfterReload.click()
  await inspector.getByRole('button', { name: 'Links' }).click()
  await expect(inspector.getByLabel('Lag')).toHaveValue('3d')

  await apiRequest(`/api/projects/${project.id}`, { method: 'DELETE' })
})
