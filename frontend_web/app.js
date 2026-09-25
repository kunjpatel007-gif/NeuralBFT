'use strict';

const WS_URL = new URLSearchParams(location.search).get('ws') || 'wss://neuralbft-backend-443293282760.asia-south1.run.app';

const CONSENSUS = ['PoW', 'PoS', 'DPoS', 'PBFT'];
const FAULTS = [
  ['honest', 'Honest (Heal)'],
  ['offline', 'Offline'],
  ['malicious', 'Malicious'],
  ['stealth', 'Stealth'],
];

const STATUS_COLOR = {
  'Verified':    '#6bb8d4',
  'Trusted':     '#5fb98c',
  'Watched':     '#d4b155',
  'High Risk':   '#d68a52',
  'Quarantined': '#e06464',
  'Blacklisted': '#8f3d47',
};
const DEFAULT_COLOR = STATUS_COLOR['Trusted'];
const STEALTH_ATTACK_REP = 55;
const TAU = Math.PI * 2;
const FONT = '"IBM Plex Sans", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';

const $ = (id) => document.getElementById(id);
const key = (id) => String(id);
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const color = (s) => STATUS_COLOR[s] || DEFAULT_COLOR;
const threatColor = (p) => (p > 0.7 ? '#e06464' : p > 0.4 ? '#d4b155' : '#5fb98c');
function num(v) { const n = typeof v === 'string' ? Number(v) : v; return typeof n === 'number' && Number.isFinite(n) ? n : null; }
function fixed(v, d = 1, suffix = '') { const n = num(v); return n == null ? '—' : n.toFixed(d) + suffix; }
function pct(v, d = 1) { const n = num(v); return n == null ? '—' : (n * 100).toFixed(d) + '%'; }
function setText(el, v) { if (typeof el === 'string') el = $(el); const s = String(v); if (el && el.textContent !== s) el.textContent = s; }
function hash(s) { let h = 2166136261; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); } return h >>> 0; }
function partition(n) { return n.partition_id || 0; }
function groups(nodes) {
  const m = new Map();
  for (const n of nodes) { const k = String(partition(n)); if (!m.has(k)) m.set(k, []); m.get(k).push(n); }
  return [...m.entries()].sort((a, b) => Number(a[0]) - Number(b[0]) || a[0].localeCompare(b[0])).map((e) => e[1]);
}

let state = { nodes: [], messages: [], round: 0, consensus: '-', blocks: [], metrics: {} };
let index = new Map();
let hasState = false;
let isSplit = false;
let splitPendingUntil = 0;
let selectedNodeId = null;

/* ── WebSocket ── */
let ws = null;
let reconnectTimeout = 1000;
let reconnectTimer = null;

const isOpen = () => !!ws && ws.readyState === WebSocket.OPEN;

function connect() {
  clearTimeout(reconnectTimer);
  setLink('connecting');
  let socket;
  try { socket = new WebSocket(WS_URL); } catch (e) { console.error(e); return retry(); }
  ws = socket;
  socket.onopen = () => { if (ws === socket) { reconnectTimeout = 1000; setLink('live'); } };
  socket.onmessage = (event) => { if (ws === socket) receive(event.data); };
  socket.onclose = () => { if (ws === socket) { setLink('offline'); retry(); } };
}

function retry() {
  reconnectTimer = setTimeout(connect, reconnectTimeout);
  reconnectTimeout = Math.min(reconnectTimeout * 2, 10000);
}

function setLink(s) {
  $('link').dataset.state = s;
  setText('status', s === 'live' ? 'Connected' : s === 'offline' ? 'Disconnected' : 'Connecting');
  syncControls();
  renderOverlay();
}

function receive(raw) {
  let data;
  try { data = JSON.parse(raw); } catch (e) { console.error('Failed to parse state:', e); return; }
  if (!data || typeof data !== 'object' || Array.isArray(data)) return;
  // Full snapshots replace state; partial messages merge instead of wiping it
  const partial = !Array.isArray(data.nodes);
  if (partial && !['round', 'consensus', 'blocks', 'metrics', 'messages'].some((k) => k in data)) return;
  const next = partial ? { ...state, ...data } : { ...data };
  next.nodes = Array.isArray(next.nodes) ? next.nodes.filter((n) => n && n.id != null) : [];
  next.messages = Array.isArray(next.messages) ? next.messages : [];
  next.blocks = Array.isArray(next.blocks) ? next.blocks : [];
  next.metrics = next.metrics && typeof next.metrics === 'object' ? next.metrics : {};
  state = next;
  hasState = true;
  try { updateUI(); } catch (e) { console.error('Render error:', e); }
}

