const { test, expect } = require('playwright/test');

test('TC-SMOKE-001 clicking the counter updates its visible value', async ({ page }) => {
  await page.setContent(`
    <button type="button" onclick="this.textContent = String(Number(this.textContent) + 1)">0</button>
  `);

  await page.getByRole('button', { name: '0', exact: true }).click();

  await expect(page.getByRole('button')).toHaveText('1');
});

test('TC-SMOKE-002 submitting a name shows the saved value', async ({ page }) => {
  await page.setContent(`
    <form onsubmit="event.preventDefault(); document.getElementById('saved').textContent = document.getElementById('name').value;">
      <label for="name">Name</label>
      <input id="name" name="name" />
      <button type="submit">Save</button>
    </form>
    <p role="status" id="saved">No saved value</p>
  `);

  await page.getByLabel('Name', { exact: true }).fill('Browser smoke');
  await page.getByRole('button', { name: 'Save', exact: true }).click();

  const expected = process.env.CIS_BROWSER_SMOKE_FAIL === '1'
    ? 'Intentionally incorrect value'
    : 'Browser smoke';
  await expect(page.getByRole('status')).toHaveText(expected);
});
