// DISPLAY CPS HELPER
// ============================================================
function displayCps(cfg) {
  if (!cfg) return null;
  if (cfg.humanize) return ((cfg.cpsMin + cfg.cpsMax) / 2).toFixed(1);
  return cfg.cpsBase;
}

// ============================================================
// MOUSE LEGEND (right panel)
// ============================================================
function renderMouseLegend() {
  const c = document.getElementById('mouse-legend');
  c.innerHTML = '';
  AVAILABLE_KEYS.forEach(key => {
    const cfg = state.macros[key];
    const el = document.createElement('div');
    el.className = 'legend-item' + (cfg ? ' configured' : '');
    el.dataset.key = key;
    const cps = cfg ? displayCps(cfg) : null;
    el.innerHTML = `
      <div class="legend-dot"></div>
      <div class="legend-name">${key}</div>
      ${cfg ? `<div class="legend-badge">${cps}cps·${cfg.interval}ms</div>` : '<div class="legend-badge" style="color:var(--text-disabled)">—</div>'}`;
    el.onclick = () => openConfigureForKey(key);
    c.appendChild(el);
  });
}

// ============================================================
// GERAL GRID
// ============================================================
function renderGeralGrid() {
  const grid = document.getElementById('geral-grid');
  grid.innerHTML = '';
  AVAILABLE_KEYS.forEach(key => {
    const cfg = state.macros[key];
    const el = document.createElement('div');
    el.className = 'geral-card' + (cfg ? ' has-cfg' : '');
    const cps = cfg ? displayCps(cfg) : null;
    el.innerHTML = `
      <div class="geral-card-title">${key}</div>
      <div class="geral-card-value">${cfg ? cps+'cps · '+cfg.interval+'ms' : 'Sem configuração'}</div>
      <div class="geral-card-action">${cfg ? 'reconfigurar →' : 'configurar →'}</div>`;
    el.onclick = () => { switchPage('macro', document.querySelector('[data-page=macro]')); openConfigureForKey(key); };
    grid.appendChild(el);
  });
}

// ============================================================
// MACRO LIST
// ============================================================
function renderMacroList() {
  const c = document.getElementById('macro-keys-list');
  c.innerHTML = '';
  const keys = Object.keys(state.macros);
  if (keys.length === 0) {
    c.innerHTML = `<div class="macro-empty-state"><div class="empty-icon">⊕</div><p>Nenhuma tecla configurada.<br>Adicione uma configuração abaixo.</p></div>`;
  } else {
    keys.forEach(key => {
      const cfg = state.macros[key];
      const cps = displayCps(cfg);
      const tags = [cps+' cps', cfg.interval+'ms'];
      if (cfg.humanize) tags.push(cfg.cpsMin+'-'+cfg.cpsMax+' cps range');
      if (cfg.bip) tags.push('bip '+cfg.bipHz+'hz');
      const card = document.createElement('div');
      card.className = 'macro-key-card';
      card.innerHTML = `<div class="macro-key-header">
        <div class="macro-key-icon"><svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="var(--accent)" stroke-width="2"><path d="M5 3l14 9-14 9V3z"/></svg></div>
        <div class="macro-key-info">
          <div class="macro-key-name">${key}</div>
          <div class="macro-key-sub">${tags.join(' · ')}</div>
        </div>
        <div class="macro-key-actions"><button class="btn btn-ghost btn-sm" onclick="openConfigureForKey('${key}')">Reconfigurar</button></div>
      </div>`;
      c.appendChild(card);
    });
  }
  const addBtn = document.getElementById('add-key-btn');
  const available = AVAILABLE_KEYS.filter(k => !state.macros[k]);
  addBtn.style.display = available.length === 0 ? 'none' : 'flex';
}

// ============================================================
// KEY SELECTOR
// ============================================================
function openKeySelector() {
  document.getElementById('macro-list-view').style.display = 'none';
  document.getElementById('key-selector-panel').style.display = 'block';
  document.getElementById('macro-configure-view').style.display = 'none';
  document.getElementById('macro-breadcrumb').style.display = 'none';

  const sel = document.getElementById('new-key-select');
  sel.innerHTML = '<option value="">— selecione a tecla —</option>';
  AVAILABLE_KEYS.forEach(k => {
    const opt = document.createElement('option');
    opt.value = k;
    opt.textContent = k + (state.macros[k] ? ' (já configurada)' : '');
    opt.disabled = !!state.macros[k];
    sel.appendChild(opt);
  });
  document.getElementById('btn-confirm-add').disabled = true;
}

