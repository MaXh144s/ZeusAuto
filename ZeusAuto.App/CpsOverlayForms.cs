using System.Runtime.InteropServices;
using ZeusAuto.Engine.Core;

namespace ZeusAuto.App;

// ─────────────────────────────────────────────────────────────────────────────
//  CpsOverlayForm
//
//  Overlay TopMost que exibe os CPS de cada macro lado a lado.
//  Pintado inteiramente no OnPaint — sem controles filhos.
//
//  Layout 100 % responsivo: todas as fontes, posições e tamanhos derivam
//  de Width/Height em tempo real. Redimensionar a janela reescala tudo.
//
//  Por slot exibe:
//    • Dot de status (verde ativo / cinza idle)
//    • Nome do botão
//    • CPS real (durante ativação) ou "0.0 CPS" (idle)
//    • CPS configurado  |  DoubleClickWindowMs
// ─────────────────────────────────────────────────────────────────────────────
public sealed class CpsOverlayForm : Form
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern int  SendMessage(IntPtr hWnd, int msg, int wp, int lp);
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION       = 0x2;
    private const int HT_BOTTOMRIGHT   = 17;

    private static readonly Color ColBg     = Color.FromArgb(18, 20, 28);
    private static readonly Color ColBorder = Color.FromArgb(70, 110, 255);
    private static readonly Color ColGlow   = Color.FromArgb(25, 110, 255);
    private static readonly Color ColActive = Color.FromArgb(60, 225, 140);
    private static readonly Color ColIdle   = Color.FromArgb(120, 120, 140);
    private static readonly Color ColName   = Color.FromArgb(240, 242, 255);
    private static readonly Color ColMuted  = Color.FromArgb(140, 145, 165);

    // Perfil de customização recebido do JS (null = usar defaults acima)
    private OverlayProfileConfig? _overlayProfile = null;

    // ── Aplica perfil de customização ─────────────────────────────────────────
    internal void ApplyOverlayProfile(OverlayProfileConfig? profile)
    {
        if (InvokeRequired) { Invoke(() => ApplyOverlayProfile(profile)); return; }
        _overlayProfile = profile;
        ApplyProfileAppearance();
        Invalidate();
    }

    private void ApplyProfileAppearance()
    {
        if (_overlayProfile?.Background is { } bg)
        {
            BackColor = ParseHex(bg.Color, ColBg);
            Opacity   = Math.Clamp(bg.Opacity, 0.0, 1.0);
        }
        else
        {
            BackColor = ColBg;
            Opacity   = 0.92;
        }
    }

    // ── Helpers para ler o perfil com fallback para os valores padrão ─────────

    private Color GetBorderColor()  => _overlayProfile?.Border is { } b ? ParseHex(b.Color, ColBorder) : ColBorder;
    private Color GetGlowColor()    => _overlayProfile?.Border is { } b ? ParseHex(b.GlowColor, ColGlow) : ColGlow;
    private bool  IsGlowEnabled()   => _overlayProfile?.Border?.GlowEnabled ?? true;
    private int   GetGlowIntensity()=> _overlayProfile?.Border?.GlowIntensity ?? 20;

    private OverlayElement? GetElement(string id) =>
        _overlayProfile?.Elements?.FirstOrDefault(e => e.Id == id);

    private bool   IsElementVisible(string id)  => GetElement(id)?.Visible ?? true;
    private float  GetElementFontSize(string id, float defaultPt) =>
        (float)(GetElement(id)?.FontSize ?? defaultPt);

    private Color GetElementColorActive(string id, Color fallback) =>
        ParseHex(GetElement(id)?.ColorActive, fallback);
    private Color GetElementColorIdle(string id, Color fallback) =>
        ParseHex(GetElement(id)?.ColorIdle, fallback);
    private Color GetElementColorPaused(string id, Color fallback) =>
        ParseHex(GetElement(id)?.ColorPaused, fallback);

    /// <summary>
    /// Converte string "#RRGGBB" para Color. Retorna fallback em caso de erro.
    /// </summary>
    private static Color ParseHex(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
                return Color.FromArgb(
                    Convert.ToInt32(hex[..2], 16),
                    Convert.ToInt32(hex[2..4], 16),
                    Convert.ToInt32(hex[4..6], 16));
        }
        catch { /* ignora — retorna fallback */ }
        return fallback;
    }

    // Largura mínima confortável por slot (px). Abaixo disso o conteúdo
    // começa a ficar apertado — usada para calcular o raio do canto também.
    private const int SlotMinW = 140;
    // Altura mínima da janela (px)
    private const int SlotMinH = 60;
    // Altura de referência na qual as proporções foram desenhadas originalmente
    private const float RefH = 110f;

    private IReadOnlyList<EngineSlot> _slots     = Array.Empty<EngineSlot>();
    private bool                      _forceHide = false;
    private bool                      _paused    = false;

    // Cor "pausado" — laranja-âmbar vibrante, legível sobre o fundo escuro
    private static readonly Color ColPaused = Color.FromArgb(255, 160, 50);

    private readonly Panel _gripper;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };

    public CpsOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        BackColor       = ColBg;
        TopMost         = true;
        ShowInTaskbar   = false;
        Opacity         = 0.92;
        Width           = 220;
        Height          = 110;
        MinimumSize     = new Size(SlotMinW, SlotMinH);
        StartPosition   = FormStartPosition.Manual;

        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint  |
            ControlStyles.UserPaint,
            true);

        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(wa.Right - Width - 20, wa.Top + 20);

        _gripper = new Panel { Size = new Size(24, 24), BackColor = ColBg, Cursor = Cursors.SizeNWSE };
        _gripper.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_BOTTOMRIGHT, 0); }
        };
        Controls.Add(_gripper);

        MouseDown += OnDrag;
        Resize    += (_, _) => { PositionGripper(); ApplyRegion(); Invalidate(); };
        ApplyRegion();
        PositionGripper();

        _timer.Tick += (_, _) => { if (Visible) Invalidate(); };
        _timer.Start();
    }

    // ── API pública ───────────────────────────────────────────────────────────

    internal void Apply(IReadOnlyList<EngineSlot> slots, bool visible)
    {
        if (InvokeRequired) { Invoke(() => Apply(slots, visible)); return; }
        _slots = slots;

        if (slots.Count == 0)
        {
            _forceHide = true;
            Width = 220; Height = 110;
            if (Visible) Hide();
            return;
        }

        _forceHide = false;
        // Atualiza o tamanho mínimo conforme o número de slots,
        // garantindo que a janela não possa ser reduzida abaixo do espaço
        // mínimo necessário para exibir todos os macros lado a lado.
        int newMinW = slots.Count * SlotMinW;
        MinimumSize = new Size(newMinW, SlotMinH);
        // Só redefine o tamanho se for a primeira carga (usuário pode ter redimensionado)
        if (!Visible)
        {
            Width  = Math.Clamp(slots.Count * 190, newMinW, 900);
            Height = 110;
        }
        else if (Width < newMinW)
        {
            // Janela já visível mas menor que o novo mínimo (ex.: adicionou
            // um segundo macro enquanto a janela estava reduzida).
            // Expande suavemente até o mínimo necessário.
            Width = newMinW;
        }
        ApplyRegion();
        PositionGripper();
        ApplyVisibility(visible);
        Invalidate();
    }

    public void ApplyVisibility(bool visible)
    {
        if (InvokeRequired) { Invoke(() => ApplyVisibility(visible)); return; }
        if (_forceHide) { if (Visible) Hide(); return; }
        if (visible) Show(); else Hide();
    }

    /// <summary>
    /// Atualiza o estado de pausa. Quando pausado, o overlay continua visível
    /// mas substitui o CPS por "PAUSADO" em laranja-âmbar.
    /// </summary>
    public void ApplyPaused(bool paused)
    {
        if (InvokeRequired) { Invoke(() => ApplyPaused(paused)); return; }
        _paused = paused;
        if (Visible) Invalidate();
    }

    // ── Pintura responsiva ────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode     = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        int count = _slots.Count;

        // ── Escala global derivada da altura atual ──────────────────────────
        float scale = Height / RefH;

        int cornerR = Math.Clamp((int)(12 * scale), 6, 20);

        // ── Cores do perfil (com fallback para os defaults) ─────────────────
        Color colBg     = _overlayProfile?.Background is { } bg ? ParseHex(bg.Color, ColBg) : ColBg;
        Color colBorder = GetBorderColor();
        Color colGlow   = GetGlowColor();

        // Fundo
        using (var b = new SolidBrush(colBg))
            g.FillPath(b, RoundRect(new Rectangle(0, 0, Width, Height), cornerR));

        // Glow
        if (IsGlowEnabled())
        {
            int glowAlpha = Math.Clamp(GetGlowIntensity(), 5, 80);
            using var p = new Pen(Color.FromArgb(glowAlpha, colGlow), 6f);
            g.DrawPath(p, RoundRect(new Rectangle(-1, -1, Width + 1, Height + 1), cornerR + 2));
        }
        else
        {
            // Glow desativado — ainda renderiza uma camada sutil para não parecer cortado
            using var p = new Pen(Color.FromArgb(8, colGlow), 2f);
            g.DrawPath(p, RoundRect(new Rectangle(-1, -1, Width + 1, Height + 1), cornerR + 2));
        }

        // Borda
        using (var p = new Pen(colBorder, 1.2f))
            g.DrawPath(p, RoundRect(new Rectangle(1, 1, Width - 3, Height - 3), cornerR));

        if (count == 0) return;

        int slotW = Math.Max(SlotMinW, Width / count);

        // ── Tamanhos de fonte: lê do perfil com fallback ────────────────────
        float fnName   = Math.Max(6f,  GetElementFontSize("buttonName", 8.5f)  * scale);
        float fnCps    = Math.Max(10f, GetElementFontSize("cpsReal",    20f)   * scale);
        float fnPaused = Math.Max(10f, GetElementFontSize("pausedText", 20f)   * scale);
        float fnFooter = Math.Max(5f,  GetElementFontSize("cpsCfg",     9.5f)  * scale);

        using var fName   = new Font("Segoe UI", fnName,   FontStyle.Bold,    GraphicsUnit.Point);
        using var fCps    = new Font("Segoe UI", fnCps,    FontStyle.Bold,    GraphicsUnit.Point);
        using var fPaused = new Font("Segoe UI", fnPaused, FontStyle.Bold,    GraphicsUnit.Point);
        using var fFooter = new Font("Segoe UI", fnFooter, FontStyle.Regular, GraphicsUnit.Point);

        var sfL = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        var sfR = new StringFormat { Alignment = StringAlignment.Far,  LineAlignment = StringAlignment.Center };

        int pad      = Math.Max(8,  (int)(14  * scale));
        int dotSize  = Math.Max(5,  (int)(8   * scale));
        int mid      = Height / 2;

        float offDot    = -26f * scale;
        float offName   = -30f * scale;
        float offCps    = -18f * scale;
        float offFooter =  20f * scale;

        float hName    = Math.Max(12f, 18f * scale);
        float hCps     = Math.Max(20f, 36f * scale);
        float hFooter  = Math.Max(10f, 18f * scale);

        for (int i = 0; i < count; i++)
        {
            var  slot   = _slots[i];
            bool active = !_paused && slot.State == MacroState.Running;

            // ── Cores dos elementos pelo perfil ─────────────────────────────
            Color colDot   = _paused
                ? GetElementColorPaused("statusDot", ColPaused)
                : (active ? GetElementColorActive("statusDot", ColActive) : GetElementColorIdle("statusDot", ColIdle));

            Color colCps   = _paused
                ? GetElementColorPaused("cpsReal", ColPaused)
                : (active ? GetElementColorActive("cpsReal", ColActive) : GetElementColorIdle("cpsReal", ColIdle));

            Color colNameC = _paused
                ? GetElementColorPaused("buttonName", ColPaused)
                : (active ? GetElementColorActive("buttonName", ColName) : GetElementColorIdle("buttonName", ColName));

            Color colFooter = _paused
                ? GetElementColorPaused("cpsCfg", ColPaused)
                : (active ? GetElementColorActive("cpsCfg", ColMuted) : GetElementColorIdle("cpsCfg", ColMuted));

            Color colPausedC = GetElementColorPaused("pausedText", ColPaused);

            int x = i * slotW + pad;
            int w = slotW - pad * 2;

            // Separador
            if (i > 0)
                using (var p = new Pen(Color.FromArgb(40, colBorder), 1f))
                    g.DrawLine(p, i * slotW, (int)(12 * scale), i * slotW, Height - (int)(12 * scale));

            // Dot de status
            if (IsElementVisible("statusDot"))
                using (var b = new SolidBrush(colDot))
                    g.FillEllipse(b, x, mid + offDot, dotSize, dotSize);

            // Nome do botão
            if (IsElementVisible("buttonName"))
                using (var b = new SolidBrush(colNameC))
                    g.DrawString(FriendlyName(slot.MacroKey), fName, b,
                        new RectangleF(x + dotSize + 3, mid + offName, w - dotSize - 3, hName), sfL);

            // CPS Real ou PAUSADO
            if (_paused)
            {
                if (IsElementVisible("pausedText"))
                    using (var b = new SolidBrush(colPausedC))
                        g.DrawString("PAUSADO", fPaused, b,
                            new RectangleF(x, mid + offCps, w, hCps), sfL);
            }
            else
            {
                if (IsElementVisible("cpsReal"))
                {
                    double realCps = active ? Math.Truncate(slot.RealCps * 10.0) / 10.0 : 0.0;
                    using var b = new SolidBrush(colCps);
                    g.DrawString($"{realCps:F1} CPS", fCps, b,
                        new RectangleF(x, mid + offCps, w, hCps), sfL);
                }
            }

            // Rodapé
            string msText  = slot.DoubleClickWindowMs.HasValue
                ? $"{slot.DoubleClickWindowMs} ms"
                : "-- ms";
            string cfgText = $"{slot.ConfigCps:F1} CPS";

            using (var b = new SolidBrush(colFooter))
            {
                if (IsElementVisible("doubleClick"))
                    g.DrawString(msText,  fFooter, b, new RectangleF(x, mid + offFooter, w, hFooter), sfL);
                if (IsElementVisible("cpsCfg"))
                    g.DrawString(cfgText, fFooter, b, new RectangleF(x, mid + offFooter, w, hFooter), sfR);
            }
        }

        // Gripper visual
        using var rp = new Pen(Color.FromArgb(90, colBorder), 1f);
        g.DrawLine(rp, Width - 16, Height - 6,  Width - 6, Height - 16);
        g.DrawLine(rp, Width - 22, Height - 6,  Width - 6, Height - 22);
        g.DrawLine(rp, Width - 28, Height - 6,  Width - 6, Height - 28);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_ERASEBKGND = 0x0014;
        if (m.Msg == WM_ERASEBKGND) { m.Result = (IntPtr)1; return; }
        base.WndProc(ref m);
    }

    // ── Internos ──────────────────────────────────────────────────────────────

    private void OnDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); }
    }

    private void PositionGripper() =>
        _gripper.Location = new Point(Width - _gripper.Width, Height - _gripper.Height);

    private void ApplyRegion()
    {
        Region?.Dispose();
        Region = new Region(RoundRect(new Rectangle(0, 0, Width, Height), Math.Clamp((int)(12 * (Height / RefH)), 6, 20)));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        _timer.Stop();
        base.OnFormClosing(e);
    }

    private static string FriendlyName(string key) =>
        key.Trim().ToUpperInvariant() switch
        {
            "MOUSELEFT"   or "TECLA ESQUERDA" => "Esquerdo",
            "MOUSERIGHT"  or "TECLA DIREITA"  => "Direito",
            "MOUSEMIDDLE" or "TECLA SCROLL"   => "Scroll",
            "MOUSEX1"     or "TECLA XBUTTON4" => "X1",
            "MOUSEX2"     or "TECLA XBUTTON5" => "X2",
            _                                 => key
        };

    private static System.Drawing.Drawing2D.GraphicsPath RoundRect(Rectangle r, int rad)
    {
        int d = rad * 2;
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        p.AddArc(r.X,         r.Y,          d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y,          d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d,   0, 90);
        p.AddArc(r.X,         r.Bottom - d, d, d,  90, 90);
        p.CloseFigure();
        return p;
    }
}