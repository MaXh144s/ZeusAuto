// CURSOR GLOW
// ============================================================
function initCursorGlow() {
  const glow = document.createElement('div');
  glow.className = 'cursor-glow';
  document.body.appendChild(glow);

  document.addEventListener('pointermove', function(e) {
    const isNavItem = Boolean(e.target.closest('.nav-item'));
    glow.classList.toggle('is-hidden', isNavItem);
    glow.style.transform = `translate3d(${e.clientX}px, ${e.clientY}px, 0)`;
  });

  document.addEventListener('pointerleave', function() {
    glow.classList.add('is-hidden');
  });

  document.addEventListener('pointerenter', function(e) {
    glow.classList.toggle('is-hidden', Boolean(e.target.closest('.nav-item')));
  });
}
