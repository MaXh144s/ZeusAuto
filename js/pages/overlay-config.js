// OVERLAY CONFIG
// ============================================================
// Lógica da página de customização da janela overlay de CPS.
// Gerencia: elementos, cores, fontes, regras responsivas,
// save/load de perfis e preview ao vivo.

// ── Estado local da página ──────────────────────────────────
const overlayState = {
  // Configuração ativa (reflete o que está na tela)
  config: null,

  // Perfis salvos disponíveis (carregados via native-bridge)
  savedProfiles: [],

  // Sim paused toggle
  previewPaused: false,
};

// ── Perfil padrão (hardcoded original) ─────────────────────
function getDefaultOverlayProfile() {
  return {
    profileName: 'Padrão',
    createdAt: new Date().toISOString(),
    background: { color: '#12141C', opacity: 0.92 },
    border: { color: '#466EFF', glowEnabled: true, glowIntensity: 20, glowColor: '#466EFF' },
    elements: [
      {
        id: 'cpsReal',
        label: 'CPS Real',
        visible: true,
        fontSize: 20.0,
        colorActive: '#3CE18C',
        colorIdle: '#78788C',
        colorPaused: '#FFA032',
        responsiveRules: []
      },
      {
        id: 'cpsCfg',
        label: 'CPS Configurado',
        visible: true,
        fontSize: 9.5,
        colorActive: '#78788C',
        colorIdle: '#78788C',
        colorPaused: '#FFA032',
        responsiveRules: []
      },
      {
        id: 'buttonName',
        label: 'Nome do Botão',
        visible: true,
        fontSize: 8.5,
        colorActive: '#F0F2FF',
        colorIdle: '#F0F2FF',
        colorPaused: '#FFA032',
        responsiveRules: []
      },
      {
        id: 'statusDot',
        label: 'Dot de Status',
        visible: true,
        fontSize: 8.0,
        colorActive: '#3CE18C',
        colorIdle: '#78788C',
        colorPaused: '#FFA032',
        responsiveRules: []
      },
      {
        id: 'doubleClick',
        label: 'Double Click (ms)',
        visible: true,
        fontSize: 9.5,
        colorActive: '#78788C',
        colorIdle: '#78788C',
        colorPaused: '#FFA032',
        responsiveRules: []
      },
      {
        id: 'pausedText',
        label: '"PAUSADO"',
        visible: true,
        fontSize: 20.0,
        colorActive: '#FFA032',
        colorIdle: '#FFA032',
        colorPaused: '#FFA032',
        responsiveRules: []
      }
    ]
  };
}

// ── Inicialização ───────────────────────────────────────────
function initOverlayConfig() {
  overlayState.config = JSON.parse(JSON.stringify(getDefaultOverlayProfile()));
  renderOverlayPage();
  renderOverlayPreview();
  loadSavedOverlayProfiles();
}

// ── Render principal da página ──────────────────────────────
function renderOverlayPage() {
  const cfg = overlayState.config;

  // Background
  document.getElementById('ov-bg-color').value = cfg.background.color;
  document.getElementById('ov-bg-color-hex').value = cfg.background.color;
  document.getElementById('ov-bg-opacity').value = Math.round(cfg.background.opacity * 100);
  document.getElementById('ov-bg-opacity-val').textContent = Math.round(cfg.background.opacity * 100) + '%';

  // Border
  document.getElementById('ov-border-color').value = cfg.border.color;
  document.getElementById('ov-border-color-hex').value = cfg.border.color;

  // Glow
  const glowEnabled = cfg.border.glowEnabled;
  document.getElementById('sw-ov-glow').classList.toggle('on', glowEnabled);
  document.getElementById('sw-ov-glow').classList.toggle('off', !glowEnabled);
  document.getElementById('ov-glow-panel').style.display = glowEnabled ? '' : 'none';
  document.getElementById('ov-glow-intensity').value = cfg.border.glowIntensity;
  document.getElementById('ov-glow-intensity-val').textContent = cfg.border.glowIntensity;
  document.getElementById('ov-glow-color').value = cfg.border.glowColor;
  document.getElementById('ov-glow-color-hex').value = cfg.border.glowColor;

  // Elementos
  renderOverlayElements();
}

function renderOverlayElements() {
  const container = document.getElementById('ov-elements-list');
  if (!container) return;
  container.innerHTML = '';
  overlayState.config.elements.forEach((el, idx) => {
    container.appendChild(buildElementCard(el, idx));
  });
}

