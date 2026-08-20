# ZeusAuto

Ferramenta de automação de mouse com interface gráfica web (HTML/CSS/JS) embutida via WebView2, engine nativa em C# (.NET 8) e comunicação bidirecional por mensagens JSON.

---

## Sumário

- [Visão Geral](#visão-geral)
- [Estrutura do Projeto](#estrutura-do-projeto)
- [Arquitetura e Fluxo de Dados](#arquitetura-e-fluxo-de-dados)
- [Interface Web](#interface-web)
- [Engine C#](#engine-c)
- [Protocolo de Comunicação (Bridge)](#protocolo-de-comunicação-bridge)
- [Configuração JSON](#configuração-json)
- [Lógica de CPS e Humanize](#lógica-de-cps-e-humanize)
- [Modo de Ativação DoubleClickHold](#modo-de-ativação-doubleclickhold)
- [Bugs Identificados e Correções](#bugs-identificados-e-correções)
- [Requisitos](#requisitos)

---

## Visão Geral

O ZeusAuto é um auto-clicker configurável com as seguintes características:

- **Interface web** responsável por toda a configuração visual do usuário
- **Engine nativa** em C# que lê a configuração e executa os cliques via `SendInput` (Win32)
- **Comunicação via WebView2**: a interface serializa o estado em JSON e envia para o C# via `postMessage`; o C# responde com status via `ExecuteScriptAsync`
- **Modo de ativação por duplo clique**: o macro só inicia quando o usuário pressiona o botão configurado duas vezes dentro de uma janela de tempo (padrão 200 ms) e se mantém ativo enquanto o botão está pressionado no segundo clique

---

## Estrutura do Projeto

```
ZeusAuto/
├── ZeusAuto.html                   # Página principal da interface
├── css/
│   ├── styles.css                  # Ponto de entrada CSS
│   ├── base/base.css               # Reset, variáveis, tipografia
│   ├── components/components.css   # Botões, cards, toggles, sliders
│   ├── layout/layout.css           # Sidebar, painéis, grid
│   ├── pages/pages.css             # Estilos específicos por página
│   └── utilities/utilities.css     # Classes utilitárias
├── js/
│   ├── app.js                      # Inicialização e wiring global
│   ├── core/
│   │   ├── state.js                # Estado global (state.macros, atalhos, settings)
│   │   ├── navigation.js           # Troca de páginas (switchPage)
│   │   └── ui.js                   # Helpers de UI (toast, toggles, etc.)
│   ├── features/
│   │   ├── native-bridge.js        # ZeusNativeBridge — comunicação com C#
│   │   ├── import-export.js        # Exportar/importar JSON de perfil e atalhos
│   │   └── cursor-glow.js          # Efeito visual do cursor
│   └── pages/
│       ├── macros.js               # Página de configuração de macros
│       ├── profiles.js             # Página de perfis + utils de slider
│       ├── atalhos.js              # Página de atalhos de teclado
│       └── settings.js             # Página de configurações
├── img/
│   └── ZeusAuto.png                # Logo
└── ZeusAuto.App/                   # Projeto C# WinForms + WebView2
    ├── Program.cs                  # Entry point ([STAThread])
    ├── MainForm.cs                 # Formulário principal, WebView2, OnWebMessageReceived
    ├── NativeBridgeMessage.cs      # DTOs de desserialização do JSON da interface
    └── ZeusAuto.Engine/            # Biblioteca da engine (projeto separado)
        └── Core/
            ├── MacroConfig.cs          # Modelo de configuração da engine
            ├── MacroState.cs           # Enum: Idle / WaitingSecondClick / Running
            ├── MacroEngine.cs          # Lógica principal do macro (loop, state machine)
            ├── InputListener.cs        # Hook global de teclado e mouse (SetWindowsHookEx)
            ├── MouseSimulator.cs       # Envio de cliques via SendInput
            ├── JsonConfigLoader.cs     # Leitura de MacroConfig a partir de JSON em disco
            ├── ProfileManager.cs       # Gerenciamento de perfis com FileSystemWatcher
            ├── ProfileChangedEventArgs.cs
            ├── InputEventArgs.cs
            └── Interfaces/
                ├── IInputListener.cs
                └── IMouseSimulator.cs
```

---

## Arquitetura e Fluxo de Dados

```
┌──────────────────────────────────────────────────────────┐
│                     Interface HTML/JS                    │
│                                                          │
│  state.macros[key] = { interval, cpsBase, humanize,      │
│                         cpsMin, cpsMax, shortcuts, ... } │
│                                                          │
│  saveMacroKey()                                          │
│      └─► ZeusNativeBridge.setActiveMacro(key)            │
│              └─► buildProfile() → postMessage(JSON)      │
└────────────────────────┬─────────────────────────────────┘
                         │  window.chrome.webview.postMessage
                         │  { type: "profile:update", profile: {...} }
                         ▼
┌──────────────────────────────────────────────────────────┐
│                    MainForm.cs (C#)                      │
│                                                          │
│  OnWebMessageReceived                                    │
│      └─► Deserializa NativeBridgeMessage                 │
│      └─► ToMacroConfig(profile)  ← conversão CPS → ms    │
│      └─► _engine.LoadConfig(config)                      │
│      └─► _engine.EnableMonitoring()                      │
│      └─► PostNativeStatus(mensagem) → ExecuteScriptAsync │
└────────────────────────┬─────────────────────────────────┘
                         │
                         ▼
┌──────────────────────────────────────────────────────────┐
│                   MacroEngine.cs                         │
│                                                          │
│  InputListener (hook Win32)                              │
│      MouseDown → HandleInputDown → state machine         │
│      MouseUp   → HandleInputUp  → state machine          │
│                                                          │
│  State machine:                                          │
│    Idle ──[1º clique]──► WaitingSecondClick              │
│    WaitingSecondClick ──[solto + 2º clique dentro        │
│                          da janela]──► Running           │
│    Running ──[solto]──► Idle                             │
│                                                          │
│  RunMacroAsync:                                          │
│    loop { Click(button) → Delay(CalculateDelay()) }      │
│                                                          │
│  MouseSimulator → SendInput (Win32 user32.dll)           │
└──────────────────────────────────────────────────────────┘
```

---

## Interface Web

### state.js — Estado Global

```js
state.macros[key] = {
  interval:  200,    // janela do double-click em ms (slider "Intervalo")
  cpsBase:   13,     // CPS fixo quando humanize = false
  humanize:  false,  // modo variável ligado/desligado
  cpsMin:    10,     // CPS mínimo para o humanize
  cpsMax:    16,     // CPS máximo para o humanize
  shortcuts: false,  // atalhos +/- CPS habilitados
  cpsPlus:   [],     // teclas do atalho de +1 CPS
  cpsMinus:  [],     // teclas do atalho de -1 CPS
  bip:       false,  // feedback sonoro
  bipHz:     200     // frequência do bip em Hz
}
```

`key` é o nome do botão do mouse (ex: `"Tecla Esquerda"`, `"Tecla Direita"`).

### native-bridge.js — ZeusNativeBridge

Objeto singleton responsável por toda a comunicação com o C#.

| Método | Descrição |
|---|---|
| `isAvailable()` | Verifica se `window.chrome.webview` existe (WebView2) |
| `buildProfile()` | Monta o objeto de perfil a partir de `state` |
| `sync()` | Serializa e envia o perfil via `postMessage` |
| `setActiveMacro(key)` | Define o macro ativo e chama `sync()` |

**Hooks instalados automaticamente** sobre as funções globais:

| Função hookada | Quando dispara o sync |
|---|---|
| `saveMacroKey()` | Após salvar a configuração de um macro |
| `executeDeleteKey()` | Após excluir um macro |
| `openConfigureForKey(key)` | Ao abrir configuração de macro já existente |
| `handleImportProfile()` | Após importar perfil JSON (com 50 ms de delay) |
| `handleImportAtalho()` | Após importar atalhos JSON (com 50 ms de delay) |
| `DOMContentLoaded` | Sync inicial com 100 ms de delay |

### macros.js — Configuração de Macros

Campos do formulário de configuração:

| Campo HTML | Propriedade salva | Tipo | Descrição |
|---|---|---|---|
| `cfg-interval` | `interval` | ms (int) | Janela de tempo do double-click |
| `cfg-cps-base` | `cpsBase` | CPS (int) | Velocidade fixa (humanize OFF) |
| `humanize` toggle | `humanize` | bool | Liga/desliga modo variável |
| `cfg-cps-min` | `cpsMin` | CPS (int) | CPS mínimo do range humanize |
| `cfg-cps-max` | `cpsMax` | CPS (int) | CPS máximo do range humanize |

---

## Engine C#

### MacroConfig.cs

Modelo interno da engine após a conversão dos dados da interface:

| Propriedade | Tipo | Descrição |
|---|---|---|
| `Enabled` | bool | Liga/desliga o macro |
| `TriggerButton` | string? | Botão que ativa (ex: `"MouseLeft"`) |
| `ClickButton` | string? | Botão que é clicado (geralmente igual ao trigger) |
| `ActivationMode` | string? | Sempre `"DoubleClickHold"` quando vindo da interface |
| `DoubleClickWindowMs` | int? | Janela de tempo para o segundo clique em ms |
| `IntervalMs` | int | Delay entre cliques em ms (convertido de CPS) |
| `RandomizationEnabled` | bool | Humanize ligado/desligado |
| `RandomMin` | int | Offset mínimo de variação em ms |
| `RandomMax` | int | Offset máximo de variação em ms |
| `StartHotkey` | string? | Hotkey para ativar monitoramento |
| `StopHotkey` | string? | Hotkey para desativar monitoramento |

### MacroEngine.cs — Máquina de Estados

```
Idle
 │  MouseDown (trigger)
 ▼
WaitingSecondClick
 │  MouseUp (trigger) → registra _firstClickReleasedAt
 │
 │  MouseDown (trigger) dentro da janela DoubleClickWindowMs
 ▼
Running ──► RunMacroAsync (loop de cliques)
 │
 │  MouseUp (trigger) enquanto Running
 ▼
Idle
```

### InputListener.cs

Instala dois hooks globais via `SetWindowsHookEx` em thread STA dedicada:
- **Hook de teclado** (`WH_KEYBOARD_LL = 13`): captura `WM_KEYDOWN`, `WM_KEYUP`, `WM_SYSKEYDOWN`, `WM_SYSKEYUP`
- **Hook de mouse** (`WH_MOUSE_LL = 14`): captura `WM_LBUTTONDOWN/UP`, `WM_RBUTTONDOWN/UP`, `WM_MBUTTONDOWN/UP`, `WM_XBUTTONDOWN/UP`

Emite eventos `InputDown` e `InputUp` com o nome normalizado do input, além de `StartHotkeyPressed` e `StopHotkeyPressed` quando combos de teclas são detectados.

### MouseSimulator.cs

Usa `SendInput` (Win32) para enviar eventos de mouse sintéticos. Suporta os 5 botões: `MouseLeft`, `MouseRight`, `MouseMiddle`, `MouseX1`, `MouseX2`.

### JsonConfigLoader.cs

Carrega `MacroConfig` diretamente de um arquivo `.json` em disco (fluxo alternativo sem interface). Detecta automaticamente se o JSON é um perfil da interface (contém `"macros"`) ou um `MacroConfig` bruto.

### ProfileManager.cs

Gerencia troca de perfis via arquivo JSON com `FileSystemWatcher` para recarregamento automático ao detectar alterações no arquivo (debounce de 150 ms).

---

## Protocolo de Comunicação (Bridge)

### Interface → C# (`postMessage`)

```json
{
  "type": "profile:update",
  "profile": {
    "profileName": "Interface",
    "enabled": true,
    "activeMacro": "Tecla Esquerda",
    "macros": {
      "Tecla Esquerda": {
        "interval": 200,
        "cpsBase": 13,
        "humanize": false,
        "cpsMin": 10,
        "cpsMax": 16,
        "shortcuts": false,
        "cpsPlus": [],
        "cpsMinus": [],
        "bip": false,
        "bipHz": 200
      }
    },
    "atalhos": { ... },
    "settings": { ... }
  }
}
```

### C# → Interface (`ExecuteScriptAsync`)

```js
window.ZeusNativeBridgeStatus?.("mensagem", isError)
```

Exibe um toast de sucesso ou erro na interface.

---

## Configuração JSON

### DTOs de desserialização (NativeBridgeMessage.cs)

```
NativeBridgeMessage
├── type: string          ("profile:update")
└── profile: WebProfile
    ├── profileName: string
    ├── enabled: bool
    ├── activeMacro: string    (chave do macro ativo)
    ├── macros: Dictionary<string, WebMacroConfig>
    │   └── WebMacroConfig
    │       ├── interval: int      (janela double-click em ms)
    │       ├── cpsBase: int       ⚠ campo ausente — ver Bugs
    │       ├── humanize: bool
    │       ├── cpsMin: int
    │       └── cpsMax: int
    └── atalhos: Dictionary<string, WebShortcutConfig>
        └── WebShortcutConfig
            ├── enabled: bool
            └── keys: string[]
```

### Normalização de botões do mouse

| Valor da interface | Valor normalizado |
|---|---|
| `"Tecla Esquerda"` | `"MouseLeft"` |
| `"Tecla Direita"` | `"MouseRight"` |
| `"Tecla Scroll"` | `"MouseMiddle"` |
| `"Tecla xbutton4"` | `"MouseX1"` |
| `"Tecla xbutton5"` | `"MouseX2"` |

---

## Lógica de CPS e Humanize

### Humanize desligado (cpsBase fixo)

O campo `cpsBase` define uma velocidade constante em cliques por segundo. Para a engine, o que importa é o delay entre cliques em milissegundos:

```
IntervalMs = 1000 / cpsBase
Exemplo: 13 CPS → 1000 / 13 ≈ 77 ms
```

### Humanize ligado (variação por range)

O `cpsBase` é desconsiderado. A velocidade base é a média aritmética de `cpsMin` e `cpsMax` convertida para ms:

```
avgCps    = (cpsMin + cpsMax) / 2
IntervalMs = 1000 / avgCps
Exemplo: cpsMin=10, cpsMax=16 → avg=13 → 77 ms
```

A variação (offset aleatório) é calculada a partir da diferença de intervalo entre `cpsMin` e `cpsMax`:

```
msAtCpsMax = 1000 / cpsMax   → menor delay (CPS maior)
msAtCpsMin = 1000 / cpsMin   → maior delay (CPS menor)
randomMaxMs = (msAtCpsMin - msAtCpsMax) / 2

Exemplo: cpsMin=10, cpsMax=16
  msAtCpsMax = 62 ms
  msAtCpsMin = 100 ms
  randomMaxMs = (100 - 62) / 2 = 19 ms
```

O `CalculateDelay` na engine aplica:
```csharp
delay = IntervalMs + Random(-randomOffset, +randomOffset)
```

Isso faz o delay oscilar entre `~58 ms` (≈17 CPS) e `~96 ms` (≈10 CPS), criando a sensação de inconstância enquanto permanece no range configurado.

---

## Modo de Ativação DoubleClickHold

O macro só ativa com uma sequência precisa:

```
1. Pressionar o botão                → estado: WaitingSecondClick
2. Soltar o botão                    → registra _firstClickReleasedAt
3. Pressionar novamente dentro       → verifica DoubleClickWindowMs
   do tempo (≤ interval ms)            se dentro: → Running
                                       se fora:   → reseta, volta para WaitingSecondClick
4. Manter pressionado                → macro clicando em loop
5. Soltar                            → estado: Idle, macro para
```

O campo `interval` da interface (padrão 200 ms) é a janela de tempo do passo 3 (`DoubleClickWindowMs`). **Não** é o delay entre cliques.

---

## Bugs Identificados e Correções

### Bug 1 — `cpsBase` não existe em `WebMacroConfig`

**Problema:** O campo `cpsBase` é salvo pelo JS mas `WebMacroConfig` não o declara, logo o C# nunca recebe a velocidade base configurada pelo usuário.

**Correção em `NativeBridgeMessage.cs`:**
```csharp
[JsonPropertyName("cpsBase")]
public int CpsBase { get; set; }
```

---

### Bug 2 — `interval` (janela do double-click) usado como delay de clique

**Problema:** `ToMacroConfig` em `MainForm.cs` faz:
```csharp
IntervalMs = Math.Max(1, macro.Interval), // ← 200 ms (janela do clique duplo!)
```
O `interval` da interface é a **janela de tempo** do double-click, não o delay de clique. Usando 200 ms como `IntervalMs`, o macro clica a ≈5 CPS em vez dos 13 CPS configurados.

**Correção em `MainForm.cs`:**
```csharp
// Humanize OFF: converte cpsBase para ms
clickIntervalMs = macro.CpsBase > 0 ? 1000 / macro.CpsBase : 100;

// Humanize ON: usa a média de cpsMin e cpsMax
double avgCps = (macro.CpsMin + macro.CpsMax) / 2.0;
clickIntervalMs = avgCps > 0 ? (int)(1000.0 / avgCps) : 100;

// A janela do double-click vai para o campo correto:
DoubleClickWindowMs = macro.Interval,
IntervalMs = clickIntervalMs,
```

---

### Bug 3 — `RandomMin`/`RandomMax` recebem CPS em vez de ms

**Problema:** `ToMacroConfig` passa:
```csharp
RandomMin = macro.CpsMin, // ex: 10 (CPS!)
RandomMax = macro.CpsMax, // ex: 16 (CPS!)
```
O `CalculateDelay` usa esses valores como **offset em ms** (`interval ± randomOffset`). Somar 10–16 ms a um delay de 77 ms resulta em CPS totalmente errado, sem relação com o range configurado.

**Correção em `MainForm.cs`:**
```csharp
// CPS maior → delay menor → RandomMin
// CPS menor → delay maior → RandomMax
int msAtCpsMax = 1000 / macro.CpsMax;
int msAtCpsMin = 1000 / macro.CpsMin;
RandomMin = 0;
RandomMax = (msAtCpsMin - msAtCpsMax) / 2;
```

---

### Bug 4 — `DoubleClickWindowMs` nunca é preenchido

**Problema:** `MacroConfig.DoubleClickWindowMs` é `int?` e fica `null` porque `ToMacroConfig` nunca o atribui. Em `IsWithinDoubleClickWindow()`:
```csharp
if (!config.DoubleClickWindowMs.HasValue || !_firstClickReleasedAt.HasValue)
{
    return true; // ← sem janela definida, qualquer dois cliques ativam
}
```
Sem a janela, o double-click não tem restrição de tempo — o comportamento esperado não funciona.

**Correção:** Atribuir `DoubleClickWindowMs = macro.Interval` no `ToMacroConfig` (resolvido junto com o Bug 2).

---

### Bug 5 — Sync inicial com `state.macros` vazio desabilita a engine

**Problema:** No `native-bridge.js`, `DOMContentLoaded` chama `ZeusNativeBridge.sync()` com 100 ms de delay. Se ainda não há macros configurados, `buildProfile()` produz `{ enabled: false }`. O C# recebe isso, chama `LoadConfig` com `Enabled = false`, e a engine para. Mesmo depois de configurar um macro, o `EnableMonitoring()` é chamado, mas o estado `Enabled = false` na config faz `StartMacro()` retornar imediatamente.

**Correção em `native-bridge.js`:**
```js
window.addEventListener('DOMContentLoaded', () => {
  setTimeout(() => {
    if (Object.keys(state.macros).length > 0) {
      ZeusNativeBridge.sync();
    }
  }, 100);
});
```

### Bug 6 — Cliques sintéticos do `SendInput` interrompem a engine imediatamente

**Problema:** O `MouseHookCallback` no `InputListener` não filtrava eventos sintéticos gerados pelo próprio `MouseSimulator` via `SendInput`. Cada clique emitido pelo macro durante o estado `Running` disparava um `WM_LBUTTONUP` de volta no hook, que chegava em `HandleInputUp` e chamava `StopMacro()` — parando o macro logo após o primeiro clique sintético. O resultado era que o auto-clicker nunca iniciava de fato.

**Correção em `InputListener.cs`:**
```csharp
// Ignora eventos sintéticos gerados pelo próprio SendInput (LLMHF_INJECTED = 0x1)
bool isInjected = (data.flags & 0x1) != 0;
if (!isInjected)
{
    // processa o evento normalmente
}
```

---



- **Windows** 10 ou superior (necessário para `SetWindowsHookEx` e `SendInput`)
- **.NET 8** (Windows target: `net8.0-windows`)
- **WebView2 Runtime** instalado (incluído no Edge / Windows 11; [download](https://developer.microsoft.com/microsoft-edge/webview2/))
- **Visual Studio 2022** ou `dotnet build` via CLI para compilar

### Build

```bash
# Restaurar dependências e compilar
cd ZeusAuto/ZeusAuto.App
dotnet build

# Executar
dotnet run
```

O executável busca `ZeusAuto.html` primeiro relativo ao diretório de build (`../../../../ZeusAuto.html`) e depois relativo ao `AppContext.BaseDirectory`, permitindo rodar tanto em modo Debug quanto publicado.
