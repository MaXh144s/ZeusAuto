// ATALHOS PAGE
// ============================================================
function renderAtalhos() {
  const c = document.getElementById('atalhos-list');
  c.innerHTML = '';
  Object.entries(state.atalhos).forEach(([id, def]) => {
    const isRecording = state.recordingAtalho === id;
    const tempKeys = state.atalhoKeys[id] || [...def.keys];
    function renderKeyList(keys) {
      if (!keys || keys.length === 0) return '<span style="color:var(--text-disabled);font-size:11px">— sem atalho —</span>';
      return keys.map(k => `<span class="key-tag">${k}</span>`).join('<span style="color:var(--text-muted);font-size:10px;margin:0 2px">+</span>');
    }
    const keysDisplay = isRecording
      ? (tempKeys.length > 0 ? renderKeyList(tempKeys) : '...')
      : renderKeyList(def.keys);

    const row = document.createElement('div');
    row.className = 'shortcut-row-config';
    row.innerHTML = `
      <div class="toggle-switch ${def.enabled?'on':''}" style="cursor:pointer;flex-shrink:0;margin-top:2px" onclick="toggleAtalhoEnabled('${id}')"></div>
      <div class="shortcut-row-left">
        <div class="shortcut-row-name">${def.label} <span class="info-icon">i</span></div>
        <div class="shortcut-row-desc">${def.desc}</div>
      </div>
      <div class="shortcut-row-right">
        <div class="key-chip ${isRecording?'recording':''}" id="atalho-chip-${id}" onclick="toggleAtalhoRecord('${id}')" style="flex-direction:row;flex-wrap:wrap">
          ${keysDisplay}
        </div>
        ${isRecording ? `<button class="btn btn-primary btn-sm" onclick="saveAtalhoRecord('${id}')">Salvar</button>` : ''}
        <div class="atalho-conflict-msg" id="atalho-err-${id}" style="display:none"></div>
      </div>`;
    c.appendChild(row);
  });
}

function toggleAtalhoEnabled(id) {
  state.atalhos[id].enabled = !state.atalhos[id].enabled;
  renderAtalhos();
}

function toggleAtalhoRecord(id) {
  if (state.recordingAtalho === id) {
    // stop without saving
    state.recordingAtalho = null;
    delete state.atalhoKeys[id];
    document.removeEventListener('keydown', handleAtalhoKey);
    renderAtalhos();
    return;
  }
  if (state.recordingAtalho) {
    state.recordingAtalho = null;
    document.removeEventListener('keydown', handleAtalhoKey);
  }
  state.recordingAtalho = id;
  state.atalhoKeys[id] = [];
  document.addEventListener('keydown', handleAtalhoKey);
  renderAtalhos();
}

function handleAtalhoKey(e) {
  if (!state.recordingAtalho) return;
  e.preventDefault();
  if (e.key === 'Escape') { toggleAtalhoRecord(state.recordingAtalho); return; }
  // normalizeKey está definido em macros.js e usa physicalKeyName —
  // retorna o VK físico da tecla, alinhado com o hook C#.
  const parts = normalizeKey(e);
  if (!parts) return;
  const id = state.recordingAtalho;
  if (!state.atalhoKeys[id]) state.atalhoKeys[id] = [];
  parts.forEach(k => {
    if (!state.atalhoKeys[id].includes(k)) {
      state.atalhoKeys[id].push(k);
    }
  });
  renderAtalhos();
}

function saveAtalhoRecord(id) {
  const keys = state.atalhoKeys[id] || [];
  if (keys.length === 0) { showToast('Pressione pelo menos uma tecla.', 'error'); return; }

  // Check conflict
  const combo = JSON.stringify(keys.slice().sort());
  const conflict = Object.entries(state.atalhos).find(([k,v]) => k !== id && JSON.stringify([...v.keys].sort()) === combo);
  if (conflict) {
    showToast(`Conflito com "${conflict[1].label}"`, 'error');
    return;
  }

  state.atalhos[id].keys = [...keys];
  delete state.atalhoKeys[id];
  state.recordingAtalho = null;
  document.removeEventListener('keydown', handleAtalhoKey);
  showToast('Atalho salvo!', 'success');
  renderAtalhos();
}

// ============================================================