function buildElementCard(el, idx) {
  const card = document.createElement('div');
  card.className = 'ov-element-card' + (el.visible ? '' : ' ov-element-hidden');
  card.id = 'ov-el-card-' + el.id;
  card.innerHTML = `
    <div class="ov-element-header">
      <div class="ov-element-title">
        <div class="toggle-switch ${el.visible ? 'on' : 'off'}" id="sw-el-${el.id}"
             onclick="toggleElementVisible('${el.id}')"></div>
        <span class="ov-element-label">${el.label}</span>
      </div>
      <button class="btn btn-ghost btn-xs ov-rules-btn" onclick="toggleElementRules('${el.id}')">
        <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/>
        </svg>
        Regras
      </button>
    </div>

    <div class="ov-element-body" id="ov-el-body-${el.id}" style="${el.visible ? '' : 'display:none'}">
      <div class="ov-row">
        <label class="ov-label">Tamanho da fonte</label>
        <div class="ov-font-row">
          <input type="number" class="num-input" style="width:65px" min="5" max="48"
                 value="${el.fontSize}" oninput="updateElementFont('${el.id}', this.value)">
          <div class="num-input-unit">pt</div>
        </div>
      </div>

      <div class="ov-colors-row">
        <div class="ov-color-group">
          <label class="ov-label-sm">Ativo</label>
          <div class="ov-color-pair">
            <input type="color" class="ov-color-picker" value="${el.colorActive}"
                   oninput="updateElementColor('${el.id}','active',this.value); document.getElementById('ov-hex-${el.id}-active').value=this.value">
            <input type="text" class="ov-hex-input" id="ov-hex-${el.id}-active" value="${el.colorActive}" maxlength="7"
                   oninput="syncHexToColor(this,'ov-el-color-active-${el.id}','${el.id}','active')">
          </div>
        </div>
        <div class="ov-color-group">
          <label class="ov-label-sm">Idle</label>
          <div class="ov-color-pair">
            <input type="color" class="ov-color-picker" id="ov-el-color-idle-${el.id}" value="${el.colorIdle}"
                   oninput="updateElementColor('${el.id}','idle',this.value); document.getElementById('ov-hex-${el.id}-idle').value=this.value">
            <input type="text" class="ov-hex-input" id="ov-hex-${el.id}-idle" value="${el.colorIdle}" maxlength="7"
                   oninput="syncHexToColor(this,'ov-el-color-idle-${el.id}','${el.id}','idle')">
          </div>
        </div>
        <div class="ov-color-group">
          <label class="ov-label-sm">Pausado</label>
          <div class="ov-color-pair">
            <input type="color" class="ov-color-picker" id="ov-el-color-paused-${el.id}" value="${el.colorPaused}"
                   oninput="updateElementColor('${el.id}','paused',this.value); document.getElementById('ov-hex-${el.id}-paused').value=this.value">
            <input type="text" class="ov-hex-input" id="ov-hex-${el.id}-paused" value="${el.colorPaused}" maxlength="7"
                   oninput="syncHexToColor(this,'ov-el-color-paused-${el.id}','${el.id}','paused')">
          </div>
        </div>
      </div>

      <!-- Regras responsivas -->
      <div class="ov-rules-panel" id="ov-rules-${el.id}" style="display:none">
        <div class="ov-rules-title">
          Regras Responsivas
          <button class="btn btn-ghost btn-xs" onclick="addResponsiveRule('${el.id}')">+ Adicionar</button>
        </div>
        <div id="ov-rules-list-${el.id}">
          ${renderRulesHTML(el)}
        </div>
      </div>
    </div>
  `;
  return card;
}

function renderRulesHTML(el) {
  if (!el.responsiveRules || el.responsiveRules.length === 0)
    return '<div class="ov-rules-empty">Nenhuma regra definida.</div>';
  return el.responsiveRules.map((rule, rIdx) => buildRuleHTML(el.id, rule, rIdx)).join('');
}

