// INIT
// ============================================================
(function init() {
  initCursorGlow();
  renderGeralGrid();
  renderAtalhos();
  document.getElementById('mouse-panel').style.display = 'none';
  document.getElementById('delete-modal').addEventListener('click', function(e){ if(e.target===this) closeModal('delete-modal'); });
})();
