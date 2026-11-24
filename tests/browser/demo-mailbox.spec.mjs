import { test, expect } from '@playwright/test';

async function scanDemo(page, who) {
  await page.goto('/');
  await expect(page.getByText(/DEMO MODE.*synthetic data only/i)).toBeVisible();
  await page.getByRole('link', { name: `Open demo ${who}` }).click();
  await expect(page).toHaveURL(/\/overview$/);
  await expect(page.getByRole('button', { name: 'Leave demo' })).toBeVisible();
  await expect(page.getByText(/Demo mailbox.*fake Gmail data/i)).toBeVisible();
  const scanLogs = [];
  page.on('console', message => {
    if (message.text().includes('[TidyMail:scan]')) scanLogs.push(message.text());
  });
  await page.getByRole('button', { name: 'Start scan' }).click();
  await expect(page.getByRole('button', { name: 'Continue to dashboard' })).toBeEnabled();
  await expect.poll(() => scanLogs.some(line => line.includes('Start scan requested'))).toBe(true);
  await expect.poll(() => scanLogs.some(line => line.includes('Mailbox scan complete') && line.includes('scanned=48'))).toBe(true);
  expect(scanLogs.join('\n')).not.toContain('user:demo-');
  await page.getByRole('button', { name: 'Continue to dashboard' }).click();
  await expect(page.getByRole('heading', { name: 'Mailbox cleanup dashboard' })).toBeVisible();
  await expect(page.locator('.row.g-3 .card').first()).toContainText('48');
}

test('two isolated demo accounts can scan and Alice can safely trash selected messages', async ({ browser }) => {
  const aliceContext = await browser.newContext();
  const bobContext = await browser.newContext();
  const alice = await aliceContext.newPage();
  const bob = await bobContext.newPage();
  try {
    await scanDemo(alice, 'Alice');
    // The actual Blazor checkbox / confirm handlers are exercised in Chromium.
    const senderCheckbox = alice.locator('.card-header input[type="checkbox"]').first();
    await expect(senderCheckbox).toBeVisible();
    await senderCheckbox.check();
    await expect(alice.getByText(/Selected messages: 8/)).toBeVisible();
    await alice.getByRole('button', { name: 'Move to trash' }).click();
    await expect(alice.getByText('Confirm action')).toBeVisible();
    const riskAck = alice.locator('#ackRisk');
    if (await riskAck.count()) await riskAck.check();
    const hiddenAck = alice.locator('#ackHidden');
    if (await hiddenAck.count()) await hiddenAck.check();
    await alice.getByRole('button', { name: 'Confirm', exact: true }).click();
    await expect(alice.getByText(/Gmail acknowledged 8 of 8 messages/).first()).toBeVisible();
    await expect(alice.locator('.row.g-3 .card').first()).toContainText('40');

    // Bob remains untouched, including cookie identity, scan and mailbox stats.
    await scanDemo(bob, 'Bob');
    await expect(bob.locator('.row.g-3 .card').first()).toContainText('48');
    await expect(bob.getByText(/user:demo-bob/)).toBeVisible();
  } finally {
    await aliceContext.close();
    await bobContext.close();
  }
});