function send(payload) {
  if (!isOpen()) return false;
  ws.send(JSON.stringify(payload));
  return true;
}

/* ── Commands ── */
function trainFalsePositive() {
  if (selectedNodeId != null) send({ action: 'report_false_positive', node_id: selectedNodeId });
}
function trainMissedAttack() {
  if (selectedNodeId != null) send({ action: 'report_missed_attack', node_id: selectedNodeId });
}
function switchConsensus(consensus) {
  send({ action: 'switch_consensus', consensus });
}
function injectFault() {
  const nodeId = $('nodeSelect').value;
  const faultType = $('faultSelect').value;
  if (nodeId) send({ action: 'inject_fault', node_id: nodeId, fault_type: faultType });
}
function toggleSplit() {
  const next = !isSplit;
  if (!sendAction(next ? 'split_network' : 'merge_network')) return;
  isSplit = next;
  splitPendingUntil = Date.now() + 2500;
  renderSplit();
}
function sendAction(action) {
  return send({ action });
}

/* ── UI ── */
function updateUI() {
  index = new Map(state.nodes.map((n) => [key(n.id), n]));

  setText('round', (state.round || 0).toLocaleString('en-US'));
  setText('consensus', state.consensus || '-');
  setText('nodeCount', state.nodes.length);
  const tps = num(state.metrics.tps) ?? 0;
  setText('tps', Number.isInteger(tps) ? tps : tps.toFixed(1));

  syncNodeSelect();
  renderConsensus();
  // Follow the server's partitions so the button is right after reloads/reconnects
  if (Date.now() > splitPendingUntil && state.nodes.length) {
    const split = groups(state.nodes).length > 1;
    if (split || state.nodes.some((n) => n.partition_id != null)) isSplit = split;
  }
  renderSplit();
  renderBoard();
  renderTicker();
  renderDetail();
  renderOverlay();
  syncControls();
  topo.dirty = true;
}

let nodeSig = null;
function syncNodeSelect() {
  const sel = $('nodeSelect');
  const ids = state.nodes.map((n) => key(n.id));
  const sig = ids.join('\u0001');
  if (sig === nodeSig) return;
  const prev = sel.value;
  sel.textContent = '';
  for (const id of ids) sel.appendChild(new Option(id, id));
  if (ids.includes(prev)) sel.value = prev;
  nodeSig = sig;
}

function buildStatic() {
  const wrap = $('consensusButtons');
  for (const name of CONSENSUS) {
    const b = document.createElement('button');
    b.type = 'button';
    b.textContent = name;
    b.dataset.value = name;
    b.dataset.cmd = '';
    b.addEventListener('click', () => switchConsensus(name));
    wrap.appendChild(b);
  }
  const fs = $('faultSelect');
  for (const [value, label] of FAULTS) fs.appendChild(new Option(label, value));
}

function renderConsensus() {
  const cur = String(state.consensus || '').toLowerCase();
  for (const b of $('consensusButtons').children) b.classList.toggle('active', b.dataset.value.toLowerCase() === cur);
}

function renderSplit() {
  const btn = $('splitBtn');
  setText(btn, isSplit ? 'Merge Network' : 'Sever Network');
  btn.classList.toggle('active', isSplit);
}

const rows = new Map();
function renderBoard() {
  const wrap = $('lbContent');
  const rep = (n) => num(n.reputation) ?? -Infinity;
  const sorted = [...state.nodes].sort((a, b) => rep(b) - rep(a) || String(a.id).localeCompare(String(b.id), undefined, { numeric: true }));
  const sel = selectedNodeId != null ? key(selectedNodeId) : null;
  const live = new Set();
  sorted.forEach((n, i) => {
    const k = key(n.id);
    live.add(k);
    let row = rows.get(k);
    if (!row) {
      row = document.createElement('div');
      row.className = 'lb-row';
      row.dataset.id = k;
      row.append(document.createElement('i'), document.createElement('span'), document.createElement('b'));
      rows.set(k, row);
    }
    row.style.setProperty('--c', color(n.status));
    row.title = n.status || '';
    setText(row.children[1], n.id);
    setText(row.children[2], num(n.reputation) == null ? '—' : Math.round(n.reputation));
    row.classList.toggle('selected', k === sel);
    if (wrap.children[i] !== row) wrap.insertBefore(row, wrap.children[i] || null);
  });
  for (const [k, row] of rows) if (!live.has(k)) { row.remove(); rows.delete(k); }
}

