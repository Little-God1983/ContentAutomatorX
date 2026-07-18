const { chromium } = require('playwright-core');
const path = require('path');

const BASE = process.env.BASE || 'http://localhost:5090';
const OUT = process.env.OUT_DIR || process.cwd();
const SHOT = (n) => path.join(OUT, `shot-${n}.png`);
const log = (...a) => console.log('[drive]', ...a);

(async () => {
  const browser = await chromium.launch({
    executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe',
    headless: true,
  });
  const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 } });
  const page = await ctx.newPage();
  page.setDefaultTimeout(20000);
  const errors = [];
  page.on('pageerror', (e) => errors.push(`pageerror: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(`console: ${m.text()}`); });

  const appbarButtons = () => page.locator('header.mud-appbar button');
  const switcherText = async () => {
    const btns = appbarButtons();
    const n = await btns.count();
    return n ? (await btns.nth(n - 1).innerText()).replace(/\s+/g, ' ').trim() : '(none)';
  };
  const clickSwitcher = async () => {
    const btns = appbarButtons();
    await btns.nth((await btns.count()) - 1).click();
  };
  const menuItemsLoc = () => page.locator('.mud-popover .mud-menu-item, .mud-popover .mud-list-item');
  const menuOpenCount = () => page.evaluate(() => document.querySelectorAll('.mud-popover .mud-menu-item, .mud-popover .mud-list-item').length);
  const openMenu = async () => {
    // Strict real-user behavior: ONE plain click must open the menu.
    // No keyboard fallback — that masked the MudBlazor 9 ActivatorContent
    // click-wiring bug once already.
    await page.locator('.mud-menu-activator').click();
    await page.waitForFunction(
      () => document.querySelectorAll('.mud-popover .mud-menu-item, .mud-popover .mud-list-item').length > 0,
      null, { timeout: 4000 });
  };
  const menuItems = async () => (await menuItemsLoc().allInnerTexts()).map((t) => t.replace(/\s+/g, ' ').trim());
  const clickMenuItem = async (text) => menuItemsLoc().filter({ hasText: text }).first().click();
  const waitCircuit = async () => {
    await page.waitForFunction(() => !document.querySelector('.mud-progress-linear'), null, { timeout: 20000 });
  };
  const fillDialog = async (name, slug) => {
    await page.waitForSelector('.mud-dialog');
    const inputs = page.locator('.mud-dialog input');
    await inputs.nth(0).fill(name);
    await page.waitForTimeout(350);
    const derived = await inputs.nth(1).inputValue();
    if (slug !== undefined) { await inputs.nth(1).fill(slug); }
    return derived;
  };
  const createBtn = () => page.locator('.mud-dialog button').filter({ hasText: 'Create & switch' });

  // ---- 1. fresh DB: empty state ----
  await page.goto(BASE);
  await waitCircuit();
  const initial = await switcherText();
  const hint = await page.locator('.mud-main-content').innerText();
  log('STEP1 empty-state switcher:', JSON.stringify(initial));
  log('STEP1 main content contains no-tenant hint:', hint.includes('No tenant yet'), '| create-from-menu copy:', hint.includes('top-right'));
  await page.screenshot({ path: SHOT('1-empty') });

  // ---- 2. create first tenant via the no-tenant button; slug auto-derives ----
  await clickSwitcher(); // "No tenant — create one" opens the dialog directly
  const derived1 = await fillDialog('Alpha Media');
  log('STEP2 slug derived from "Alpha Media" (expect alpha-media):', JSON.stringify(derived1));
  await createBtn().click();
  await page.waitForSelector('.mud-dialog', { state: 'detached' });
  await page.waitForTimeout(800);
  log('STEP2b switcher after first create (expect Alpha Media):', JSON.stringify(await switcherText()));
  await page.screenshot({ path: SHOT('2-first-tenant') });
  // No reload here on purpose: the menu swapped in after the first create must
  // respond to a plain click immediately (regression check for the
  // ActivatorContent click-wiring bug).

  // ---- 3. second tenant; manual slug edit stops auto-derive ----
  await openMenu();
  await clickMenuItem('New tenant');
  await page.waitForSelector('.mud-dialog');
  const inputs = page.locator('.mud-dialog input');
  await inputs.nth(0).fill('Beta Studio');
  await page.waitForTimeout(350);
  const derived2 = await inputs.nth(1).inputValue();
  await inputs.nth(1).fill('beta');           // manual edit
  await inputs.nth(0).fill('Beta Studio Renamed');
  await page.waitForTimeout(350);
  const afterManual = await inputs.nth(1).inputValue();
  log('STEP3 derived (expect beta-studio):', JSON.stringify(derived2), '| after manual edit + name change (expect beta):', JSON.stringify(afterManual));
  await createBtn().click();
  await page.waitForSelector('.mud-dialog', { state: 'detached' });
  await page.waitForTimeout(800);
  log('STEP3b switcher (expect Beta Studio Renamed):', JSON.stringify(await switcherText()));

  // ---- 4. duplicate slug rejected, dialog stays open ----
  await openMenu();
  await clickMenuItem('New tenant');
  await fillDialog('Dup Test', 'beta');
  await createBtn().click();
  await page.waitForTimeout(800);
  const dlgOpen = (await page.locator('.mud-dialog').count()) > 0;
  const snacks = await page.locator('.mud-snackbar').allInnerTexts().catch(() => []);
  log('STEP4 dup slug: dialog still open =', dlgOpen, '| snackbar:', JSON.stringify(snacks));
  await page.screenshot({ path: SHOT('4-dup-slug') });
  await page.locator('.mud-dialog button').filter({ hasText: 'Cancel' }).click();
  await page.waitForSelector('.mud-dialog', { state: 'detached' });

  // ---- 5. menu shows both tenants, check on current; switch to Alpha ----
  await openMenu();
  const items = await menuItems();
  log('STEP5 menu items:', JSON.stringify(items));
  const checkIcons = await page.locator('.mud-popover .mud-menu-item .mud-icon-root, .mud-popover .mud-list-item .mud-icon-root').count();
  log('STEP5 icon count in menu (check + add + settings >= 3):', checkIcons);
  await page.screenshot({ path: SHOT('5-menu') });
  await clickMenuItem('Alpha Media');
  await page.waitForTimeout(800);
  log('STEP5b switcher after switch (expect Alpha Media):', JSON.stringify(await switcherText()));

  // ---- 6. persistence across reload ----
  await page.reload();
  await waitCircuit();
  log('STEP6 switcher after reload (expect Alpha Media):', JSON.stringify(await switcherText()));
  await page.screenshot({ path: SHOT('6-after-reload') });

  // ---- 7. all pages: no tenant dropdown, no errors ----
  for (const route of ['/sources', '/recipes', '/content', '/drafts', '/runs']) {
    await page.goto(BASE + route);
    await waitCircuit();
    const dropdown = await page.locator('.mud-main-content label').filter({ hasText: /^Tenant$/ }).count();
    log(`STEP7 ${route}: tenant-dropdown=${dropdown} switcher=${JSON.stringify(await switcherText())}`);
  }
  await page.screenshot({ path: SHOT('7-last-page') });

  // ---- 8. manage tenants; rename active tenant reflects in switcher ----
  await page.goto(BASE);
  await waitCircuit();
  await openMenu();
  await clickMenuItem('Manage tenants');
  await page.waitForURL('**/tenants');
  await waitCircuit();
  const alphaRow = page.locator('tr').filter({ hasText: 'Alpha Media' });
  await alphaRow.locator('button').filter({ hasText: 'Edit' }).click();
  await page.waitForTimeout(400);
  const nameField = page.locator('.mud-main-content input').first();
  await nameField.fill('Alpha Media Renamed');
  await page.locator('.mud-main-content button').filter({ hasText: /^Save$/ }).click();
  await page.waitForTimeout(900);
  log('STEP8 switcher after rename (expect Alpha Media Renamed):', JSON.stringify(await switcherText()));
  await page.screenshot({ path: SHOT('8-renamed') });

  // Delete now requires typing the tenant name into a confirm dialog.
  const confirmInput = () => page.locator('.mud-dialog input');
  const confirmDeleteBtn = () => page.locator('.mud-dialog button').filter({ hasText: /^Delete$/ });
  const dialogCount = () => page.locator('.mud-dialog').count();

  // ---- 9. delete ACTIVE tenant (via confirm dialog) -> fallback to remaining ----
  const rowDel = page.locator('tr').filter({ hasText: 'Alpha Media Renamed' });
  await rowDel.locator('button').filter({ hasText: 'Delete' }).click();
  await page.waitForSelector('.mud-dialog');
  log('STEP9a Delete disabled with empty input (expect true):', await confirmDeleteBtn().isDisabled());
  await confirmInput().fill('Alpha');                 // wrong name
  await page.waitForTimeout(300);
  log('STEP9b Delete disabled with wrong name (expect true):', await confirmDeleteBtn().isDisabled());
  await page.keyboard.press('Enter');                 // Enter with wrong name must NOT delete
  await page.waitForTimeout(500);
  log('STEP9c dialog still open after Enter on wrong name (expect 1):', await dialogCount());
  await page.screenshot({ path: SHOT('9a-confirm-dialog') });
  await page.locator('.mud-dialog button').filter({ hasText: 'Cancel' }).click();
  await page.waitForSelector('.mud-dialog', { state: 'detached' });
  await page.waitForTimeout(300);
  const stillThere = await page.locator('tr').filter({ hasText: 'Alpha Media Renamed' }).count();
  log('STEP9d tenant survives Cancel (expect 1):', stillThere);

  await rowDel.locator('button').filter({ hasText: 'Delete' }).click();
  await page.waitForSelector('.mud-dialog');
  await confirmInput().fill('Alpha Media Renamed');   // exact name
  await page.waitForTimeout(300);
  log('STEP9e Delete enabled with exact name (expect false):', await confirmDeleteBtn().isDisabled());
  await confirmDeleteBtn().click();
  await page.waitForSelector('.mud-dialog', { state: 'detached' });
  await page.waitForTimeout(1000);
  log('STEP9f switcher after deleting active (expect Beta Studio Renamed):', JSON.stringify(await switcherText()));
  await page.screenshot({ path: SHOT('9-fallback') });

  // ---- 10. delete last tenant (confirm via Enter key) -> empty state returns ----
  const rowDel2 = page.locator('tr').filter({ hasText: 'Beta Studio Renamed' });
  await rowDel2.locator('button').filter({ hasText: 'Delete' }).click();
  await page.waitForSelector('.mud-dialog');
  await confirmInput().fill('Beta Studio Renamed');
  await page.waitForTimeout(300);
  await page.keyboard.press('Enter');                 // Enter path (fill leaves focus in the input)
  await page.waitForSelector('.mud-dialog', { state: 'detached' });
  await page.waitForTimeout(1000);
  const finalText = await switcherText();
  await page.goto(BASE);
  await waitCircuit();
  const finalHint = await page.locator('.mud-main-content').innerText();
  log('STEP10 switcher after deleting all (expect No tenant):', JSON.stringify(finalText), '| dashboard hint back:', finalHint.includes('No tenant yet'));
  await page.screenshot({ path: SHOT('10-empty-again') });

  log('JS ERRORS:', errors.length ? JSON.stringify(errors) : 'none');
  await browser.close();
  log('DONE');
})().catch((e) => { console.error('[drive] FAILED:', e.message); process.exit(1); });
