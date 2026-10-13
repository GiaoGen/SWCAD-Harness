'use strict';
let modelView = null, modernView = null, selectedFeature = null, queryChanges = [], modernKey = '';
function removeTool(label) { const b = text('button', '×'); if (window.playgroundIcon) b.replaceChildren(window.playgroundIcon('x')); b.title = label; b.setAttribute('aria-label', label); return b; }
const modernTargets = () => modernView?.kind === 'batch' ? modernView.data.orderedEdits.map(e => e.target) : modernView?.kind === 'scalar' ? [modernView.data.request.target] : [];
function options(select, features, blank) {
  select.replaceChildren(text('option', blank)); select.firstChild.value = '';
  features.forEach(f => { const o = text('option', f.name); o.value = f.id; select.append(o); });
}
function renderModel(model) {
  modelView = model; selectedFeature = null;
  $('modelBadge').textContent = model ? `${model.origin.toUpperCase()} / ${model.phase.toUpperCase()}` : 'NO MODEL';
  $('modelIdentity').className = ''; $('modelIdentity').replaceChildren(); $('modelFeatures').replaceChildren();
  $('relationGraph').replaceChildren(text('div', 'No feature selected.', 'empty'));
  if (!model) { $('modelIdentity').append(text('div', 'No model selected.', 'empty')); return; }
  $('modelIdentity').append(tree({ source: model.sourcePath, workingCopy: model.workingPath, configuration: model.configuration, revision: model.revision,
    inventory: model.inventoryComplete ? 'complete' : 'partial', ownership: model.owned ? 'test-owned' : 'closed inspection copy', support: model.editable ? 'qualified' : 'read-only' }, 'Identity', true));
  for (const f of model.features) {
    const row = document.createElement('button'); row.className = 'featureRow'; row.dataset.feature = f.id;
    row.append(text('span', f.name), text('span', f.support.toUpperCase(), f.support === 'editable' ? 'pass' : 'muted'), text('small', `${f.kind} / ${f.health}`));
    row.onclick = () => selectFeature(f); $('modelFeatures').append(row);
  }
  options($('queryHost'), model.features, 'Any host'); options($('queryRelationTarget'), model.features, 'None');
}
async function selectFeature(feature) {
  selectedFeature = feature;
  document.querySelectorAll('.featureRow').forEach(b => b.classList.toggle('selected', b.dataset.feature === feature.id));
  const result = await api('/api/model/relations?target=' + encodeURIComponent(feature.id));
  if (selectedFeature !== feature) return;
  const graph = $('relationGraph'); graph.className = ''; graph.replaceChildren();
  if (!result.success) { graph.append(text('div', result.failureCode, 'fail')); return; }
  const g = result.details, lanes = text('div', '', 'graphLanes');
  for (const [title, nodes] of [['Parents', g.parents], ['Selected', [g.focus]], ['Children', g.children]]) {
    const lane = text('div', '', 'graphLane'); lane.append(text('h3', title));
    if (!nodes.length) lane.append(text('span', 'No recorded edge', 'muted'));
    nodes.forEach(f => { const b = text('button', f.name); b.onclick = () => selectFeature(modelView.features.find(n => n.id === f.id)); lane.append(b); }); lanes.append(lane);
  }
  graph.append(lanes, text('div', `Dependencies: ${feature.dependencyStatus} / ${feature.reason}`, 'muted'));
  g.edges.forEach(e => graph.append(text('div', `${e.from} → ${e.to} / ${e.kind} / ${e.provenance}`, 'edgeRow')));
  feature.parameters.forEach(p => {
    const row = text('div', '', 'parameterRow'); row.append(text('span', `${human(p.key)} = ${p.value} ${p.unit}`), text('span', p.editable ? 'EDITABLE' : 'READ ONLY', p.editable ? 'pass' : 'muted'));
    if (p.editable && modelView.origin === 'external') { const b = text('button', 'Select'); b.onclick = () => { $('queryParameter').value = p.key; $('queryOld').value = p.value; $('queryNew').value = p.value; }; row.append(b); }
    graph.append(row);
  });
  graph.append(tree({ nativeReference: feature.nativeReference, geometry: feature.geometry }, 'Native / geometry', false)); modernControls();
}
function profileDrawing(op) {
  if (!op.sketch) return null;
  const s = op.sketch, points = s.points, xs = points.map(p => p.x.millimeters), ys = points.map(p => p.y.millimeters);
  const radius = Math.max(...s.entities.map(e => e.radius?.millimeters || e.width?.millimeters / 2 || 0), 0);
  const minX = Math.min(...xs) - radius, minY = Math.min(...ys) - radius, width = Math.max(...xs) - minX + radius, height = Math.max(...ys) - minY + radius;
  const pad = Math.max(width, height, 1) * .12, ns = 'http://www.w3.org/2000/svg';
  const svg = document.createElementNS(ns, 'svg'); svg.setAttribute('viewBox', `${minX-pad} ${-minY-height-pad} ${width+2*pad} ${height+2*pad}`); svg.setAttribute('role', 'img'); svg.setAttribute('aria-label', `${op.semanticId} local profile`); svg.classList.add('profileDrawing');
  for (const e of s.entities) {
    const ps = e.points.map(id => points.find(p => p.id === id)); let shape;
    if (e.kind === 'circle') { shape = document.createElementNS(ns, 'circle'); shape.setAttribute('cx', ps[0].x.millimeters); shape.setAttribute('cy', -ps[0].y.millimeters); shape.setAttribute('r', e.radius.millimeters); }
    else if (e.kind === 'slot') { shape = document.createElementNS(ns, 'line'); shape.setAttribute('x1', ps[0].x.millimeters); shape.setAttribute('y1', -ps[0].y.millimeters); shape.setAttribute('x2', ps[1].x.millimeters); shape.setAttribute('y2', -ps[1].y.millimeters); shape.style.strokeWidth = e.width.millimeters; shape.style.strokeLinecap = 'round'; shape.style.vectorEffect = 'none'; }
    else { shape = document.createElementNS(ns, 'polygon'); shape.setAttribute('points', ps.map(p => `${p.x.millimeters},${-p.y.millimeters}`).join(' ')); }
    svg.append(shape);
  }
  return svg;
}
function renderModern(view) {
  modernView = view; $('modernBadge').textContent = view ? `${view.kind.toUpperCase()} / ${view.stage.toUpperCase()}` : 'NO PREVIEW';
  const panel = $('modernPlan'); panel.className = ''; panel.replaceChildren();
  if (!view) { panel.append(text('div', 'No preview.', 'empty')); return; }
  panel.append(text('div', `PROVISIONAL / Revision ${view.revision} / ${view.executable ? 'PRECHECK PASS' : 'PLAN ONLY'}`, 'previewStatus'));
  if (view.kind === 'construction') {
    const response = view.data.response;
    const dims = document.createElement('table'); dims.append(text('caption', 'Requirement provenance'));
    response.requirements.requirements.forEach(r => { const row = document.createElement('tr'); [r.semanticKey, `${r.value ?? 'unresolved'} ${r.unit}`, r.source, r.ruleId || ''].forEach(v => row.append(text('td', v))); dims.append(row); }); panel.append(dims);
    response.program.operations.forEach(o => { const item = text('div', '', 'plannedFeature'); item.append(text('h3', `${o.semanticId} / ${human(o.kind)}`)); const drawing = profileDrawing(o); if (drawing) item.append(drawing); item.append(tree(o, 'Feature / placement', false)); panel.append(item); });
    panel.append(tree(view.data.capabilities, 'Qualified capability projection', false));
  } else if (view.kind === 'batch') {
    const table = document.createElement('table'); table.append(text('caption', 'Exact resolved matches'));
    view.data.matches.forEach(m => { const row = document.createElement('tr'); [m.displayName, m.target, `${m.oldValue} → ${m.newValue} ${m.unit}`, m.support].forEach(v => row.append(text('td', v))); table.append(row); }); panel.append(table);
    panel.append(tree({ executionOrder: view.data.orderedEdits, affectedTargets: view.data.affectedTargets, affectedDependencies: view.data.affectedDependencies, fullNativeValidation: view.data.fullNativeValidationRequired }, 'Validation scope', true));
  } else panel.append(tree(view.data, 'Scalar preflight', true));
  panel.append(tree({ previewId: view.id, sha256: view.sha256 }, 'Approval identity', false));
}
function modernControls() {
  const busy = pending || snapshot?.busy || !snapshot, sw = snapshot?.solidworks;
  const live = modelView?.origin === 'external' && modelView.owned && modelView.editable && sw?.healthy;
  $('openModel').disabled = busy || !sw?.connected || sw?.partOpen;
  $('modernConnect').disabled = busy || sw?.connected;
  $('modernClose').disabled = busy || (!sw?.partOpen && !modelView);
  $('constructionPlan').disabled = busy || !snapshot?.llm?.configured || modelView?.origin === 'external' && modelView?.owned || !!sw?.partOpen && !modelView;
  $('constructionPreflight').disabled = busy || modernView?.kind !== 'construction';
  $('modernExecute').disabled = busy || !modernView?.executable || !sw?.connected;
  $('batchPreview').disabled = busy || !live || !queryChanges.length;
  $('scalarPreview').disabled = busy || !live || !selectedFeature?.parameters.some(p => p.key === $('queryParameter').value && p.editable);
}
function modernRefresh(s) {
  const key = pretty([s.model, s.modernPreview]);
  if (key !== modernKey) { if (pretty(s.model) !== pretty(modelView)) renderModel(s.model); renderModern(s.modernPreview); modernKey = key; }
  modernControls();
}
function requirementRow() {
  const row = text('div', '', 'inputRows requirement');
  const key = document.createElement('input'); key.placeholder = 'semantic key'; key.setAttribute('aria-label', 'Requirement key');
  const value = document.createElement('input'); value.type = 'number'; value.step = 'any'; value.placeholder = 'Value'; value.setAttribute('aria-label', 'Given value');
  const unit = document.createElement('select'); ['millimeter','degree','count'].forEach(v => { const o = text('option', v); o.value = v; unit.append(o); }); unit.setAttribute('aria-label', 'Unit');
  const remove = removeTool('Remove requirement'); remove.onclick = () => row.remove(); row.append(key, value, unit, remove); $('requirements').append(row);
}
function ruleRow() {
  const row = text('div', '', 'inputRows rule'); const key = document.createElement('input'); key.placeholder = 'derived key'; key.setAttribute('aria-label', 'Rule output key');
  const rule = document.createElement('select'); ['layout_zero','layout_zero_angle','half_dimension','copy_dimension','sum_dimensions','difference_dimensions','negative_dimension','full_revolution','cosmetic_convex_radius','bracket_cosmetic_corner','capsule_end_radius'].forEach(v => { const o = text('option', v); o.value = v; rule.append(o); }); rule.setAttribute('aria-label', 'Disclosed rule');
  const inputs = document.createElement('input'); inputs.placeholder = 'input keys, comma-separated'; inputs.setAttribute('aria-label', 'Rule input keys');
  const remove = removeTool('Remove rule'); remove.onclick = () => row.remove(); row.append(key, rule, inputs, remove); $('rules').append(row);
}
function modernConfirm(kind, body, message) {
  if (pending || snapshot?.busy) return;
  confirmation = { modern: true, route: kind === 'intake' ? '/api/model/open' : '/api/modern/execute', body };
  $('confirmTitle').textContent = kind === 'intake' ? 'Confirm model intake' : 'Confirm exact preview';
  $('confirmDescription').textContent = message; $('confirmExecute').textContent = kind === 'intake' ? 'Inspect / Open' : 'Execute once'; $('confirmation').showModal();
}
$('workflow').onchange = () => document.body.classList.toggle('legacyMode', $('workflow').value === 'legacy');
$('intakeMode').onchange = () => $('packageRoot').disabled = !$('intakeMode').value.endsWith('restore');
$('openModel').onclick = () => modernConfirm('intake', { mode: $('intakeMode').value, path: $('partPath').value, root: $('packageRoot').value || null, configuration: $('configuration').value || null, confirmed: true }, `${$('intakeMode').value}\nSource / selected file: ${$('partPath').value}\nPackage: ${$('packageRoot').value || 'new isolated copy'}\nOriginal is not modified.`);
$('modernConnect').onclick = () => action('/api/solidworks/connect'); $('modernClose').onclick = () => action('/api/part/close');
$('addRequirement').onclick = requirementRow; $('addRule').onclick = ruleRow;
$('constructionPlan').onclick = () => {
  const requirements = [...document.querySelectorAll('.requirement')].map(r => ({ semanticKey: r.children[0].value, value: r.children[1].value === '' ? null : Number(r.children[1].value), unit: r.children[2].value, source: r.children[1].value === '' ? 'unresolved' : 'given', ruleId: null, ruleVersion: null, confidence: 1, ambiguous: false, critical: true }));
  const rules = [...document.querySelectorAll('.rule')].map(r => ({ semanticKey: r.children[0].value, ruleId: r.children[1].value, inputKey: null, inputs: r.children[2].value.split(',').map(v => v.trim()).filter(Boolean) }));
  const request = { input: { schemaVersion: '0.3', recordId: 'ui_' + Date.now(), revision: modelView?.owned ? modelView.revision : 0, intent: $('constructionIntent').value, requirements }, rules };
  if ($('draftVersion').value) request.semanticDraftVersion = Number($('draftVersion').value); action('/api/construction/plan', request);
};
$('constructionPreflight').onclick = () => action('/api/construction/preflight', { planId: modernView.id, revision: modernView.revision });
function queryClause() {
  const parameter = $('queryParameter').value, unit = parameter === 'pattern_count' ? 'count' : 'millimeter';
  return { query: { subtype: $('querySubtype').value || null, nativeType: null, parameter, unit, dimension: $('queryOld').value === '' ? null : { minimum: null, maximum: null, values: [Number($('queryOld').value)] }, host: $('queryHost').value || null, relation: $('queryRelation').value ? { kind: $('queryRelation').value, target: $('queryRelationTarget').value } : null, capability: null, cardinality: $('queryCardinality').value }, value: Number($('queryNew').value) };
}
function renderClauses() { $('queryClauses').replaceChildren(); queryChanges.forEach((c, i) => { const row = text('div', '', 'clause'); row.append(text('span', `${human(c.query.parameter)} / ${c.query.dimension?.values[0] ?? 'any'} → ${c.value} / ${c.query.cardinality}`)); const b = removeTool('Remove clause'); b.onclick = () => { queryChanges.splice(i, 1); renderClauses(); modernControls(); }; row.append(b); $('queryClauses').append(row); }); }
$('addQuery').onclick = () => { if (queryChanges.length < 16) { queryChanges.push(queryClause()); renderClauses(); modernControls(); } };
$('batchPreview').onclick = () => action('/api/batch/preview', { revision: modelView.revision, changes: queryChanges });
$('queryParameter').onchange = modernControls;
$('scalarPreview').onclick = () => action('/api/scalar/preview', { revision: modelView.revision, target: selectedFeature.id, parameter: $('queryParameter').value, unit: $('queryParameter').value === 'pattern_count' ? 'count' : 'millimeter', expectedOldValue: Number($('queryOld').value), value: Number($('queryNew').value) });
$('modernExecute').onclick = () => { const targets = modernTargets(); modernConfirm('execute', { id: modernView.id, sha256: modernView.sha256, revision: modernView.revision, targets, confirmed: true }, `${modernView.kind} / Revision ${modernView.revision}\nSHA256: ${modernView.sha256}\nExact execution order:\n${targets.join('\n') || 'new / appended construction'}\nSource: ${modelView?.sourcePath || 'managed creation'}\nWorking copy: ${modelView?.workingPath || 'new owned package'}`); };