function renderTicker() {
  const t = $('ticker');
  const blocks = state.blocks.filter((b) => b && typeof b === 'object');
  if (!blocks.length) { setText(t, 'Awaiting blocks'); return; }
  const sig = blocks.map((b) => b.hash).join('|');
  if (t.dataset.sig === sig) return;
  t.dataset.sig = sig;
  t.textContent = '';
  for (const b of [...blocks].reverse()) {
    const s = document.createElement('span');
    s.textContent = `${String(b.hash ?? '—').slice(0, 8)}  ·  ${b.proposer ?? '—'}  ·  ${b.tx_count ?? 0} tx  ·  ${b.consensus ?? ''}`;
    t.appendChild(s);
  }
}

function renderDetail() {
  const panel = $('nodeDetail');
  panel.hidden = selectedNodeId == null;
  if (selectedNodeId == null) return;
  const n = index.get(key(selectedNodeId));
  if (n) showDetail(n);
  else { setText('detailTitle', selectedNodeId); setText('detailStatus', 'Offline'); $('detailStatus').style.color = ''; }
}

function showDetail(n) {
  $('nodeDetail').hidden = false;
  setText('detailTitle', n.id);
  setText('detailRep', fixed(n.reputation, 1));
  setText('detailStatus', n.status || '—');
  $('detailStatus').style.color = color(n.status);
  setText('detailProfile', n.network_profile || 'Unknown');
  setText('detailThreat', pct(n.ml_prob));
  const d = n.ml_detail;
  setText('detailNN', d ? pct(num(d.nn_prob) ?? 0) : '—');
  setText('detailAnomaly', d ? pct(num(d.anomaly_score) ?? 0) : '—');
  const f = n.ml_features;
  setText('detailFreq', f ? fixed(num(f.msg_freq) ?? 0, 1) : '—');
  setText('detailVote', f ? pct(num(f.vote_inconsistency) ?? 0) : '—');
  setText('detailLat', f ? fixed(num(f.latency) ?? 0, 0, ' ms') : '—');
  setText('detailForks', f ? fixed(num(f.fork_attempts) ?? 0, 0) : '—');
  setText('detailSilence', f ? pct(num(f.silence_ratio) ?? 0) : '—');
}

function renderOverlay() {
  const linked = isOpen();
  setText('mapOverlay', !linked ? (hasState ? 'Disconnected, reconnecting…' : `Connecting to ${WS_URL}`) : hasState ? '' : 'Waiting for data');
}

function syncControls() {
  const open = isOpen();
  for (const b of document.querySelectorAll('[data-cmd]')) b.disabled = !open;
  const train = open && selectedNodeId != null && index.has(key(selectedNodeId));
  $('fpBtn').disabled = !train;
  $('maBtn').disabled = !train;
}

function selectNode(id) {
  selectedNodeId = id;
  if (id != null && [...$('nodeSelect').options].some((o) => o.value === key(id))) $('nodeSelect').value = key(id);
  renderBoard();
  renderDetail();
  syncControls();
}

/* ── Topology ── */
const canvas = $('arena');
const ctx = canvas.getContext('2d');
const topo = { w: 0, h: 0, dpr: 1, pos: new Map(), groups: [], split: false, dirty: true };
let hoverKey = null;
let mouse = null;
let lastFrame = 0;

function resize() {
  const r = $('mapPanel').getBoundingClientRect();
  topo.w = Math.max(1, Math.floor(r.width));
  topo.h = Math.max(1, Math.floor(r.height));
  topo.dpr = Math.min(window.devicePixelRatio || 1, 2);
  canvas.width = Math.round(topo.w * topo.dpr);
  canvas.height = Math.round(topo.h * topo.dpr);
  topo.dirty = true;
}