function buildRuleHTML(elId, rule, rIdx) {
  return `
    <div class="ov-rule-card" id="ov-rule-${elId}-${rIdx}">
      <div class="ov-rule-header">
        <span class="ov-rule-label">Regra ${rIdx + 1}</span>
        <button class="btn btn-ghost btn-xs" style="color:var(--danger)"
                onclick="removeResponsiveRule('${elId}',${rIdx})">✕ Remover</button>
      </div>
      <div class="ov-rule-grid">
        <div>
          <label class="ov-label-sm">Largura mín (px)</label>
          <input type="number" class="num-input" style="width:70px" min="0" value="${rule.widthMin}"
                 oninput="updateRule('${elId}',${rIdx},'widthMin',this.value)">
        </div>
        <div>
          <label class="ov-label-sm">Largura máx (px)</label>
          <input type="number" class="num-input" style="width:70px" min="0" value="${rule.widthMax}"
                 oninput="updateRule('${elId}',${rIdx},'widthMax',this.value)">
        </div>
        <div>
          <label class="ov-label-sm">Altura mín (px)</label>
          <input type="number" class="num-input" style="width:70px" min="0" value="${rule.heightMin}"
                 oninput="updateRule('${elId}',${rIdx},'heightMin',this.value)">
        </div>
        <div>
          <label class="ov-label-sm">Altura máx (px)</label>
          <input type="number" class="num-input" style="width:70px" min="0" value="${rule.heightMax}"
                 oninput="updateRule('${elId}',${rIdx},'heightMax',this.value)">
        </div>
      </div>
      <div class="ov-rule-action-row">
        <label class="ov-label-sm">Ação</label>
        <select class="form-select" style="width:auto" onchange="updateRule('${elId}',${rIdx},'action',this.value); toggleNewPosition('${elId}',${rIdx},this.value)">
          <option value="scaleFont" ${rule.action === 'scaleFont' ? 'selected' : ''}>Diminuir fonte proporcionalmente</option>
          <option value="relocate" ${rule.action === 'relocate' ? 'selected' : ''}>Diminuir fonte e realocar</option>
          <option value="hide" ${rule.action === 'hide' ? 'selected' : ''}>Ocultar elemento</option>
        </select>
        <div class="ov-new-pos" id="ov-newpos-${elId}-${rIdx}" style="${rule.action === 'relocate' ? '' : 'display:none'}">
          <label class="ov-label-sm">Nova posição</label>
          <select class="form-select" style="width:auto" onchange="updateRule('${elId}',${rIdx},'newPosition',this.value)">
            <option value="top"    ${rule.newPosition === 'top'    ? 'selected' : ''}>Topo</option>
            <option value="center" ${rule.newPosition === 'center' ? 'selected' : ''}>Centro</option>
            <option value="bottom" ${rule.newPosition === 'bottom' ? 'selected' : ''}>Rodapé</option>
          </select>
        </div>
      </div>
    </div>
  `;
}

// ── Handlers de elementos ───────────────────────────────────
function toggleElementVisible(id) {
  const el = overlayState.config.elements.find(e => e.id === id);
  if (!el) return;
  el.visible = !el.visible;
  const sw = document.getElementById('sw-el-' + id);
  if (sw) { sw.classList.toggle('on', el.visible); sw.classList.toggle('off', !el.visible); }
  const card = document.getElementById('ov-el-card-' + id);
  if (card) card.classList.toggle('ov-element-hidden', !el.visible);
  const body = document.getElementById('ov-el-body-' + id);
  if (body) body.style.display = el.visible ? '' : 'none';
  renderOverlayPreview();
}

function updateElementFont(id, val) {
  const el = overlayState.config.elements.find(e => e.id === id);
  if (!el) return;
  el.fontSize = parseFloat(val) || el.fontSize;
  renderOverlayPreview();
}

function updateElementColor(id, state, val) {
  const el = overlayState.config.elements.find(e => e.id === id);
  if (!el) return;
  if (state === 'active') el.colorActive = val;
  else if (state === 'idle') el.colorIdle = val;
  else if (state === 'paused') el.colorPaused = val;
  renderOverlayPreview();
}

function syncHexToColor(hexInput, colorId, elId, colorState) {
  const val = hexInput.value;
  if (/^#[0-9A-Fa-f]{6}$/.test(val)) {
    const picker = document.getElementById(colorId);
    if (picker) picker.value = val;
    updateElementColor(elId, colorState, val);
  }
}

function toggleElementRules(id) {
  const panel = document.getElementById('ov-rules-' + id);
  if (!panel) return;
  panel.style.display = panel.style.display === 'none' ? '' : 'none';
}

function addResponsiveRule(id) {
  const el = overlayState.config.elements.find(e => e.id === id);
  if (!el) return;
  el.responsiveRules.push({ widthMin: 0, widthMax: 140, heightMin: 0, heightMax: 60, action: 'scaleFont', newPosition: null });
  const listEl = document.getElementById('ov-rules-list-' + id);
  if (listEl) listEl.innerHTML = renderRulesHTML(el);
}

