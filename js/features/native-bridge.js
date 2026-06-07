// NATIVE BRIDGE
// ============================================================
const ZeusNativeBridge = {
  activeMacro: null,

  // BUG FIX (Causa D): debounce para não enviar profile:update no meio de uma
  // janela de ativação de double-click. O sync é adiado em DEBOUNCE_MS; se
  // outro sync chegar antes do timer disparar, o timer é reiniciado (debounce).
  // Valor deve ser > DoubleClickWindowMs máximo configurável (padrão 200 ms).
  _syncTimer: null,
  DEBOUNCE_MS: 350,

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
      settings: state.settings,
      overlayProfile: state.overlayProfile || null
    };
  },

  sync() {
    if (!this.isAvailable()) return;

    // Cancela timer anterior e agenda novo — debounce protege a janela de ativação
    if (this._syncTimer !== null) {
      clearTimeout(this._syncTimer);
    }
    this._syncTimer = setTimeout(() => {
      this._syncTimer = null;
      window.chrome.webview.postMessage({
        type: 'profile:update',
        profile: this.buildProfile()
      });
    }, this.DEBOUNCE_MS);
  },

  // syncImmediate: para casos onde a urgência supera a proteção (ex: inicialização)
  syncImmediate() {
    if (!this.isAvailable()) return;
    if (this._syncTimer !== null) {
      clearTimeout(this._syncTimer);
      this._syncTimer = null;
    }
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
  },

  // Dispara uma ação de atalho global diretamente da UI, sem precisar da
  // tecla física. Usado pelo botão ▶ ao lado de cada atalho na página Atalhos.
  // Os ids válidos são: 'pausar', 'cpsOverlay', 'bipToggle', 'encerrar'.
  triggerAction(id) {
    if (!this.isAvailable()) return;
    window.chrome.webview.postMessage({ type: 'action:trigger', id });
  },

  // Salva um perfil de overlay no diretório gerenciado pelo C#
  saveOverlayProfile(profile) {
    if (!this.isAvailable()) return;
    window.chrome.webview.postMessage({ type: 'overlay:saveProfile', profile });
  },

  // Solicita a lista de perfis de overlay salvos (C# responde via ZeusOverlayProfiles)
  listOverlayProfiles() {
    if (!this.isAvailable()) return;
    window.chrome.webview.postMessage({ type: 'overlay:listProfiles' });
  },

  // Aplica o perfil de overlay ao estado e sincroniza com C#
  applyOverlayProfile(profile) {
    state.overlayProfile = profile;
    this.sync();
  }
};

window.ZeusNativeBridgeStatus = function(message, isError) {
  if (typeof showToast === 'function') {
    showToast(message, isError ? 'error' : 'success');
  }
};

// Chamado pelo C# (PostToggleState) sempre que um atalho toggle é acionado via
// tecla física. Atualiza o estado local e re-renderiza a página Atalhos para
// que o ícone ▶/⏸ reflita o novo estado sem precisar do botão na interface.
window.ZeusToggleState = function(id, active) {
  if (id === 'pausar')     state.isPaused    = active;
  if (id === 'cpsOverlay') state.isOverlayOn = active;
  if (id === 'bipToggle')  state.isBipOn     = active;
  if (typeof renderAtalhos === 'function') renderAtalhos();
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
    // (evita enviar enabled:false e desabilitar a engine prematuramente).
    // Usa syncImmediate — na inicialização não há janela de ativação em andamento.
    setTimeout(() => {
      if (Object.keys(state.macros).length > 0) {
        ZeusNativeBridge.syncImmediate();
      }
    }, 100);
  });
})();
