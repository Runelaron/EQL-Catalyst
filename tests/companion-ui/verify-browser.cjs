/* Actual bundled browser components with an offline, synthetic PC projection.
   No TLS validation bypass, network listener, real profile or production mock endpoint. */
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { chromium } = require('playwright');
(async () => {
  const root = path.resolve(__dirname, '../..');
  const out = process.env.CATALYST_BROWSER_OUT || path.join(root, 'test-results/companion-browser'); fs.mkdirSync(out, { recursive: true });
  const source = path.join(root, 'src/EqlTracker.Companion/wwwroot');
  fs.writeFileSync(path.join(out, 'index.html'), fs.readFileSync(path.join(source, 'index.html'), 'utf8').replaceAll('="/app.', '="./app.'));
  for (const file of ['app.js', 'app.css']) fs.copyFileSync(path.join(source, file), path.join(out, file));
  const fixture = JSON.parse(fs.readFileSync(path.join(process.env.CATALYST_FIXTURE_DIR||path.join(root,'test-results/integrated-companions'),'phone-projection.json'), 'utf8'));
  const themes=JSON.parse(fs.readFileSync(path.join(process.env.CATALYST_FIXTURE_DIR||path.join(root,'test-results/integrated-companions'),'phone-themes.json'),'utf8'));
  fixture.build.rows.push({ label: 'An exceptionally long equipment, spell, and upgrade-rank explanation retained for a compact phone and tablet layout without truncating its meaning', value: 'Unknown', detail: 'The effect has not been verified for this recipient, rank, instrument and AA context. No contribution of zero or universal upgrade is inferred.' });
  const browser = await chromium.launch({ headless: true,channel:process.env.CATALYST_BROWSER_CHANNEL||'msedge' }); const checks = [];
  for (const viewport of [{ width:320,height:568 }, {width:390,height:844}, {width:1024,height:768}]) {
    const page = await browser.newPage({ viewport });
    await page.addInitScript(data => {
      window.fixtureProjection=data;
      window.fetch = async (url, options) => {
        let value = url.includes('/session') ? { protocol:1,csrf:'fixture-only',serverId:'fixture' } : url.includes('/search') || url.includes('/history') ? { revision:data.revision,contextId:data.contextId,rows:[{label:url.includes('kind=loot')?'Older loot fixture':url.includes('/history')?'Older session fixture':'Shared lookup fixture',value:'Unknown',detail:'Synthetic retained history'}],more:false } : { protocol:1,type:'snapshot',revision:data.revision,at:data.at,data,status:'Connected' };
        return new Response(JSON.stringify(value), {status:200,headers:{'Content-Type':'application/json'}});
      };
      window.WebSocket = class { constructor(){ window.fixtureSocket=this; setTimeout(()=>this.onopen?.(),0); } close(){} };
    }, fixture);
    await page.goto(pathToFileURL(path.join(out,'index.html')).href); await page.getByRole('heading', {name:'Build & song coverage'}).waitFor();
    for (const [theme,tokens] of Object.entries(themes)) {
      await page.evaluate(tokens => window.fixtureSocket.onmessage({data:JSON.stringify({protocol:1,type:'snapshot',revision:window.fixtureProjection.revision,at:window.fixtureProjection.at,data:{...window.fixtureProjection,theme:tokens},status:'Connected'})}), tokens);
      for (const destination of ['build','goals','session']) {
        await page.locator(`nav button[data-view=${destination}]`).click();
        const details = page.locator('#content details'); if(await details.count())await details.first().locator('summary').click();
        const overflow = await page.evaluate(()=>document.documentElement.scrollWidth>window.innerWidth); if(overflow)throw Error(`Horizontal overflow: ${theme}/${destination}/${viewport.width}`);
        if(await page.locator('nav button').count()!==3)throw Error('Unexpected phone navigation');
        await page.screenshot({path:path.join(out,`${theme}-${destination}-${viewport.width}.png`),fullPage:true}); checks.push(`${theme}/${destination}/${viewport.width}`);
      }
    }
    await page.locator('nav button[data-view=goals]').click();await page.locator('#history-more').click();await page.locator('#history-results').getByText('Older loot fixture').waitFor();
    await page.locator('nav button[data-view=session]').click();if((await page.locator('#history-results').textContent()).includes('Older loot fixture'))throw Error('Loot history leaked into Session');
    await page.locator('#history-more').click();await page.locator('#history-results').getByText('Older session fixture').waitFor();
    await page.locator('nav button[data-view=goals]').click();if(!(await page.locator('#history-results').textContent()).includes('Older loot fixture'))throw Error('Loot history position was lost');checks.push(`separate-history/${viewport.width}`);
    await page.locator('#search').fill('fixture');await page.locator('#results').getByText('Shared lookup fixture').waitFor();
    await page.evaluate(()=>window.fixtureSocket.onmessage({data:JSON.stringify({protocol:1,type:'paused',revision:99,status:'Sharing paused'})}));
    if((await page.locator('#content').textContent()).trim())throw Error('Paused feed retained selected projection'); checks.push(`paused/${viewport.width}`);
    if((await page.locator('#results').textContent()).trim()||(await page.locator('#history-results').textContent()).trim())throw Error('Paused feed retained lookup/history');
    await page.evaluate(()=>{window.fixtureSocket.onmessage({data:JSON.stringify({protocol:99,type:'snapshot'})});window.fixtureSocket.onclose();});
    if(!(await page.locator('#status').textContent()).includes('Protocol mismatch'))throw Error('Protocol mismatch was overwritten by disconnect state');checks.push(`protocol-mismatch/${viewport.width}`);
    await page.close();
  }
  await browser.close(); fs.writeFileSync(path.join(out,'checks.json'),JSON.stringify({passed:checks.length,checks,syntheticProjection:true,actualBundledComponents:true,tlsBypass:false},null,2)); process.stdout.write(`${checks.length} browser layout/state checks passed\n`);
})().catch(error=>{process.stderr.write(error.stack+'\n');process.exit(1)});