function removeResponsiveRule(id, rIdx) {
  const el = overlayState.config.elements.find(e => e.id === id);
  if (!el) return;
  el.responsiveRules.splice(rIdx, 1);
  const listEl = document.getElementById('ov-rules-list-' + id);
  if (listEl) listEl.innerHTML = renderRulesHTML(el);
}

function updateRule(elId, rIdx, field, val) {
  const el = overlayState.config.elements.find(e => e.id === elId);
  if (!el || !el.responsiveRules[rIdx]) return;
  el.responsiveRules[rIdx][field] = (field === 'action' || field === 'newPosition') ? val : (parseInt(val) || 0);
}

function toggleNewPosition(elId, rIdx, action) {
  const div = document.getElementById(`ov-newpos-${elId}-${rIdx}`);
  if (div) div.style.display = action === 'relocate' ? '' : 'none';
}

// ── Handlers de aparência geral ─────────────────────────────
function updateOvBgColor(val) {
  overlayState.config.background.color = val;
  document.getElementById('ov-bg-color-hex').value = val;
  renderOverlayPreview();
}
function updateOvBgColorHex(val) {
  if (/^#[0-9A-Fa-f]{6}$/.test(val)) {
    overlayState.config.background.color = val;
    document.getElementById('ov-bg-color').value = val;
    renderOverlayPreview();
  }
}
function updateOvBgOpacity(val) {
  overlayState.config.background.opacity = parseInt(val) / 100;
  document.getElementById('ov-bg-opacity-val').textContent = val + '%';
  renderOverlayPreview();
}

function updateOvBorderColor(val) {
  overlayState.config.border.color = val;
  document.getElementById('ov-border-color-hex').value = val;
  renderOverlayPreview();
}
function updateOvBorderColorHex(val) {
  if (/^#[0-9A-Fa-f]{6}$/.test(val)) {
    overlayState.config.border.color = val;
    document.getElementById('ov-border-color').value = val;
    renderOverlayPreview();
  }
}

function toggleOvGlow() {
  overlayState.config.border.glowEnabled = !overlayState.config.border.glowEnabled;
  const on = overlayState.config.border.glowEnabled;
  document.getElementById('sw-ov-glow').classList.toggle('on', on);
  document.getElementById('sw-ov-glow').classList.toggle('off', !on);
  document.getElementById('ov-glow-panel').style.display = on ? '' : 'none';
  renderOverlayPreview();
}

function updateOvGlowIntensity(val) {
  overlayState.config.border.glowIntensity = parseInt(val);
  document.getElementById('ov-glow-intensity-val').textContent = val;
  renderOverlayPreview();
}

function updateOvGlowColor(val) {
  overlayState.config.border.glowColor = val;
  document.getElementById('ov-glow-color-hex').value = val;
  renderOverlayPreview();
}
function updateOvGlowColorHex(val) {
  if (/^#[0-9A-Fa-f]{6}$/.test(val)) {
    overlayState.config.border.glowColor = val;
    document.getElementById('ov-glow-color').value = val;
    renderOverlayPreview();
  }
}

// ── Perfis ──────────────────────────────────────────────────
function saveOverlayProfile() {
  const nameInput = document.getElementById('ov-save-name');
  const name = nameInput ? nameInput.value.trim() : '';
  if (!name) { showToast('Digite um nome para o perfil.', 'error'); return; }

  const profile = JSON.parse(JSON.stringify(overlayState.config));
  profile.profileName = name;
  profile.createdAt = new Date().toISOString();

  if (typeof ZeusNativeBridge !== 'undefined' && ZeusNativeBridge.isAvailable()) {
    ZeusNativeBridge.saveOverlayProfile(profile);
    showToast(`Perfil "${name}" salvo com sucesso.`, 'success');
  } else {
    // Fallback: simula localmente para preview
    overlayState.savedProfiles.push(profile);
    showToast(`Perfil "${name}" salvo (modo preview).`, 'success');
    renderSavedProfilesList();
  }
  if (nameInput) nameInput.value = '';
}

function loadSavedOverlayProfiles() {
  if (typeof ZeusNativeBridge !== 'undefined' && ZeusNativeBridge.isAvailable()) {
    ZeusNativeBridge.listOverlayProfiles();
  } else {
    renderSavedProfilesList();
  }
}