function onNewKeySelected() {
  const val = document.getElementById('new-key-select').value;
  document.getElementById('btn-confirm-add').disabled = !val;
}

function confirmAddKey() {
  const key = document.getElementById('new-key-select').value;
  if (!key) return;
  cancelKeySelector();
  openConfigureForKey(key, true);
}

function cancelKeySelector() {
  document.getElementById('key-selector-panel').style.display = 'none';
  document.getElementById('macro-list-view').style.display = 'block';
  renderMacroList();
}

// ============================================================
// CONFIGURE PANEL
// ============================================================
function openConfigureForKey(key, isNew = false) {
  state.editingKey = key;
  state.macroShortcuts = { 'cps-plus':[], 'cps-minus':[] };
  state.recordingMacroShortcut = null;

  document.getElementById('macro-list-view').style.display = 'none';
  document.getElementById('key-selector-panel').style.display = 'none';
  document.getElementById('macro-configure-view').style.display = 'block';
  document.getElementById('macro-breadcrumb').style.display = 'block';

  document.getElementById('cfg-key-name').textContent = key;

  const cfg = state.macros[key] || { interval:200, cpsBase:13, humanize:false, cpsMin:10, cpsMax:16, shortcuts:false, cpsPlus:[], cpsMinus:[], bip:false, bipHz:200 };

  // Interval
  document.getElementById('cfg-interval').value = cfg.interval;
  document.getElementById('cfg-interval-num').value = cfg.interval;
  document.getElementById('cfg-interval-val').textContent = cfg.interval + 'ms';
  updateSliderPosition('cfg-interval', 'cfg-interval-val');

  // CPS Base da tecla
  document.getElementById('cfg-cps-base').value = cfg.cpsBase !== undefined ? cfg.cpsBase : 13;

  // Humanize
  state.options.humanize = cfg.humanize;
  setToggle('humanize', cfg.humanize);
  document.getElementById('humanize-panel').style.display = cfg.humanize ? 'block' : 'none';
  setCpsBaseLock(cfg.humanize);
  document.getElementById('cfg-cps-min').value = cfg.cpsMin || 10;
  document.getElementById('cfg-cps-max').value = cfg.cpsMax || 16;

  // Shortcuts
  state.options.shortcuts = cfg.shortcuts;
  state.macroShortcuts['cps-plus'] = cfg.cpsPlus ? [...cfg.cpsPlus] : [];
  state.macroShortcuts['cps-minus'] = cfg.cpsMinus ? [...cfg.cpsMinus] : [];
  setToggle('shortcuts', cfg.shortcuts);
  document.getElementById('shortcuts-panel').style.display = cfg.shortcuts ? 'block' : 'none';
  renderMacroShortcutDisplay('cps-plus');
  renderMacroShortcutDisplay('cps-minus');

  // Bip
  state.options.bip = cfg.bip;
  setToggle('bip', cfg.bip);
  document.getElementById('bip-hz-panel').style.display = cfg.bip ? 'block' : 'none';
  const hzEl = document.getElementById('cfg-hz');
  hzEl.value = cfg.bipHz || 200;
  document.getElementById('cfg-hz-val').textContent = (cfg.bipHz||200) + 'hz';
  updateSliderPosition('cfg-hz', 'cfg-hz-val');

  // Delete btn
  document.getElementById('delete-key-btn').style.display = state.macros[key] ? 'inline-flex' : 'none';

  // Highlight legend
  document.querySelectorAll('.legend-item').forEach(el => el.classList.toggle('active-zone', el.dataset.key === key));

  // Key listener for shortcuts
  document.removeEventListener('keydown', handleMacroShortcutKey);
  document.addEventListener('keydown', handleMacroShortcutKey);
}

function closeConfigurePanel() {
  state.recordingMacroShortcut = null;
  document.removeEventListener('keydown', handleMacroShortcutKey);
  document.getElementById('macro-configure-view').style.display = 'none';
  document.getElementById('macro-breadcrumb').style.display = 'none';
  document.getElementById('macro-list-view').style.display = 'block';
  state.editingKey = null;
  renderMacroList();
  renderMouseLegend();
  document.querySelectorAll('.legend-item').forEach(el => el.classList.remove('active-zone'));
}

