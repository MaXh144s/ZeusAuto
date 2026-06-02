// PROFILES
// ============================================================
function renderProfiles() {
  const list = document.getElementById('profiles-list');
  const empty = document.getElementById('profiles-empty');
  const keys = Object.keys(state.macros);
  if (keys.length === 0) { list.style.display = 'none'; empty.style.display = 'block'; return; }
  list.style.display = 'grid'; empty.style.display = 'none';
  list.innerHTML = '';
  keys.forEach(key => {
    const cfg = state.macros[key];
    const cps = displayCps(cfg);
    const card = document.createElement('div');
    card.className = 'profile-card';
    card.innerHTML = `
      <div class="profile-card-name">${key}</div>
      <div class="profile-tags">
        <span class="profile-tag">${cps} cps</span>
        <span class="profile-tag">${cfg.interval}ms</span>
        ${cfg.humanize?`<span class="profile-tag">range ${cfg.cpsMin}–${cfg.cpsMax} cps</span>`:'<span class="profile-tag" style="color:var(--text-disabled)">fixo</span>'}
        ${cfg.shortcuts?`<span class="profile-tag">atalhos +/-</span>`:'<span class="profile-tag" style="color:var(--text-disabled)">sem atalhos</span>'}
        ${cfg.bip?`<span class="profile-tag">bip ${cfg.bipHz}hz</span>`:'<span class="profile-tag" style="color:var(--text-disabled)">sem bip</span>'}
      </div>
      <div class="profile-actions">
        <button class="btn btn-ghost btn-sm" onclick="switchPage('macro', document.querySelector('[data-page=macro]')); openConfigureForKey('${key}')">Reconfigurar</button>
        <button class="btn btn-danger btn-sm" onclick="deleteProfileKey('${key}')">
          <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/></svg>
          Excluir
        </button>
      </div>`;
    list.appendChild(card);
  });
}

function deleteProfileKey(key) { state.editingKey = key; confirmDeleteKey(); }

// ============================================================
// SLIDER UTILS
// ============================================================
function updateSliderVal(sliderId, valId, unit) {
  const slider = document.getElementById(sliderId);
  document.getElementById(valId).textContent = slider.value + unit;
  updateSliderPosition(sliderId, valId);
}
function updateSliderPosition(sliderId, valId) {
  const slider = document.getElementById(sliderId);
  const val = document.getElementById(valId);
  if (!slider || !val) return;
  const pct = (slider.value - slider.min) / (slider.max - slider.min);
  val.style.left = `calc(${pct*100}% - ${pct*36}px + 8px)`;
}

// ============================================================
