import { chromium } from '@playwright/test';
import { fileURLToPath } from 'node:url';

// Run the local app first. These captures always use synthetic sample data.
const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1100 }, deviceScaleFactor: 1 });
  await page.goto('http://127.0.0.1:5098');
  await page.getByRole('button', { name: 'Run sample comparison' }).click();
  await page.locator('#result').waitFor({ state: 'visible' });
  await page.evaluate(() => document.querySelector('.source-section').scrollIntoView());
  await page.screenshot({ path: fileURLToPath(new URL('../docs/workspace.png', import.meta.url)) });
  await page.locator('#findings').scrollIntoViewIfNeeded();
  await page.evaluate(() => document.querySelector('#findings').scrollIntoView());
  await page.screenshot({ path: fileURLToPath(new URL('../docs/findings.png', import.meta.url)) });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.evaluate(() => document.querySelector('#findings').scrollIntoView());
  await page.screenshot({ path: fileURLToPath(new URL('../docs/mobile.png', import.meta.url)) });
} finally { await browser.close(); }
