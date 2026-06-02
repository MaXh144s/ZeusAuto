// MODAL / TOAST
// ============================================================
function closeModal(id) { document.getElementById(id).classList.remove('open'); }
function showToast(msg, type='info') {
  const c = document.getElementById('toast-container');
  const t = document.createElement('div');
  const icons = {success:'✓',error:'✕',info:'·'};
  t.className = 'toast '+type;
  t.innerHTML = `<span style="font-weight:700;color:${type==='success'?'var(--accent-2)':type==='error'?'var(--danger)':'var(--accent)'}">${icons[type]}</span> ${msg}`;
  c.appendChild(t); setTimeout(()=>t.remove(),3100);
}

// ============================================================