function layout() {
  const parts = groups(state.nodes);
  const P = Math.max(1, parts.length);
  topo.split = parts.length > 1;
  const padX = 32, padTop = topo.split ? 72 : 40, padBottom = 40;
  const cellW = (topo.w - padX * 2) / P;
  const cellH = Math.max(40, topo.h - padTop - padBottom);
  const seen = new Set();
  topo.groups = parts.map((nodes, gi) => {
    const N = nodes.length;
    const cx = padX + cellW * (gi + 0.5), cy = padTop + cellH / 2;
    const maxR = Math.max(20, Math.min(cellW / 2 - 70, cellH / 2 - 40));
    const radius = N <= 1 ? 0 : Math.min(maxR, Math.max(60, N * 20));
    const spacing = N <= 1 ? 999 : (TAU * radius) / N;
    const r = clamp(spacing * 0.16, 4, 10);
    nodes.forEach((n, i) => {
      const k = key(n.id);
      seen.add(k);
      const ang = N <= 1 ? -Math.PI / 2 : (i / N) * TAU - Math.PI / 2;
      const tx = cx + Math.cos(ang) * radius, ty = cy + Math.sin(ang) * radius;
      const p = topo.pos.get(k) || { x: tx, y: ty, r };
      Object.assign(p, { tx, ty, tr: r, ang, spacing });
      topo.pos.set(k, p);
    });
    return { cx, cy, radius, x0: padX + cellW * gi };
  });
  for (const k of topo.pos.keys()) if (!seen.has(k)) topo.pos.delete(k);
  topo.dirty = false;
}

function frame(ts) {
  requestAnimationFrame(frame);
  const dt = lastFrame ? Math.min(0.1, (ts - lastFrame) / 1000) : 0.016;
  lastFrame = ts;
  if (topo.dirty) layout();
  const e = 1 - Math.exp(-dt * 8);
  for (const p of topo.pos.values()) { p.x += (p.tx - p.x) * e; p.y += (p.ty - p.y) * e; p.r += (p.tr - p.r) * e; }
  if (mouse) { hoverKey = hit(mouse.x, mouse.y); canvas.classList.toggle('hovering', hoverKey != null); }

  ctx.setTransform(topo.dpr, 0, 0, topo.dpr, 0, 0);
  ctx.clearRect(0, 0, topo.w, topo.h);
  ctx.globalAlpha = isOpen() || !hasState ? 1 : 0.35;
  drawPartitions();
  drawMessages(Date.now());
  drawNodes();
  ctx.globalAlpha = 1;
}

function drawPartitions() {
  if (!topo.split) return;
  ctx.strokeStyle = '#19191b';
  ctx.lineWidth = 1;
  for (const g of topo.groups.slice(1)) {
    const x = Math.round(g.x0) + 0.5;
    ctx.beginPath(); ctx.moveTo(x, 56); ctx.lineTo(x, topo.h - 24); ctx.stroke();
  }
  ctx.font = `400 12.5px ${FONT}`;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillStyle = '#dd6a6a';
  ctx.fillText('Network severed', topo.w / 2, 28);
}

function drawMessages(now) {
  const sel = selectedNodeId != null ? key(selectedNodeId) : null;
  ctx.lineWidth = 1;
  for (const m of state.messages) {
    if (!m) continue;
    const fk = key(m.from), tk = key(m.to);
    const a = topo.pos.get(fk), b = topo.pos.get(tk);
    if (!a || !b || a === b) continue;
    const hot = sel != null && (fk === sel || tk === sel);
    const alpha = sel == null ? 0.05 : hot ? 0.2 : 0.02;
    ctx.strokeStyle = `rgba(237,237,238,${alpha})`;
    ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.stroke();
    const t = isOpen() ? ((now + (hash(fk + tk) % 800)) % 800) / 800 : 0.5;
    ctx.fillStyle = `rgba(237,237,238,${alpha * 6})`;
    ctx.beginPath(); ctx.arc(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, 1.4, 0, TAU); ctx.fill();
  }
}

