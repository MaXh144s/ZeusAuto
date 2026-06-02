// NAVIGATION
// ============================================================
function switchPage(pageId, navEl) {
  document.querySelectorAll('.page').forEach(p => p.classList.remove('active'));
  document.querySelectorAll('.nav-item,.settings-btn').forEach(n => n.classList.remove('active'));
  document.getElementById('page-' + pageId).classList.add('active');
  if (navEl) navEl.classList.add('active');

  const panel = document.getElementById('mouse-panel');
  // Show mouse panel only on macro, perfis, atalhos
  panel.style.display = (pageId === 'geral' || pageId === 'settings') ? 'none' : 'flex';

  if (pageId === 'macro') { renderMacroList(); renderMouseLegend(); }
  if (pageId === 'perfis') { renderProfiles(); renderMouseLegend(); }
  if (pageId === 'atalhos') { renderAtalhos(); renderMouseLegend(); }
  if (pageId === 'geral') renderGeralGrid();
}

// ============================================================
