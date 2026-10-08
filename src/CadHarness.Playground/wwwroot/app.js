'use strict';
const $ = id => document.getElementById(id);
let snapshot = null, currentPlan = null, pending = false, confirmation = null, capability = null, lastState = '';
const text = (tag, value, cls) => { const n = document.createElement(tag); n.textContent = String(value); if (cls) n.className = cls; return n; };
const pretty = value => JSON.stringify(value, null, 2);
const human = key => key.replace(/([a-z])([A-Z])/g, '$1 $2').replaceAll('_', ' ');
function operationValue(key, value) {
  if (value && typeof value === 'object') {
    if (value.kind === 'centered_rectangle') return `${value.widthMm} × ${value.heightMm} mm · centered rectangle`;
    if (value.kind === 'circle') return `Ø${value.diameterMm} mm · circle`;
    if (value.semanticId && value.type) return `${value.semanticId} · ${value.type}`;
    if ('xMm' in value && 'yMm' in value) return `(${value.xMm}, ${value.yMm}) mm · local XY`;
    return pretty(value);
  }
  return `${value}${key.endsWith('Mm') ? ' mm' : key.endsWith('Deg') ? '°' : ''}`;
}
function tree(value, name = 'Details', open = false) {
  if (value === null || typeof value !== 'object') return text('div', `${human(name)}: ${value ?? 'unknown'}`, 'leaf');
  const d = document.createElement('details'); d.open = open; d.append(text('summary', `${human(name)}${Array.isArray(value) ? ` [${value.length}]` : ''}`));
  for (const [k, v] of Object.entries(value)) {
    const label = Array.isArray(value) && v && typeof v === 'object' ? v.semanticId || v.target || v.kind || `#${Number(k) + 1}` : k;
    d.append(tree(v, label, !['advanced', 'base64'].includes(k) && open && !Array.isArray(value)));
  }
  return d;
}
async function api(path, body, method) {
  const response = await fetch(path, { method: method || (body === undefined ? 'GET' : 'POST'), headers: body === undefined ? {} : { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  return response.json();
}
function showResult(result) {
  $('result').className = ''; $('result').replaceChildren(text('div', `${result.success ? 'PASS' : 'REJECTED / FAILED'} · ${result.stage}`, `resulttitle ${result.success ? 'pass' : 'fail'}`), text('div', result.message));
  if (result.failureCode) $('result').append(text('pre', result.failureCode, 'fail'));
  if (result.details?.planId) {
    $('result').append(text('div', `${result.details.program.operations.length} operations · ${result.details.program.relations.length} relations · ${result.details.edit ? 'edit' : 'create'} plan`, 'hint'));
    if (result.details.preflight) $('result').append(tree(result.details.preflight, 'Explicit pure preflight', true));
  } else if (result.details) {
    const output = {...result.details}; delete output.state;
    if (result.details.transaction) {
      const t = result.details.transaction;
      const flags = text('div', '', 'fields');
      ['mutationStarted','rebuildSucceeded','rollbackAttempted','rollbackSucceeded','stateCommitted','revision','stage'].forEach(k => flags.append(text('span',human(k),'fieldkey'),text('span',t[k] ?? 'unknown')));
      $('result').append(flags);
      if (Array.isArray(result.details.operations)) for (const op of result.details.operations.filter(o => typeof o.succeeded === 'boolean')) {
        const kind = currentPlan?.program.operations.find(o => o.semanticId === op.semanticId)?.kind || op.semanticId;
        $('result').append(text('div', `${op.succeeded ? '✓' : '✕'} ${kind} · ${op.message}`, `event ${op.succeeded ? 'pass' : 'fail'}`));
      }
    }
    $('result').append(tree(output, 'Production result / timing / validation reads', false));
  }
  if (result.stage === 'planning' && result.success) $('pipelineSummary').textContent = 'CadPlanner PASS · provider structured output → strict envelope → CadProgram → runtime capability → pure preflight. Awaiting explicit preflight approval.';
  else $('pipelineSummary').textContent = `${result.stage} · ${result.failureCode || (result.success ? 'PASS' : 'FAIL')} · ${result.message}`;
  if (!result.success && ['provider_structured_output','strict_envelope','cad_program_parse','runtime_capability_validation','pure_preflight','input','planning'].includes(result.stage)) $('plannerTree').replaceChildren(tree(result.details || {}, 'Rejected planner response', true));
}
async function action(path, body = {}) {
  if (pending) return;
  pending = true; controls();
  try { const result = await api(path, body); showResult(result); await refresh(); return result; }
  catch { showResult({ success: false, stage: 'connection', failureCode: 'HOST_UNAVAILABLE', message: 'Local host request failed. Refresh status before repeating any native action.' }); }
  finally { pending = false; controls(); }
}
function controls() {
  const busy = pending || snapshot?.busy || !snapshot;
  document.querySelectorAll('[data-action]').forEach(b => b.disabled = !!busy);
  const p = currentPlan, live = snapshot?.solidworks?.partOpen && snapshot?.solidworks?.healthy;
  $('preflight').disabled = busy || !p || p.edit;
  $('execute').disabled = busy || !p || p.edit || !p.preflightId || !snapshot?.solidworks?.connected || snapshot?.solidworks?.partOpen;
  $('editGenerate').disabled = busy || !live;
  $('editPreflight').disabled = busy || !p?.edit || !live;
  $('editExecute').disabled = busy || !p?.edit || !p.preflightId || !live;
  $('close').disabled = busy || !snapshot?.solidworks?.partOpen;
  $('connect').disabled = busy || snapshot?.solidworks?.connected;
  $('generate').disabled = busy || !snapshot?.llm?.configured;
  $('editGenerate').disabled ||= !snapshot?.llm?.configured;
  $('test').disabled = busy || !snapshot?.llm?.configured;
  $('confirmExecute').disabled = !!busy;
}
function renderPlan(p) {
  $('planBadge').textContent = p ? `${p.edit ? 'EDIT' : 'CREATE'} · ${p.preflightId ? 'PRECHECK PASS' : 'AWAITING PRECHECK'}` : 'NO PLAN';
  if (!p) { $('visual').replaceChildren(text('div', 'No current plan. Generate a new plan to continue.', 'empty')); $('rawJson').textContent = 'No plan'; $('relations').replaceChildren(text('div', 'No current relations.', 'empty')); return; }
  const program = p.program; $('visual').replaceChildren();
  if (p.editDetails) $('visual').append(tree(p.editDetails, 'Requested parameter edit', true));
  program.operations.forEach((op, i) => {
    const card = text('div', '', 'operation'); card.append(text('h3', `${i + 1} · ${op.semanticId || op.id}`), text('div', op.kind, 'opkind'));
    const fields = text('div', '', 'fields');
    Object.entries(op).filter(([k]) => !['kind', 'semanticId'].includes(k)).forEach(([k, v]) => { fields.append(text('span', human(k), 'fieldkey'), text('span', operationValue(k, v))); });
    card.append(fields); $('visual').append(card); if (i < program.operations.length - 1) $('visual').append(text('div', '↓', 'flowarrow'));
  });
  $('rawJson').textContent = pretty(program);
  $('relations').replaceChildren();
  if (!program.relations?.length) $('relations').append(text('div', p.edit ? 'No new relations. Existing model relations are preserved by the transaction.' : 'No explicit relations in this plan.', 'empty'));
  else for (const r of program.relations) { const card = text('div', '', 'operation'); card.append(text('h3', r.subject), text('div', `${r.kind} → ${r.reference ?? '(none)'}`, 'opkind')); $('relations').append(card); }
  $('capabilityTree').replaceChildren(tree(p.capabilities, p.edit ? 'Live edit runtime projection' : 'Construction runtime projection', true));
  $('plannerTree').replaceChildren(tree(p.plannerDetails, 'Planner details', true));
  if (p.preflight) $('visual').append(tree(p.preflight, 'Explicit pure preflight', true));
}
async function refresh() {
  const [s, st, ev] = await Promise.all([api('/api/status'), api('/api/state'), api('/api/session/events')]);
  snapshot = s.details; const next = snapshot.plan;
  if (pretty(next) !== pretty(currentPlan)) { currentPlan = next; renderPlan(next); }
  const sw = snapshot.solidworks; $('status').replaceChildren();
  const badges = [['LLM', snapshot.llm.configured ? `${snapshot.llm.provider} · ${snapshot.llm.maskedKey}` : 'Not configured'], ['SOLIDWORKS', sw.connectionMode], ['Managed Part', sw.partOpen ? 'TEST PART OPEN' : 'None'], ['Session', sw.healthy ? 'Healthy' : 'Invalid'], ['Revision', sw.revision ?? '—'], ['Action', snapshot.busy ? 'BUSY' : 'Idle']];
  badges.forEach(([k, v]) => { const item = text('span', ''); item.append(text('b', k), text('span', v, k === 'Session' ? (sw.healthy ? 'pass' : 'fail') : '')); $('status').append(item); });
  $('revision').textContent = `REV ${sw.revision ?? '—'}`;
  if (pretty(st.details) !== lastState) { lastState = pretty(st.details); $('stateTree').className = ''; $('stateTree').replaceChildren(st.details.available ? tree(st.details, 'Committed CADState', true) : text('div', 'No committed live state.', 'empty')); }
  $('events').replaceChildren(); for (const event of ev.details) { const row = text('div', '', 'event'); row.append(text('time', new Date(event.time).toLocaleTimeString()), text('span', `${event.stage} · ${event.failureCode || (event.success ? 'PASS' : 'FAIL')} · ${event.message}`, event.success ? '' : 'fail')); $('events').append(row); }
  controls();
}
$('config').addEventListener('submit', async event => { event.preventDefault(); const input = { provider: $('provider').value, apiKey: $('key').value || null, endpoint: $('endpoint').value, model: $('model').value, timeoutSeconds: Number($('timeout').value), maxOutputTokens: Number($('tokens').value), environmentCredential: $('credential').value || null }; $('key').value = ''; await action('/api/llm/configure', input); input.apiKey = null; });
$('provider').addEventListener('change', () => { const deep = $('provider').value === 'deepseek'; $('endpoint').value = deep ? 'https://api.deepseek.com/responses' : 'https://api.openai.com/v1/responses'; $('model').value = deep ? 'deepseek-chat' : ''; });
$('credential').addEventListener('change', () => { $('key').value = ''; $('key').disabled = !!$('credential').value; });
$('test').onclick = () => action('/api/llm/test');
$('clear').onclick = () => { $('key').value = ''; action('/api/llm/clear'); };
$('generate').onclick = () => action('/api/plan', { intent: $('intent').value });
$('editGenerate').onclick = () => action('/api/edit/plan', { intent: $('editIntent').value, revision: snapshot.solidworks.revision });
$('preflight').onclick = () => action('/api/preflight', { planId: currentPlan.planId });
$('editPreflight').onclick = () => action('/api/edit/preflight', { planId: currentPlan.planId, revision: currentPlan.revision });
$('connect').onclick = () => action('/api/solidworks/connect');
$('close').onclick = () => action('/api/part/close');
function confirm(edit) {
  if (!currentPlan?.preflightId || pending || snapshot.busy) return;
  confirmation = { planId: currentPlan.planId, preflightId: currentPlan.preflightId, revision: currentPlan.revision, confirmed: true, autoClose: !edit && $('autoclose').checked };
  $('confirmTitle').textContent = edit ? 'Confirm one parameter transaction' : 'Confirm one test-owned Part';
  $('confirmDescription').textContent = `${edit ? 'This action will edit the current Playground-owned Part.' : 'This action will create one test-owned SOLIDWORKS Part.'} Operations: ${currentPlan.program.operations.length}. Relations: ${currentPlan.program.relations.length}. ${edit ? `Revision: ${currentPlan.revision}.` : `Auto-close: ${confirmation.autoClose ? 'ON' : 'OFF (inspect mode)'}.`}`;
  $('confirmExecute').textContent = edit ? 'Execute One Edit' : 'Execute One Part'; confirmation.edit = edit; $('confirmation').showModal();
}
$('execute').onclick = () => confirm(false); $('editExecute').onclick = () => confirm(true);
$('cancelConfirm').onclick = () => { confirmation = null; $('confirmation').close(); };
$('confirmExecute').onclick = () => { const request = confirmation; confirmation = null; $('confirmation').close(); if (!request) return; const edit = request.edit; delete request.edit; action(edit ? '/api/edit/execute' : '/api/execute', request); };
document.querySelectorAll('[data-tab]').forEach(button => button.onclick = () => { document.querySelectorAll('[data-tab]').forEach(b => b.classList.toggle('selected', b === button)); document.querySelectorAll('.tab').forEach(t => t.classList.toggle('active', t.id === button.dataset.tab)); });
function download(name, data) { const url = URL.createObjectURL(new Blob([pretty(data)], { type: 'application/json' })); const a = document.createElement('a'); a.href = url; a.download = name; a.click(); URL.revokeObjectURL(url); }
$('download').onclick = () => { if (currentPlan) download('cad-program.json', currentPlan.program); };
$('copy').onclick = async () => { if (currentPlan) { try { await navigator.clipboard.writeText(pretty(currentPlan.program)); toast('IR copied'); } catch { toast('Clipboard unavailable; use Download IR.'); } } };
$('export').onclick = async () => { const r = await action('/api/session/export'); if (r?.success) download('playground-session.json', r.details); };
function toast(message) { $('toast').textContent = message; $('toast').style.display = 'block'; setTimeout(() => $('toast').style.display = 'none', 2500); }
try { document.documentElement.classList.toggle('light', localStorage.getItem('playground-theme') === 'light'); } catch { }
$('theme').onclick = () => { const light = document.documentElement.classList.toggle('light'); try { localStorage.setItem('playground-theme', light ? 'light' : 'dark'); } catch { } };
async function initialize() { try { capability = (await api('/api/capabilities')).details; $('capabilityTree').replaceChildren(tree(capability, 'Construction runtime projection', true)); await refresh(); } catch { showResult({ success: false, stage: 'connection', failureCode: 'HOST_UNAVAILABLE', message: 'Cannot reach the local host.' }); } }
controls(); initialize(); setInterval(() => { if (!pending && !$('confirmation').open) refresh().catch(() => { snapshot = null; controls(); $('pipelineSummary').textContent = 'Host unavailable. Native action status is unknown; refresh before taking further action.'; }); }, 3000);