function drawNodes() {
  const sel = selectedNodeId != null ? key(selectedNodeId) : null;
  for (const n of state.nodes) {
    const k = key(n.id);
    const p = topo.pos.get(k);
    if (!p) continue;
    const { x, y, r } = p;
    const rep = num(n.reputation) ?? 0;
    const threat = clamp(num(n.ml_prob) ?? 0, 0, 1);

    // ML threat: thin arc
    if (threat > 0.01) {
      ctx.beginPath(); ctx.arc(x, y, r + 4, -Math.PI / 2, -Math.PI / 2 + threat * TAU);
      ctx.strokeStyle = threatColor(threat); ctx.lineWidth = 1.5; ctx.stroke();
    }
    // Injected fault ring (stealth: blue while hiding, red while attacking)
    let tag = null;
    if (n.is_byzantine) {
      let ring = '#e06464';
      if (n.fault_type === 'stealth') {
        const attacking = rep >= STEALTH_ATTACK_REP;
        tag = attacking ? 'attacking' : 'hiding';
        if (!attacking) ring = '#8c93cf';
      }
      ctx.beginPath(); ctx.arc(x, y, r + 8, 0, TAU);
      ctx.strokeStyle = ring; ctx.lineWidth = 1; ctx.stroke();
    }
    ctx.beginPath(); ctx.arc(x, y, r, 0, TAU);
    ctx.fillStyle = color(n.status); ctx.fill();
    if (k === sel) {
      ctx.beginPath(); ctx.arc(x, y, r + (n.is_byzantine ? 12 : 8), 0, TAU);
      ctx.strokeStyle = '#ededee'; ctx.lineWidth = 1; ctx.stroke();
    }

    // Label outward from the ring
    const focus = k === sel || k === hoverKey;
    if (p.spacing < 34 && !focus && !tag) continue;
    const ux = Math.cos(p.ang), uy = Math.sin(p.ang);
    const off = r + (n.is_byzantine ? 14 : 10);
    const ax = x + ux * off, ay = y + uy * off;
    ctx.textAlign = ux > 0.3 ? 'left' : ux < -0.3 ? 'right' : 'center';
    ctx.textBaseline = 'middle';
    const lines = [];
    if (tag) lines.push([tag, n.fault_type === 'stealth' && rep < STEALTH_ATTACK_REP ? '#8c93cf' : '#e06464']);
    if (p.spacing >= 34 || focus) lines.push([String(n.id), focus ? '#ffffff' : '#a8a8ad']);
    if (p.spacing >= 46 || focus) lines.push([String(Math.round(rep)), '#4e4e53']);
    const top = uy < -0.3 ? ay - lines.length * 14 : uy > 0.3 ? ay : ay - (lines.length * 14) / 2;
    ctx.font = `400 11.5px ${FONT}`;
    lines.forEach(([text, c], i) => { ctx.fillStyle = c; ctx.fillText(text, ax, top + i * 14 + 7); });
  }
}

function hit(mx, my) {
  let best = null, bd = Infinity;
  for (const [k, p] of topo.pos) {
    const d = Math.hypot(mx - p.x, my - p.y);
    if (d < Math.max(p.r + 8, 12) && d < bd) { best = k; bd = d; }
  }
  return best;
}

function local(e) { const r = canvas.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; }

/* ── Wiring ── */
canvas.addEventListener('mousemove', (e) => { mouse = local(e); });
canvas.addEventListener('mouseleave', () => { mouse = null; hoverKey = null; canvas.classList.remove('hovering'); });
canvas.addEventListener('click', (e) => {
  const { x, y } = local(e);
  const n = index.get(hit(x, y));
  selectNode(n ? n.id : null);
});
$('lbContent').addEventListener('click', (e) => {
  const row = e.target.closest('.lb-row');
  const n = row && index.get(row.dataset.id);
  if (n) selectNode(n.id);
});
$('detailClose').addEventListener('click', () => selectNode(null));
document.addEventListener('keydown', (e) => { if (e.key === 'Escape') selectNode(null); });
$('injectBtn').addEventListener('click', injectFault);
$('splitBtn').addEventListener('click', toggleSplit);
$('addNodeBtn').addEventListener('click', () => sendAction('add_node'));
$('removeNodeBtn').addEventListener('click', () => sendAction('remove_node'));
$('fpBtn').addEventListener('click', trainFalsePositive);
$('maBtn').addEventListener('click', trainMissedAttack);

new ResizeObserver(resize).observe($('mapPanel'));
window.addEventListener('resize', resize);

buildStatic();
resize();
syncControls();
connect();
requestAnimationFrame(frame);



