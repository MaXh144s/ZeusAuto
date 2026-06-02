// SETTINGS
// ============================================================
function toggleSetting(id) {
  const km = {'cps-overlay':'cpsOverlay','show-cps-change':'showCpsChange','always-visible':'alwaysVisible','animate':'animate'};
  const k = km[id];
  state.settings[k] = !state.settings[k];
  document.getElementById('sw-'+id).className = 'toggle-switch'+(state.settings[k]?' on':'');
  if (k === 'animate') {
    document.body.classList.toggle('no-animate', !state.settings.animate);
  }
}

// ============================================================
