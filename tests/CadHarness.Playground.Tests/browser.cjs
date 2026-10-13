// All requests target the dedicated M19 mock host; this script cannot reach CAD or a provider.
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.PLAYWRIGHT_PACKAGE || 'playwright');
const root = path.resolve(__dirname, '../..');
const output = path.join(root, 'artifacts/milestone19/ui', new Date().toISOString().replace(/[:.]/g, ''));
fs.mkdirSync(output, { recursive: true });
const tests = [], errors = [];
function check(value, name) { tests.push({ name, passed: !!value }); fs.writeFileSync(path.join(output, `step_${tests.length}.json`), JSON.stringify(tests.at(-1)), { flag: 'wx' }); if (!value) throw Error(name); }
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.BROWSER_EXECUTABLE || 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
  try {
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    const page = await context.newPage(); page.on('pageerror', e => errors.push(e.message));
    await page.goto('http://127.0.0.1:5188');
    await page.waitForFunction(() => document.getElementById('status').textContent.includes('Idle'));
    check(await page.locator('#modernExecute').isDisabled(), 'initial execution disabled');
    await page.locator('#modernConnect').click();
    await page.waitForFunction(() => document.getElementById('status').textContent.includes('MOCK ONLY'));
    await page.locator('#partPath').fill('D:\\mock\\source.SLDPRT'); await page.locator('#openModel').click();
    check(await page.locator('#confirmation').isVisible(), 'intake explicitly confirmed');
    await page.locator('#cancelConfirm').click(); check((await page.locator('.featureRow').count()) === 0, 'cancel intake does not open');
    await page.locator('#openModel').click(); await page.locator('#confirmExecute').click();
    await page.waitForFunction(() => document.querySelectorAll('.featureRow').length === 5);
    check((await page.locator('#modelBadge').textContent()).includes('OBSERVED_READ_ONLY'), 'external inspection read-only shown');
    check(await page.locator('#scalarPreview').isDisabled(), 'read-only scalar disabled');
    await page.locator('[data-feature="pattern"]').click();
    await page.waitForFunction(() => document.getElementById('relationGraph').textContent.includes('PatternSeed'));
    check((await page.locator('#relationGraph').textContent()).includes('observed_native'), 'native edge provenance shown');
    await page.screenshot({ path: path.join(output, 'desktop-inspection.png'), fullPage: true });
    await page.locator('#intakeMode').selectOption('external_copy'); await page.locator('#openModel').click(); await page.locator('#confirmExecute').click();
    await page.waitForFunction(() => document.getElementById('modelBadge').textContent.includes('COMMITTED'));
    check((await page.locator('#modelIdentity').textContent()).includes('source.SLDPRT') && (await page.locator('#modelIdentity').textContent()).includes('working.SLDPRT'), 'source / working copy separately displayed');
    await page.locator('#queryNew').fill('8'); await page.locator('#addQuery').click(); await page.locator('#batchPreview').click();
    await page.waitForFunction(() => document.getElementById('result').textContent.includes('AMBIGUOUS_TARGET_SET'));
    check(await page.locator('#modernExecute').isDisabled(), 'ambiguous query cannot execute');
    await page.locator('#queryClauses button').click();
    await page.locator('#queryOld').fill('10'); await page.locator('#addQuery').click();
    await page.locator('#queryOld').fill('6'); await page.locator('#addQuery').click(); await page.locator('#batchPreview').click();
    await page.waitForFunction(() => document.getElementById('modernBadge').textContent.includes('PREFLIGHT'));
    check((await page.locator('#modernPlan').textContent()).includes('hole_b') && (await page.locator('#modernPlan').textContent()).includes('hole_c'), 'exact native target set and validation scope displayed');
    await page.locator('#modernExecute').click();
    check((await page.locator('#confirmDescription').textContent()).includes('hole_b\nhole_c'), 'exact execution order in confirmation');
    await page.locator('#cancelConfirm').click();
    await page.screenshot({ path: path.join(output, 'desktop-batch.png'), fullPage: true });
    await page.setViewportSize({ width: 390, height: 844 }); await page.screenshot({ path: path.join(output, 'mobile-batch.png'), fullPage: true });
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'mobile no horizontal overflow');
    check(await page.evaluate(() => [...document.querySelectorAll('button')].filter(b => b.getClientRects().length).every(b => b.scrollWidth <= b.clientWidth + 1)), 'mobile button labels fit');
    await page.locator('#modernExecute').click(); await page.locator('#confirmExecute').click();
    await page.waitForFunction(() => document.getElementById('modelIdentity').textContent.includes('revision: 5'));
    check(await page.locator('#modernExecute').isDisabled(), 'committed preview consumed / no duplicate dispatch');
    await page.locator('#modernClose').click(); await page.waitForFunction(() => document.getElementById('modelBadge').textContent === 'NO MODEL');
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.locator('#key').fill('mock_ui_secret_0123456789'); await page.locator('#config button[type="submit"]').click();
    await page.waitForFunction(() => document.getElementById('status').textContent.includes('deepseek'));
    check(await page.locator('#key').inputValue() === '', 'credential input erased on configure');
    await page.locator('#constructionIntent').fill('Create a rectangular prism, width 62, height 46 and depth 9 mm, using disclosed zero placement.');
    for (const [key, value] of [['width', '62'], ['height', '46'], ['depth', '9']]) {
      await page.locator('#addRequirement').click(); const r = page.locator('.requirement').last(); await r.locator('input').nth(0).fill(key); await r.locator('input').nth(1).fill(value);
    }
    for (const key of ['zero', 'frame_angle']) {
      await page.locator('#addRule').click(); const r = page.locator('.rule').last(); await r.locator('input').nth(0).fill(key); await r.locator('select').selectOption(key === 'frame_angle' ? 'layout_zero_angle' : 'layout_zero');
    }
    await page.locator('#constructionPlan').click();
    await page.waitForFunction(() => document.getElementById('modernBadge').textContent.includes('CONSTRUCTION'));
    check(await page.locator('#modernExecute').isDisabled(), 'construction plan alone cannot execute');
    check(await page.locator('.profileDrawing').count() === 1, 'actual compiled sketch profile rendered');
    check((await page.locator('#modernPlan').textContent()).includes('given') && (await page.locator('#modernPlan').textContent()).includes('defaulted'), 'given / defaulted provenance displayed');
    await page.locator('#constructionPreflight').click(); await page.waitForFunction(() => document.getElementById('modernBadge').textContent.includes('PREFLIGHT'));
    await page.screenshot({ path: path.join(output, 'desktop-construction.png'), fullPage: true });
    await page.setViewportSize({ width: 390, height: 844 }); await page.screenshot({ path: path.join(output, 'mobile-construction.png'), fullPage: true });
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'mobile populated construction no overflow');
    const graphic = await page.locator('.profileDrawing').evaluate(s => ({ width: s.getBoundingClientRect().width, height: s.getBoundingClientRect().height, polygon: s.querySelector('polygon')?.getAttribute('points') }));
    check(graphic.width > 250 && graphic.height === 150 && graphic.polygon?.includes('31'), 'profile drawing nonblank / stable dimensions');
    await page.locator('#modernExecute').click(); await page.locator('#confirmExecute').click();
    await page.waitForFunction(() => document.getElementById('modelBadge').textContent.includes('MANAGED'));
    check((await page.locator('#modelBadge').textContent()).includes('COMMITTED'), 'managed committed model distinguished from external and provisional');
    await page.locator('#modernClose').click(); await page.waitForFunction(() => document.getElementById('modelBadge').textContent === 'NO MODEL');
    check(errors.length === 0, 'no browser runtime errors');
    await page.locator('#clear').click();
    check(!(await page.locator('body').textContent()).includes('mock_ui_secret_0123456789'), 'secret absent from visible / retained UI');
    fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify({ verdict: 'PASS', tests, errors, nativeParts: 0, nativeOpens: 0, realProviderCalls: 0 }, null, 2), { flag: 'wx' });
    console.log(`${tests.length} browser checks PASS; no native / provider. ${output}`);
  } catch (error) { fs.writeFileSync(path.join(output, 'failure.json'), JSON.stringify({ error: error.stack, tests, errors }, null, 2), { flag: 'wx' }); throw error; }
  finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