function setToggle(id, val) {
  state.options[id] = val;
  document.getElementById('switch-' + id).className = 'toggle-switch' + (val ? ' on' : '');
  const row = document.getElementById('toggle-' + id);
  if (row) row.className = 'toggle-row' + (val ? ' checked' : '');
}

function setCpsBaseLock(locked) {
  const input = document.getElementById('cfg-cps-base');
  const wrap  = input.closest('.form-group');
  input.disabled = locked;
  input.style.opacity = locked ? '0.35' : '1';
  input.style.cursor  = locked ? 'not-allowed' : '';
  // hint text
  let hint = wrap.querySelector('.num-input-hint');
  if (hint) hint.textContent = locked
    ? 'Controlado pelo range de humanização'
    : 'Velocidade fixa usada quando humanização está desativada';
}

function toggleOption(opt) {
  state.options[opt] = !state.options[opt];
  setToggle(opt, state.options[opt]);
  if (opt === 'shortcuts') document.getElementById('shortcuts-panel').style.display = state.options.shortcuts ? 'block' : 'none';
  if (opt === 'humanize') {
    document.getElementById('humanize-panel').style.display = state.options.humanize ? 'block' : 'none';
    setCpsBaseLock(state.options.humanize);
  }
  if (opt === 'bip') document.getElementById('bip-hz-panel').style.display = state.options.bip ? 'block' : 'none';
}

// ============================================================
// MACRO SHORTCUT RECORDING (accumulates keys until save)
// ============================================================
function startRecordMacroShortcut(which) {
  if (state.recordingMacroShortcut === which) {
    // stop recording this one
    state.recordingMacroShortcut = null;
    document.getElementById('sd-' + which).classList.remove('recording');
    document.getElementById('sh-' + which).textContent = 'teclas acumulam até salvar';
    return;
  }
  // stop any other
  if (state.recordingMacroShortcut) {
    const prev = state.recordingMacroShortcut;
    document.getElementById('sd-' + prev).classList.remove('recording');
    document.getElementById('sh-' + prev).textContent = 'teclas acumulam até salvar';
  }
  state.recordingMacroShortcut = which;
  state.macroShortcuts[which] = [];
  renderMacroShortcutDisplay(which);
  document.getElementById('sd-' + which).classList.add('recording');
  document.getElementById('sh-' + which).textContent = 'pressione as teclas... (clique novamente para parar)';
}

function normalizeKey(e) {
  // Modificadores sozinhos não formam combo
  if (['Control','Shift','Alt','Meta'].includes(e.key)) return null;
  const parts = [];
  if (e.ctrlKey)  parts.push('Ctrl');
  if (e.shiftKey) parts.push('Shift');
  if (e.altKey)   parts.push('Alt');
  if (e.metaKey)  parts.push('Meta');
  let main = e.key;
  if (main === ' ') main = 'Space';
  else if (main.length === 1) main = main.toUpperCase();
  parts.push(main);
  return parts; // ex: ['Ctrl','Shift','A']
}

function handleMacroShortcutKey(e) {
  if (!state.recordingMacroShortcut) return;
  e.preventDefault();
  if (e.key === 'Escape') { startRecordMacroShortcut(state.recordingMacroShortcut); return; }
  const parts = normalizeKey(e);
  if (!parts) return;
  const which = state.recordingMacroShortcut;
  // Cada keydown adiciona as teclas ao combo atual (acumula modificadores + teclas)
  parts.forEach(k => {
    if (!state.macroShortcuts[which].includes(k)) {
      state.macroShortcuts[which].push(k);
    }
  });
  renderMacroShortcutDisplay(which);
}

function renderMacroShortcutDisplay(which) {
  const sd = document.getElementById('sd-' + which);
  const keys = state.macroShortcuts[which];
  if (keys.length === 0) {
    sd.innerHTML = `<span class="shortcut-placeholder" id="sp-${which}">clique para gravar</span>`;
  } else {
    const rendered = keys.map(k => `<span class="key-tag">${k}</span>`).join('<span style="color:var(--text-muted);font-size:10px;margin:0 2px">+</span>');
    sd.innerHTML = rendered + `<span id="sp-${which}"></span>`;
  }
}

