const $ = (selector) => document.querySelector(selector);
let report = null;
let objectUrls = [];
const acknowledged = new Set();
let running = false;
let liveEnabled = false;
const labels = { changed: 'Changed', added: 'Added', removed: 'Removed', review: 'Unclear · check source', unchanged: 'Unchanged' };

function message(text, error = false) {
  $('#message').textContent = text;
  $('#message').classList.toggle('error', error);
}
function busy(value) {
  running = value;
  $('#sample').disabled = value;
  $('#compare').disabled = value || !liveEnabled;
  $('#upload').inert = value;
  $('#result').inert = value;
}
function element(tag, className, text) {
  const item = document.createElement(tag);
  if (className) item.className = className;
  if (text !== undefined) item.textContent = text;
  return item;
}
function resetResult() {
  $('#result').hidden = true;
  report = null;
  acknowledged.clear();
  $('#source-checked').checked = false;
  $('#show-unchanged').checked = false;
  $('#before-preview').replaceChildren();
  $('#after-preview').replaceChildren();
  objectUrls.forEach(URL.revokeObjectURL);
  objectUrls = [];
}
function preview(side, source) {
  const url = source instanceof File ? URL.createObjectURL(source) : source;
  if (source instanceof File) objectUrls.push(url);
  const isPdf = source instanceof File && source.name.toLowerCase().endsWith('.pdf');
  const view = element(isPdf ? 'iframe' : 'img');
  if (isPdf) { view.title = `${side} label PDF preview`; view.setAttribute('sandbox', ''); }
  else view.alt = `${side} label`;
  view.src = url;
  $(`#${side}-preview`).replaceChildren(view);
  $(`#${side}-link`).href = url;
}
function showReport(data, before, after) {
  report = data;
  $('#mode').textContent = data.mode === 'sample' ? 'Sample fixtures · no AI call' : 'Live AI extraction';
  $('#disclosure').textContent = data.disclosure;
  $('#raw').textContent = JSON.stringify(data, null, 2);
  preview('before', before);
  preview('after', after);
  renderRows();
  $('#result').hidden = false;
  const count = data.rows.filter(row => row.status !== 'unchanged').length;
  message(`Comparison ready. ${count} findings need your review.`);
}
function renderRows() {
  if (!report) return;
  const rows = $('#rows');
  rows.replaceChildren();
  let category = '';
  const findings = report.rows.filter(row => row.status !== 'unchanged');
  const unclear = findings.filter(row => row.status === 'review').length;
  $('#summary').textContent = `${findings.length} findings, including ${unclear} unclear. ${report.rows.length - findings.length} fields unchanged.`;
  for (const row of report.rows) {
    if (row.status === 'unchanged' && !$('#show-unchanged').checked) continue;
    if (row.category !== category) {
      category = row.category;
      rows.append(element('h3', 'category', category));
    }
    const article = element('article', 'finding');
    article.dataset.status = row.status;
    const heading = element('div', 'finding-head');
    heading.append(element('h3', '', row.title), element('span', 'tag', `${labels[row.status]} / ${row.kind}`));
    article.append(heading);
    const pair = element('div', 'field-pair');
    for (const [name, field] of [['Previous', row.before], ['New', row.after]]) {
      const cell = element('div', 'field');
      cell.append(element('span', 'field-label', `${name} · ${field.state}`));
      cell.append(element('p', 'value', field.state === 'absent' ? 'Not present on label' : field.value || 'Unable to read'));
      if (field.evidence) cell.append(element('p', 'evidence', `Evidence: ${field.evidence}`));
      pair.append(cell);
    }
    article.append(pair);
    if (row.status !== 'unchanged') {
      const label = element('label', 'check acknowledge');
      const checkbox = element('input');
      checkbox.type = 'checkbox';
      checkbox.checked = acknowledged.has(row.key);
      checkbox.addEventListener('change', () => {
        if (checkbox.checked) acknowledged.add(row.key); else acknowledged.delete(row.key);
        progress();
      });
      label.append(checkbox, element('span', '', `I checked ${row.title.toLowerCase()} against both originals.`));
      article.append(label);
    }
    rows.append(article);
  }
  if (!findings.length) rows.append(element('p', '', 'No differences were found in the extracted fields. Check the originals for missed content.'));
  progress();
}
function progress() {
  if (!report) return;
  const total = report.rows.filter(row => row.status !== 'unchanged').length;
  const complete = acknowledged.size === total && $('#source-checked').checked;
  $('#review-progress').textContent = `${acknowledged.size} of ${total} findings checked. ${complete ? 'Review acknowledgments complete; no label approval is implied.' : 'Review is still pending.'}`;
}
async function readResponse(response) {
  const data = await response.json();
  if (!response.ok) throw new Error(data.detail || data.title || `Request failed (${response.status}).`);
  return data;
}
$('#sample').addEventListener('click', async () => {
  if (running) return;
  resetResult();
  busy(true);
  message('Loading prepared extraction fixtures and comparing fields…');
  try {
    const data = await readResponse(await fetch('/api/sample'));
    showReport(data, '/samples/before.png', '/samples/after.png');
  } catch (error) { message(error.message, true); }
  finally { busy(false); }
});
$('#upload').addEventListener('submit', async (event) => {
  event.preventDefault();
  if (running || !liveEnabled) return;
  const before = $('#before').files[0];
  const after = $('#after').files[0];
  if (!before || !after || !$('#consent').checked) return;
  if ([before, after].some(file => file.size === 0 || file.size > 6 * 1024 * 1024)) {
    message('Each label must be between 1 byte and 6 MiB.', true);
    return;
  }
  resetResult();
  busy(true);
  message('Reading both labels with AI, then comparing the extracted fields. This may take up to three minutes…');
  const form = new FormData();
  form.append('before', before);
  form.append('after', after);
  form.append('consent', 'send');
  try {
    const data = await readResponse(await fetch('/api/compare', { method: 'POST', headers: { 'X-Label-Review': '1' }, body: form }));
    showReport(data, before, after);
  } catch (error) { message(error.message || 'Comparison could not finish. Check the server and try again.', true); }
  finally { busy(false); }
});
$('#show-unchanged').addEventListener('change', renderRows);
$('#source-checked').addEventListener('change', progress);
$('#export').addEventListener('click', () => {
  if (!report || running) return;
  const total = report.rows.filter(row => row.status !== 'unchanged').length;
  const artifact = { ...report, review: {
    exportedAt: new Date().toISOString(),
    state: acknowledged.size === total && $('#source-checked').checked ? 'acknowledged' : 'pending',
    originalsChecked: $('#source-checked').checked,
    acknowledgedFields: [...acknowledged],
    note: 'Browser-local acknowledgments, not a signed audit record or label approval.'
  } };
  const url = URL.createObjectURL(new Blob([JSON.stringify(artifact, null, 2)], { type: 'application/json' }));
  const link = element('a');
  link.href = url;
  link.download = `label-review-${report.mode}.json`;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
});
fetch('/api/status').then(readResponse).then(status => {
  liveEnabled = status.liveEnabled;
  $('#live-status').textContent = liveEnabled ? `Live AI ready. Model: ${status.model}. Labels are sent to OpenAI only when you submit.` : 'Live AI is off. Set OPENAI_API_KEY and OPENAI_MODEL on the server to enable uploads.';
  $('#compare').disabled = !liveEnabled || running;
}).catch(() => message('Cannot reach the local server. Restart the app and reload.', true));
