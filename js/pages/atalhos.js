// ATALHOS PAGE
// ============================================================

function renderAtalhos() {
  const c = document.getElementById('atalhos-list');
  c.innerHTML = '';
  Object.entries(state.atalhos).forEach(([id, def]) => {
    const isRecording = state.recordingAtalho === id;
    const tempKeys = state.atalhoKeys[id] || null;

    function renderKeyList(keys) {
      if (!keys || keys.length === 0) return '<span style="color:var(--text-disabled);font-size:11px">— sem atalho —</span>';
      return keys.map(k => `<span class="key-tag">${k}</span>`).join('<span style="color:var(--text-muted);font-size:10px;margin:0 2px">+</span>');
    }

    // Durante gravação mostra as teclas temporárias (podem ser vazias enquanto aguarda input).
    // Fora da gravação mostra as teclas salvas.
    const keysDisplay = isRecording
      ? (tempKeys !== null && tempKeys.length > 0 ? renderKeyList(tempKeys) : '<span style="color:var(--accent);font-size:11px">pressione a combinação...</span>')
      : renderKeyList(def.keys);

    // Botão de ação direta: aciona a função do atalho sem precisar de tecla física.
    // Atalhos togláveis (pausar, cpsOverlay, bipToggle) alternam entre ▶ e ⏸
    // para indicar o estado atual. 'encerrar' é sempre ▶ (ação única, sem estado).
    const TOGGLE_STATE = {
      pausar:     () => state.isPaused,
      cpsOverlay: () => state.isOverlayOn,
      bipToggle:  () => state.isBipOn,
    };
    const isToggle = id in TOGGLE_STATE;
    const actionIcon = isToggle && TOGGLE_STATE[id]() ? '⏸' : '▶';
    const actionBtn = `<button
      class="btn btn-sm"
      id="atalho-action-btn-${id}"
      style="padding:2px 8px;font-size:11px;opacity:0.8"
      title="Executar agora"
      onclick="triggerAtalhoAction('${id}')">${actionIcon}</button>`;

    const row = document.createElement('div');
    row.className = 'shortcut-row-config';
    row.innerHTML = `
      <div class="toggle-switch ${def.enabled?'on':''}" style="cursor:pointer;flex-shrink:0;margin-top:2px" onclick="toggleAtalhoEnabled('${id}')"></div>
      <div class="shortcut-row-left">
        <div class="shortcut-row-name">
          ${def.label}
          ${actionBtn}
          <span class="info-icon">i</span>
        </div>
        <div class="shortcut-row-desc">${def.desc}</div>
      </div>
      <div class="shortcut-row-right">
        <div class="key-chip ${isRecording?'recording':''}" id="atalho-chip-${id}" onclick="toggleAtalhoRecord('${id}')" style="flex-direction:row;flex-wrap:wrap">
          ${keysDisplay}
        </div>
        ${isRecording ? `
          <button class="btn btn-primary btn-sm" onclick="saveAtalhoRecord('${id}')">Salvar</button>
          <button class="btn btn-sm" style="margin-left:4px" onclick="toggleAtalhoRecord('${id}')">Cancelar</button>
        ` : ''}
        <div class="atalho-conflict-msg" id="atalho-err-${id}" style="display:none"></div>
      </div>`;
    c.appendChild(row);
  });
}

function toggleAtalhoEnabled(id) {
  state.atalhos[id].enabled = !state.atalhos[id].enabled;
  renderAtalhos();
  ZeusNativeBridge.sync();
}

function toggleAtalhoRecord(id) {
  if (state.recordingAtalho === id) {
    // Cancela sem salvar
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
  // null = ainda não pressionou nada (exibe "pressione a combinação...")
  // [] seria zero teclas capturadas mas já iniciou — usamos null para distinguir
  delete state.atalhoKeys[id];
  document.addEventListener('keydown', handleAtalhoKey);
  renderAtalhos();
}

function handleAtalhoKey(e) {
  if (!state.recordingAtalho) return;
  e.preventDefault();
  if (e.key === 'Escape') { toggleAtalhoRecord(state.recordingAtalho); return; }

  // CORREÇÃO DO BUG DE GRAVAÇÃO:
  // normalizeKey(e) já retorna o estado completo da combinação no instante
  // deste evento: modificadores ativos (ctrlKey/shiftKey/altKey) + tecla física.
  // Ex: Ctrl+Shift+F7 pressionados juntos → ['Ctrl','Shift','F7']
  //
  // A versão anterior acumulava teclas de múltiplos keydowns, o que permitia
  // gravar combos impossíveis (ex: Ctrl solto antes de F7 → ['Ctrl','F7'] mas
  // nunca pressionados juntos). A correção é SUBSTITUIR a lista a cada keydown,
  // não acumular — cada evento já carrega o snapshot completo das teclas ativas.
  const parts = normalizeKey(e);
  if (!parts) return; // modificador sozinho — aguarda tecla principal

  const id = state.recordingAtalho;
  // Substitui (não acumula): o último keydown com tecla principal define o combo.
  state.atalhoKeys[id] = parts;
  renderAtalhos();
}

function saveAtalhoRecord(id) {
  const keys = state.atalhoKeys[id] || [];
  if (keys.length === 0) { showToast('Pressione pelo menos uma tecla.', 'error'); return; }

  // Verifica conflito com outros atalhos
  const combo = JSON.stringify(keys.slice().sort());
  const conflict = Object.entries(state.atalhos).find(([k, v]) => k !== id && JSON.stringify([...v.keys].sort()) === combo);
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
  ZeusNativeBridge.sync();
}

// Aciona a função do atalho diretamente via native-bridge,
// sem precisar pressionar a tecla física.
// Encerrar exige confirmação no lado C# (MessageBox) — o JS não precisa confirmar.
function triggerAtalhoAction(id) {
  if (!ZeusNativeBridge.isAvailable()) {
    showToast('Não conectado ao app nativo.', 'error');
    return;
  }
  // Atualiza estado local para refletir o toggle visualmente antes da resposta C#
  if (id === 'pausar')     { state.isPaused    = !state.isPaused;    renderAtalhos(); }
  if (id === 'cpsOverlay') { state.isOverlayOn = !state.isOverlayOn; renderAtalhos(); }
  if (id === 'bipToggle')  { state.isBipOn     = !state.isBipOn;     renderAtalhos(); }
  ZeusNativeBridge.triggerAction(id);
}

// ============================================================