// ============================================================
// CPS VALIDATION
// ============================================================
function validateCpsBaseField(el) {
  let v = parseInt(el.value);
  if (isNaN(v)) return;
  if (v < 1) { el.value = 1; }
  if (v > 40) { el.value = 40; }
}

function validateCpsRange() {
  const minEl = document.getElementById('cfg-cps-min');
  const maxEl = document.getElementById('cfg-cps-max');
  const errEl = document.getElementById('cps-range-err');
  let mn = parseInt(minEl.value), mx = parseInt(maxEl.value);
  mn = Math.max(1, Math.min(40, mn || 1));
  mx = Math.max(1, Math.min(40, mx || 40));
  if (mn >= mx) {
    errEl.style.display = 'block';
    errEl.textContent = 'CPS Mínimo deve ser menor que CPS Máximo';
    minEl.classList.add('has-error');
    maxEl.classList.add('has-error');
    return false;
  }
  errEl.style.display = 'none';
  minEl.classList.remove('has-error');
  maxEl.classList.remove('has-error');
  return true;
}

// ============================================================
// INTERVAL SYNC
// ============================================================
function syncIntervalNum() {
  let v = parseInt(document.getElementById('cfg-interval-num').value);
  if (isNaN(v)) return;
  v = Math.max(50, Math.min(1000, v));
  document.getElementById('cfg-interval').value = v;
  document.getElementById('cfg-interval-val').textContent = v + 'ms';
  updateSliderPosition('cfg-interval', 'cfg-interval-val');
}
function syncIntervalSlider() {
  const v = document.getElementById('cfg-interval').value;
  document.getElementById('cfg-interval-num').value = v;
  document.getElementById('cfg-interval-val').textContent = v + 'ms';
  updateSliderPosition('cfg-interval', 'cfg-interval-val');
}

// ============================================================
// SAVE / DELETE
// ============================================================
function saveMacroKey() {
  if (!state.editingKey) return;
  if (state.options.humanize && !validateCpsRange()) { showToast('Corrija o range de CPS antes de salvar.', 'error'); return; }
  if (state.options.shortcuts) {
    const pp = state.macroShortcuts['cps-plus'];
    const pm = state.macroShortcuts['cps-minus'];
    if (pp.length === 0 || pm.length === 0) { showToast('Defina os atalhos CPS+ e CPS- antes de salvar.', 'error'); return; }
    if (JSON.stringify(pp) === JSON.stringify(pm)) { showToast('Atalhos CPS+ e CPS- não podem ser iguais.', 'error'); return; }
  }

  state.macros[state.editingKey] = {
    interval: parseInt(document.getElementById('cfg-interval').value),
    cpsBase: parseInt(document.getElementById('cfg-cps-base').value) || 13,
    humanize: state.options.humanize,
    cpsMin: parseInt(document.getElementById('cfg-cps-min').value),
    cpsMax: parseInt(document.getElementById('cfg-cps-max').value),
    shortcuts: state.options.shortcuts,
    cpsPlus: [...state.macroShortcuts['cps-plus']],
    cpsMinus: [...state.macroShortcuts['cps-minus']],
    bip: state.options.bip,
    bipHz: parseInt(document.getElementById('cfg-hz').value),
  };

  state.recordingMacroShortcut = null;
  document.removeEventListener('keydown', handleMacroShortcutKey);
  showToast('Configuração salva!', 'success');
  closeConfigurePanel();
  renderGeralGrid();
}

function confirmDeleteKey() {
  if (!state.editingKey || !state.macros[state.editingKey]) return;
  document.getElementById('delete-key-label').textContent = state.editingKey;
  document.getElementById('delete-modal').classList.add('open');
}

function executeDeleteKey() {
  delete state.macros[state.editingKey];
  closeModal('delete-modal');
  showToast('Macro excluído.', 'info');
  state.recordingMacroShortcut = null;
  document.removeEventListener('keydown', handleMacroShortcutKey);
  closeConfigurePanel();
  renderGeralGrid();
  renderProfiles();
  renderMouseLegend();
}

// ============================================================
