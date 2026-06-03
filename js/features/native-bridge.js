// NATIVE BRIDGE
// ============================================================
const ZeusNativeBridge = {
  activeMacro: null,

  isAvailable() {
    return Boolean(window.chrome?.webview);
  },

  buildProfile() {
    const macroKeys = Object.keys(state.macros);
    if (!this.activeMacro || !state.macros[this.activeMacro]) {
      this.activeMacro = macroKeys[0] || null;
    }

    return {
      profileName: 'Interface',
      enabled: macroKeys.length > 0,
      activeMacro: this.activeMacro,
      macros: state.macros,
      atalhos: state.atalhos,
      settings: state.settings
    };
  },

  sync() {
    if (!this.isAvailable()) return;

    window.chrome.webview.postMessage({
      type: 'profile:update',
      profile: this.buildProfile()
    });
  },

  setActiveMacro(key) {
    this.activeMacro = key;
    this.sync();
  },

  // BUG FIX: comando dedicado para o atalho cpsOverlay alternar o overlay
  // sem precisar reconstruir toda a engine (evita reiniciar macros ativos)
  toggleOverlay() {
    if (!this.isAvailable()) return;
    window.chrome.webview.postMessage({ type: 'overlay:toggle' });
  }
};

window.ZeusNativeBridgeStatus = function(message, isError) {
  if (typeof showToast === 'function') {
    showToast(message, isError ? 'error' : 'success');
  }
};

(function installNativeBridgeHooks() {
  const originalSaveMacroKey = window.saveMacroKey;
  window.saveMacroKey = function() {
    const key = state.editingKey;
    originalSaveMacroKey();
    if (key && state.macros[key]) ZeusNativeBridge.setActiveMacro(key);
  };

  const originalExecuteDeleteKey = window.executeDeleteKey;
  window.executeDeleteKey = function() {
    originalExecuteDeleteKey();
    ZeusNativeBridge.sync();
  };

  const originalOpenConfigureForKey = window.openConfigureForKey;
  window.openConfigureForKey = function(key, isNew) {
    originalOpenConfigureForKey(key, isNew);
    // Só sincroniza se o macro JÁ existe (não ao abrir config de novo macro)
    if (!isNew && state.macros[key]) ZeusNativeBridge.setActiveMacro(key);
  };

  const originalHandleImportProfile = window.handleImportProfile;
  window.handleImportProfile = function(event) {
    originalHandleImportProfile(event);
    setTimeout(() => ZeusNativeBridge.sync(), 50);
  };

  const originalHandleImportAtalho = window.handleImportAtalho;
  window.handleImportAtalho = function(event) {
    originalHandleImportAtalho(event);
    setTimeout(() => ZeusNativeBridge.sync(), 50);
  };

  window.addEventListener('DOMContentLoaded', () => {
    // Só sincroniza na inicialização se já houver macros salvos
    // (evita enviar enabled:false e desabilitar a engine prematuramente)
    setTimeout(() => {
      if (Object.keys(state.macros).length > 0) {
        ZeusNativeBridge.sync();
      }
    }, 100);
  });
})();
