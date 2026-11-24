import { test, expect } from '@playwright/test';

async function assertStylesLoaded(page) {
  // Catch missing or stale CSS, not just missing text.
  const response = await page.request.get('/css/site.css?v=tidymail-ui-20261124-2');
  expect(response.ok(), 'Tidy-Mail stylesheet should be served').toBeTruthy();
  expect(await response.text()).toContain('.tm-stats-grid');
  await expect
    .poll(() => page.locator('.tm-stats-grid').evaluate((element) => getComputedStyle(element).display))
    .toBe('grid');
}

async function scanDemo(page, who) {
  await page.goto('/');
  await expect(page.getByText(/DEMO MODE.*synthetic data only/i)).toBeVisible();
  await page.getByRole('link', { name: 'Explore as demo ' + who }).click();
  await expect(page).toHaveURL(/\/overview$/);
  await expect(page.getByRole('button', { name: 'Leave demo' })).toBeVisible();
  await expect(page.getByText(/Demo mailbox.*fake Gmail data/i)).toBeVisible();

  await page.getByRole('button', { name: 'Start mailbox scan' }).click();
  const openDashboard = page.getByRole('button', { name: 'Open dashboard' });
  await expect(openDashboard).toBeEnabled();
  await openDashboard.click();

  await expect(page.getByRole('heading', { name: 'Mailbox overview' })).toBeVisible();
  await assertStylesLoaded(page);
  await expect(page.locator('.tm-stat-card').first()).toContainText('48');
  await page.screenshot({ path: 'test-results/tidy-mail-dashboard-' + who.toLowerCase() + '.png', fullPage: true });
}

test('two isolated demo accounts can scan and Alice can safely trash selected messages', async ({ browser }) => {
  const aliceContext = await browser.newContext();
  const bobContext = await browser.newContext();
  const alice = await aliceContext.newPage();
  const bob = await bobContext.newPage();
  try {
    await scanDemo(alice, 'Alice');
    await alice.getByRole('button', { name: 'Results', exact: true }).click();

    const senderCheckbox = alice.locator('.tm-results-list .card-header input[type="checkbox"]').first();
    await expect(senderCheckbox).toBeVisible();
    await senderCheckbox.check();
    await expect(alice.locator('.tm-selection-count')).toContainText('8 selected');

    await alice.getByRole('button', { name: 'Move to Trash' }).click();
    const preview = alice.getByRole('dialog', { name: 'Preview: Move to Trash' });
    await expect(preview).toBeVisible();
    await expect(preview).toContainText('8 messages');
    await alice.screenshot({ path: 'test-results/tidy-mail-confirmation-modal.png', fullPage: true });
    const riskAck = preview.getByLabel('I reviewed the potentially important messages.');
    if (await riskAck.count()) await riskAck.check();
    const hiddenAck = preview.getByLabel('Include hidden selected messages');
    if (await hiddenAck.count()) await hiddenAck.check();
    await preview.getByRole('button', { name: 'Confirm action' }).click();
    await expect(alice.getByText(/Gmail acknowledged 8 of 8 messages/).first()).toBeVisible();

    await alice.getByRole('button', { name: 'Overview', exact: true }).click();
    await expect(alice.locator('.tm-stat-card').first()).toContainText('40');

    // A different cookie-authenticated account must not see Alice's changes.
    await scanDemo(bob, 'Bob');
    await expect(bob.locator('.tm-stat-card').first()).toContainText('48');
    await expect(bob.getByText(/user:demo-bob/)).toBeVisible();
  } finally {
    await aliceContext.close();
    await bobContext.close();
  }
});
