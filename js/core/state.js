// STATE
// ============================================================
const AVAILABLE_KEYS = ['Tecla Esquerda','Tecla Direita','Tecla Scroll','Tecla xbutton5'];

const state = {
  macros: {},
  atalhos: {
    pausar:     { label:'Pausar e despausar Macros',    desc:'Pausa/retoma todos os macros ativos',     enabled:true, keys:['Ctrl','Shift','\\'] },
    cpsOverlay: { label:'Ligar / Desligar Janela CPS',  desc:'Mostra ou esconde o overlay de CPS',      enabled:true, keys:['Shift','I'] },
    bipToggle:  { label:'Ligar / Desligar Bip sonoro',  desc:'Ativa ou desativa o bip de feedback',     enabled:true, keys:['Ctrl','Shift','I'] },
    encerrar:   { label:'Encerrar Programa',            desc:'Fecha o programa completamente',           enabled:true, keys:[] },
  },
  settings: { cpsOverlay:true, showCpsChange:true, alwaysVisible:true, animate:true },
  editingKey: null,
  options: { humanize:false, shortcuts:false, bip:false },
  // Shortcut recording state
  macroShortcuts: { 'cps-plus':[], 'cps-minus':[] },
  recordingMacroShortcut: null,
  // Atalho recording per id
  recordingAtalho: null,
  atalhoKeys: {},      // temp keys being recorded
};

// ============================================================