// Chamado pelo C# com a lista de perfis disponíveis
window.ZeusOverlayProfiles = function(profiles) {
  overlayState.savedProfiles = profiles || [];
  renderSavedProfilesList();
};

function renderSavedProfilesList() {
  const btn = document.getElementById('ov-import-btn');
  const list = document.getElementById('ov-saved-profiles-list');
  if (!btn || !list) return;

  const hasProfiles = overlayState.savedProfiles.length > 0;
  btn.disabled = !hasProfiles;
  btn.classList.toggle('btn-disabled', !hasProfiles);

  list.innerHTML = '';
  if (!hasProfiles) {
    list.innerHTML = '<div style="font-size:11px;color:var(--text-muted);padding:8px 0">Nenhum perfil salvo.</div>';
    return;
  }
  overlayState.savedProfiles.forEach((p, i) => {
    const item = document.createElement('div');
    item.className = 'ov-profile-item';
    item.innerHTML = `
      <span class="ov-profile-name">${p.profileName}</span>
      <button class="btn btn-ghost btn-xs" onclick="loadOverlayProfile(${i})">Carregar</button>
    `;
    list.appendChild(item);
  });
}

function loadOverlayProfile(idx) {
  const p = overlayState.savedProfiles[idx];
  if (!p) return;
  overlayState.config = JSON.parse(JSON.stringify(p));
  renderOverlayPage();
  renderOverlayPreview();
  showToast(`Perfil "${p.profileName}" carregado.`, 'success');
}

function restoreDefaultOverlay() {
  overlayState.config = JSON.parse(JSON.stringify(getDefaultOverlayProfile()));
  renderOverlayPage();
  renderOverlayPreview();
  showToast('Configurações restauradas para o padrão.', 'info');
}

