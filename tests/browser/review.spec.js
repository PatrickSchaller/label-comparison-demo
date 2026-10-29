import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';

test('sample has explicit provenance, cautions, review persistence and export', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Extract & compare with AI' })).toBeDisabled();
  await page.getByRole('button', { name: 'Run sample comparison' }).click();
  await expect(page.locator('#mode')).toHaveText('Sample fixtures · no AI call');
  await expect(page.locator('#summary')).toHaveText('9 findings, including 1 unclear. 5 fields unchanged.');
  await expect(page.locator('.finding')).toHaveCount(9);
  await expect(page.locator('.finding[data-status="review"]')).toContainText('Second allergen is blurred');
  await expect(page.getByAltText('before label')).toBeVisible();
  await expect(page.getByAltText('after label')).toBeVisible();
  const preview = await page.locator('#before-preview').boundingBox();
  const image = await page.getByAltText('before label').boundingBox();
  expect(image.y + image.height).toBeLessThanOrEqual(preview.y + preview.height);
  await page.locator('.acknowledge input').first().check();
  await page.getByLabel('Show unchanged fields').check();
  await expect(page.locator('.finding')).toHaveCount(14);
  await expect(page.locator('.acknowledge input').first()).toBeChecked();
  let downloadPromise = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export review JSON' }).click();
  let download = await downloadPromise;
  let artifact = JSON.parse(await readFile(await download.path(), 'utf8'));
  expect(artifact.review.state).toBe('pending');
  expect(artifact.review.acknowledgedFields).toHaveLength(1);
  expect(artifact.before.sha256).toMatch(/^[0-9a-f]{64}$/);
  expect(artifact.before.responseId).toBe('fixture');
  for (const input of await page.locator('.acknowledge input').all()) await input.check();
  await page.getByLabel('I checked both original labels').check();
  await expect(page.locator('#review-progress')).toContainText('Review acknowledgments complete');
  downloadPromise = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export review JSON' }).click();
  download = await downloadPromise;
  artifact = JSON.parse(await readFile(await download.path(), 'utf8'));
  expect(artifact.review.state).toBe('acknowledged');
  expect(artifact.review.acknowledgedFields).toHaveLength(9);
  expect(artifact.rows.find(row => row.key === 'allergens').status).toBe('review');
  await page.getByRole('button', { name: 'Run sample comparison' }).click();
  await expect(page.locator('#review-progress')).toContainText('0 of 9 findings checked');
  await expect(page.getByLabel('I checked both original labels')).not.toBeChecked();
  expect(errors).toEqual([]);
});

test('mobile review fits the screen and untrusted extraction is displayed as text', async ({ page, request }) => {
  const report = await (await request.get('/api/sample')).json();
  report.rows[0].after.value = '<img src=x onerror="window.injected=true">';
  report.rows[0].status = 'changed';
  await page.route('**/api/sample', route => route.fulfill({ json: report }));
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');
  await page.getByRole('button', { name: 'Run sample comparison' }).click();
  await expect(page.locator('.value').first()).toBeVisible();
  await expect(page.locator('#rows')).toContainText('<img src=x onerror="window.injected=true">');
  expect(await page.evaluate(() => window.injected)).toBeUndefined();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('live failure clears the previous report and restores the submit button', async ({ page }) => {
  await page.route('**/api/status', route => route.fulfill({ json: { liveEnabled: true, model: 'test-only' } }));
  await page.route('**/api/compare', route => route.fulfill({ status: 502, json: { detail: 'AI provider unavailable in this test.' } }));
  await page.goto('/');
  await page.getByRole('button', { name: 'Run sample comparison' }).click();
  const fixture = 'src/LabelReview/wwwroot/samples/before.png';
  await page.locator('#before').setInputFiles(fixture);
  await page.locator('#after').setInputFiles(fixture);
  await page.locator('#consent').check();
  await page.getByRole('button', { name: 'Extract & compare with AI' }).click();
  await expect(page.locator('#message')).toHaveText('AI provider unavailable in this test.');
  await expect(page.locator('#result')).toBeHidden();
  await expect(page.getByRole('button', { name: 'Extract & compare with AI' })).toBeEnabled();
});

test('local API rejects cross-site form submissions and hostile Host headers', async ({ request }) => {
  const missingHeader = await request.post('/api/compare', { multipart: { consent: 'send' } });
  expect(missingHeader.status()).toBe(403);
  const hostileHost = await request.get('/api/sample', { headers: { Host: 'malicious.example' } });
  expect(hostileHost.status()).toBe(403);
  const disabled = await request.post('/api/compare', { headers: { 'X-Label-Review': '1' }, multipart: { consent: 'send' } });
  expect(disabled.status()).toBe(503);
  const sample = await request.get('/api/sample');
  expect(sample.headers()['x-content-type-options']).toBe('nosniff');
  expect(sample.headers()['content-security-policy']).toContain("script-src 'self'");
});
