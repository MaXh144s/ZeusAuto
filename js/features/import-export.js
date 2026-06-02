// IMPORT / EXPORT JSON
// ============================================================
function exportProfile() {
  if (Object.keys(state.macros).length === 0) { showToast('Nenhum macro para exportar.','error'); return; }
  downloadJSON(JSON.stringify({ macros: state.macros }, null, 2), 'zeusauto-perfil.json');
  showToast('Perfil exportado!','success');
}
function importProfile() { document.getElementById('import-profile-input').click(); }
function handleImportProfile(e) {
  const file = e.target.files[0]; if (!file) return;
  const r = new FileReader();
  r.onload = ev => {
    try {
      const d = JSON.parse(ev.target.result);
      if (!d.macros) throw new Error();
      Object.assign(state.macros, d.macros);
      renderMacroList(); renderProfiles(); renderMouseLegend(); renderGeralGrid();
      showToast('Perfil importado!','success');
    } catch { showToast('Arquivo inválido.','error'); }
  };
  r.readAsText(file); e.target.value = '';
}
function exportAtalho() {
  downloadJSON(JSON.stringify({ atalhos: state.atalhos }, null, 2), 'zeusauto-atalhos.json');
  showToast('Atalhos exportados!','success');
}
function importAtalho() { document.getElementById('import-atalho-input').click(); }
function handleImportAtalho(e) {
  const file = e.target.files[0]; if (!file) return;
  const r = new FileReader();
  r.onload = ev => {
    try {
      const d = JSON.parse(ev.target.result);
      if (!d.atalhos) throw new Error();
      Object.assign(state.atalhos, d.atalhos);
      renderAtalhos();
      showToast('Atalhos importados!','success');
    } catch { showToast('Arquivo inválido.','error'); }
  };
  r.readAsText(file); e.target.value = '';
}
function downloadJSON(data, filename) {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([data],{type:'application/json'}));
  a.download = filename; a.click();
}

// ============================================================
