import { chromium } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

// Hand-authored extraction fixtures and invented artwork share one source. This is not an AI recording.
const common = {
  productName: 'Oat & Cocoa Drink Mix',
  netContents: 'Net wt. 180 g',
  servingSize: '18 g (2 scoops)',
  servingsPerContainer: '10',
  factsPanel: 'Calories 70\nTotal fat 2 g | 3% DV\nSodium 55 mg | 2% DV\nTotal carbohydrate 12 g | 4% DV\nTotal sugars 6 g\nProtein 2 g',
  ingredients: 'Oat flour, cocoa, milk powder, cane sugar.',
  allergens: 'Contains: milk.',
  claims: 'Just add water.',
  directions: 'Mix 18 g with 150 mL water. Stir well.',
  warnings: '',
  storage: 'Keep sealed in a cool, dry place.',
  manufacturer: 'Example Foods\nDemonstration only. Not for sale.',
  visualMarks: 'Blue square badge reading Small batch. Vertical barcode illustration.',
  otherText: 'Revision A'
};
const changed = {
  ...common,
  netContents: 'Net wt. 216 g',
  servingsPerContainer: '12',
  factsPanel: common.factsPanel.replace('55 mg | 2%', '85 mg | 4%').replace('sugars 6 g', 'sugars 4 g'),
  ingredients: 'Oat flour, cocoa, milk powder, soy flour, cane sugar.',
  allergens: 'Contains: milk, [unclear]',
  claims: 'Mix hot or cold.',
  warnings: 'Do not use if seal is broken.',
  visualMarks: 'Orange circle badge reading Batch checked. Vertical barcode illustration.',
  otherText: 'Revision B'
};
const fixtures = [common, changed].map((label, index) => ({ fields: Object.entries(label).map(([key, value]) => ({
  key, value, evidence: index === 1 && key === 'allergens' ? 'Second allergen is blurred; inspect the original.' : value,
  state: !value ? 'absent' : index === 1 && key === 'allergens' ? 'unclear' : 'read'
})) }));
const directory = new URL('../src/LabelReview/wwwroot/samples/', import.meta.url);
await mkdir(directory, { recursive: true });
await writeFile(new URL('extractions.json', directory), JSON.stringify(fixtures, null, 2) + '\n');
const escape = text => text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: 620, height: 940 }, deviceScaleFactor: 1 });
  for (const [index, data] of [common, changed].entries()) {
    const facts = data.factsPanel.split('\n').map(line => `<tr><td>${escape(line)}</td></tr>`).join('');
    const allergen = index ? 'Contains: milk, <span class="blur">soy.</span>' : data.allergens;
    await page.setContent(`<!doctype html><html lang="en"><meta charset="utf-8"><title>Synthetic sample label</title><style>
      *{box-sizing:border-box}body{margin:0;background:white;color:#152f43;font-family:Arial,sans-serif;padding:36px;width:620px;height:940px;border:3px solid #173f60}
      .top{font-size:15px;color:#3a617d;border-bottom:2px solid #173f60;padding-bottom:12px}h1{font-size:44px;line-height:1.05;margin:24px 0 10px;max-width:450px}
      .claim{font-size:20px;color:#365d75;margin:10px 0 24px}.weight{font-size:19px;font-weight:bold}.columns{display:grid;grid-template-columns:252px 1fr;gap:24px;margin-top:28px}
      .facts{border:2px solid #172f40;padding:10px;color:#111}h2{font-size:25px;line-height:1.1;margin:0 0 8px}.facts p{font-size:14px;margin:8px 0}.facts table{border-collapse:collapse;width:100%;border-top:7px solid #111;font-size:15px}.facts td{border-bottom:1px solid #333;padding:10px 0}
      .copy{font-size:14px;line-height:1.45}.copy h3{font-size:15px;margin:0 0 5px}.copy p{margin:0 0 18px}.allergen{font-weight:bold;font-size:16px}.blur{filter:blur(2px)}
      .badge{display:flex;align-items:center;justify-content:center;background:${index ? '#ac570b' : '#245d85'};color:white;width:96px;height:84px;border-radius:${index ? '50%' : '0'};font-size:17px;font-weight:bold;text-align:center;margin:20px 0}
      .bottom{position:absolute;top:800px;left:36px;right:36px;border-top:2px solid #173f60;padding-top:14px;display:flex;justify-content:space-between;font-size:13px}.barcode{width:180px;height:40px;background:repeating-linear-gradient(90deg,#152f43 0 2px,white 2px 5px,#152f43 5px 6px,white 6px 10px)}.demo{position:absolute;bottom:20px;left:36px;font-size:12px;color:#456176}
      </style><div class="top">Example Foods / Invented product label</div><h1>${escape(data.productName)}</h1><div class="claim">${escape(data.claims)}</div><div class="weight">${escape(data.netContents)}</div>
      <div class="columns"><div><div class="facts"><h2>Nutrition Facts</h2><p>Serving size ${escape(data.servingSize)}</p><p>${escape(data.servingsPerContainer)} servings per container</p><table>${facts}</table></div><div class="badge">${index ? 'Batch<br>checked' : 'Small<br>batch'}</div></div>
      <div class="copy"><h3>Ingredients</h3><p>${escape(data.ingredients)}</p><p class="allergen">${allergen}</p><h3>Directions</h3><p>${escape(data.directions)}</p>${data.warnings ? `<h3>Warning</h3><p>${escape(data.warnings)}</p>` : ''}<h3>Storage</h3><p>${escape(data.storage)}</p></div></div>
      <div class="bottom"><div>Example Foods<br>Demonstration only. Not for sale.<br>${escape(data.otherText)}</div><div class="barcode"></div></div><div class="demo">Synthetic portfolio fixture. Nutrition values and barcode are illustrations.</div></html>`);
    await page.screenshot({ path: fileURLToPath(new URL(index ? 'after.png' : 'before.png', directory)), fullPage: false });
  }
} finally { await browser.close(); }