// ── Preview ao vivo ─────────────────────────────────────────
function renderOverlayPreview() {
  const canvas = document.getElementById('ov-preview-canvas');
  if (!canvas) return;

  const cfg = overlayState.config;
  const paused = overlayState.previewPaused;
  const W = canvas.offsetWidth || 320;
  const H = canvas.offsetHeight || 120;
  canvas.width = W;
  canvas.height = H;

  const ctx = canvas.getContext('2d');
  ctx.clearRect(0, 0, W, H);

  // Fundo com opacidade
  const bgAlpha = cfg.background.opacity;
  const bgHex = cfg.background.color;
  ctx.globalAlpha = bgAlpha;
  ctx.fillStyle = bgHex;
  const r = 12;
  roundedRect(ctx, 1, 1, W - 2, H - 2, r);
  ctx.fill();
  ctx.globalAlpha = 1;

  // Glow
  if (cfg.border.glowEnabled) {
    const intensity = cfg.border.glowIntensity;
    ctx.shadowColor = cfg.border.glowColor;
    ctx.shadowBlur = intensity;
    ctx.strokeStyle = 'transparent';
    roundedRect(ctx, 2, 2, W - 4, H - 4, r);
    ctx.stroke();
    ctx.shadowBlur = 0;
  }

  // Borda
  ctx.strokeStyle = cfg.border.color;
  ctx.lineWidth = 1.5;
  roundedRect(ctx, 2, 2, W - 4, H - 4, r);
  ctx.stroke();

  // Slots de dados (simula 2 slots: 1 ativo, 1 idle)
  const slots = [
    { name: 'Esquerdo', active: true,  realCps: 13.5, cfgCps: 13.0, ms: 200 },
    { name: 'Direito',  active: false, realCps: 0.0,  cfgCps: 10.0, ms: 150 },
  ];

  const count = slots.length;
  const slotW = W / count;
  const scale = H / 110;
  const pad = Math.max(8, 14 * scale);
  const mid = H / 2;

  // Fontes dos elementos
  const elMap = {};
  cfg.elements.forEach(e => { elMap[e.id] = e; });

  for (let i = 0; i < count; i++) {
    const slot = slots[i];
    const x = i * slotW + pad;
    const w = slotW - pad * 2;

    // Separador
    if (i > 0) {
      ctx.strokeStyle = hexToRgba(cfg.border.color, 0.15);
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(i * slotW, 12 * scale);
      ctx.lineTo(i * slotW, H - 12 * scale);
      ctx.stroke();
    }

    const stateKey = paused ? 'paused' : (slot.active ? 'active' : 'idle');

    // Dot de status
    const dotEl = elMap['statusDot'];
    if (dotEl && dotEl.visible) {
      const dotSize = Math.max(5, 8 * scale);
      ctx.fillStyle = paused ? dotEl.colorPaused : (slot.active ? dotEl.colorActive : dotEl.colorIdle);
      ctx.beginPath();
      ctx.arc(x + dotSize / 2, mid - 26 * scale + dotSize / 2, dotSize / 2, 0, Math.PI * 2);
      ctx.fill();
    }

    // Nome do botão
    const nameEl = elMap['buttonName'];
    if (nameEl && nameEl.visible) {
      ctx.fillStyle = paused ? nameEl.colorPaused : (slot.active ? nameEl.colorActive : nameEl.colorIdle);
      ctx.font = `bold ${nameEl.fontSize * scale}pt Segoe UI`;
      ctx.textBaseline = 'middle';
      ctx.fillText(slot.name, x + 12 * scale, mid - 28 * scale);
    }

    // CPS Real ou PAUSADO
    if (paused) {
      const pausedEl = elMap['pausedText'];
      if (pausedEl && pausedEl.visible) {
        ctx.fillStyle = pausedEl.colorPaused;
        ctx.font = `bold ${pausedEl.fontSize * scale}pt Segoe UI`;
        ctx.textBaseline = 'middle';
        ctx.fillText('PAUSADO', x, mid - 14 * scale);
      }
    } else {
      const cpsEl = elMap['cpsReal'];
      if (cpsEl && cpsEl.visible) {
        const cpsVal = slot.active ? slot.realCps.toFixed(1) : '0.0';
        ctx.fillStyle = slot.active ? cpsEl.colorActive : cpsEl.colorIdle;
        ctx.font = `bold ${cpsEl.fontSize * scale}pt Segoe UI`;
        ctx.textBaseline = 'middle';
        ctx.fillText(cpsVal + ' CPS', x, mid - 14 * scale);
      }
    }

    // Rodapé: ms | cfg
    const dcEl = elMap['doubleClick'];
    const cfgEl = elMap['cpsCfg'];
    if ((dcEl && dcEl.visible) || (cfgEl && cfgEl.visible)) {
      ctx.textBaseline = 'middle';
      const footerY = mid + 20 * scale;
      if (dcEl && dcEl.visible) {
        ctx.fillStyle = paused ? dcEl.colorPaused : (slot.active ? dcEl.colorActive : dcEl.colorIdle);
        ctx.font = `${dcEl.fontSize * scale}pt Segoe UI`;
        ctx.fillText(slot.ms + ' ms', x, footerY);
      }
      if (cfgEl && cfgEl.visible) {
        ctx.fillStyle = paused ? cfgEl.colorPaused : (slot.active ? cfgEl.colorActive : cfgEl.colorIdle);
        ctx.font = `${cfgEl.fontSize * scale}pt Segoe UI`;
        ctx.textAlign = 'right';
        ctx.fillText(slot.cfgCps.toFixed(1) + ' CPS', x + w, footerY);
        ctx.textAlign = 'left';
      }
    }
  }

  // Gripper visual
  ctx.strokeStyle = hexToRgba(cfg.border.color, 0.35);
  ctx.lineWidth = 1;
  ctx.beginPath(); ctx.moveTo(W - 16, H - 6); ctx.lineTo(W - 6, H - 16); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(W - 22, H - 6); ctx.lineTo(W - 6, H - 22); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(W - 28, H - 6); ctx.lineTo(W - 6, H - 28); ctx.stroke();
}

function togglePreviewPaused() {
  overlayState.previewPaused = !overlayState.previewPaused;
  const btn = document.getElementById('ov-toggle-pause-btn');
  if (btn) btn.textContent = overlayState.previewPaused ? '▶ Simular Ativo' : '⏸ Simular Pausado';
  renderOverlayPreview();
}

// ── Helpers canvas ──────────────────────────────────────────
function roundedRect(ctx, x, y, w, h, r) {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.lineTo(x + w - r, y);
  ctx.quadraticCurveTo(x + w, y, x + w, y + r);
  ctx.lineTo(x + w, y + h - r);
  ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
  ctx.lineTo(x + r, y + h);
  ctx.quadraticCurveTo(x, y + h, x, y + h - r);
  ctx.lineTo(x, y + r);
  ctx.quadraticCurveTo(x, y, x + r, y);
  ctx.closePath();
}

function hexToRgba(hex, alpha) {
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  return `rgba(${r},${g},${b},${alpha})`;
}

// Inicializar quando a página overlay for exibida
// (Chamado de navigation.js via switchPage)
