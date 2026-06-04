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

    // Largura mínima confortável por slot (px). Abaixo disso o conteúdo
    // começa a ficar apertado — usada para calcular o raio do canto também.
    private const int SlotMinW = 140;
    // Altura mínima da janela (px)
    private const int SlotMinH = 60;
    // Altura de referência na qual as proporções foram desenhadas originalmente
    private const float RefH = 110f;

    private IReadOnlyList<EngineSlot> _slots     = Array.Empty<EngineSlot>();
    private bool                      _forceHide = false;

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

    // ── Pintura responsiva ────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode     = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        int count = _slots.Count;

        // ── Escala global derivada da altura atual ──────────────────────────
        // Todas as métricas verticais escalam proporcionalmente a RefH = 110.
        float scale = Height / RefH;

        // Raio dos cantos: escala com a altura, limitado para não ficar enorme
        int cornerR = Math.Clamp((int)(12 * scale), 6, 20);

        // Fundo
        using (var b = new SolidBrush(ColBg))
            g.FillPath(b, RoundRect(new Rectangle(0, 0, Width, Height), cornerR));

        // Glow
        using (var p = new Pen(Color.FromArgb(20, ColGlow), 6f))
            g.DrawPath(p, RoundRect(new Rectangle(-1, -1, Width + 1, Height + 1), cornerR + 2));

        // Borda
        using (var p = new Pen(ColBorder, 1.2f))
            g.DrawPath(p, RoundRect(new Rectangle(1, 1, Width - 3, Height - 3), cornerR));

        if (count == 0) return;

        // ── Largura de cada slot ────────────────────────────────────────────
        // MinimumSize já garante Width >= count * SlotMinW, então
        // Width / count é sempre >= SlotMinW. Mantemos o Math.Max por
        // segurança defensiva (ex.: durante transição de resize).
        int slotW = Math.Max(SlotMinW, Width / count);

        // ── Tamanhos de fonte derivados de Height ───────────────────────────
        float fnName   = Math.Max(6f,  8.5f  * scale);
        float fnCps    = Math.Max(10f, 20f   * scale);
        float fnFooter = Math.Max(5f,  9.5f  * scale);   // era 7.5 → sobe para 9.5

        using var fName   = new Font("Segoe UI", fnName,   FontStyle.Bold,    GraphicsUnit.Point);
        using var fCps    = new Font("Segoe UI", fnCps,    FontStyle.Bold,    GraphicsUnit.Point);
        using var fFooter = new Font("Segoe UI", fnFooter, FontStyle.Regular, GraphicsUnit.Point);

        var sfL = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        var sfR = new StringFormat { Alignment = StringAlignment.Far,  LineAlignment = StringAlignment.Center };

        // ── Métricas verticais proporcionais ───────────────────────────────
        int pad      = Math.Max(8,  (int)(14  * scale));   // padding lateral interno
        int dotSize  = Math.Max(5,  (int)(8   * scale));   // diâmetro do dot
        int mid      = Height / 2;

        // Offsets verticais relativos ao centro (mid)
        float offDot    = -26f * scale;   // dot de status
        float offName   = -30f * scale;   // label do nome
        float offCps    = -18f * scale;   // número CPS grande
        float offFooter =  20f * scale;   // rodapé ms | cfg

        // Alturas das faixas
        float hName    = Math.Max(12f, 18f * scale);
        float hCps     = Math.Max(20f, 36f * scale);
        float hFooter  = Math.Max(10f, 18f * scale);

        for (int i = 0; i < count; i++)
        {
            var  slot   = _slots[i];
            bool active = slot.State == MacroState.Running;
            Color col   = active ? ColActive : ColIdle;

            int x = i * slotW + pad;
            int w = slotW - pad * 2;

            // Separador vertical entre slots
            if (i > 0)
                using (var p = new Pen(Color.FromArgb(40, ColBorder), 1f))
                    g.DrawLine(p, i * slotW, (int)(12 * scale), i * slotW, Height - (int)(12 * scale));

            // Dot de status
            using (var b = new SolidBrush(col))
                g.FillEllipse(b, x, mid + offDot, dotSize, dotSize);

            // Nome do botão
            using (var b = new SolidBrush(ColName))
                g.DrawString(FriendlyName(slot.MacroKey), fName, b,
                    new RectangleF(x + dotSize + 3, mid + offName, w - dotSize - 3, hName), sfL);

            // CPS real (fonte grande, centro)
            double realCps = active ? Math.Truncate(slot.RealCps * 10.0) / 10.0 : 0.0;
            using (var b = new SolidBrush(col))
                g.DrawString($"{realCps:F1} CPS", fCps, b,
                    new RectangleF(x, mid + offCps, w, hCps), sfL);

            // Rodapé: "200 ms  |  13.0 cfg"
            // Ambos os textos ficam dentro do rect do slot (x … x+w),
            // garantindo que o cfgText alinhado à direita não ultrapasse
            // a borda do slot nem sobreponha o slot vizinho.
            string msText  = slot.DoubleClickWindowMs.HasValue
                ? $"{slot.DoubleClickWindowMs} ms"
                : "-- ms";
            string cfgText = $"{slot.ConfigCps:F1} CPS";

            using (var b = new SolidBrush(ColMuted))
            {
                g.DrawString(msText,  fFooter, b, new RectangleF(x,         mid + offFooter, w, hFooter), sfL);
                g.DrawString(cfgText, fFooter, b, new RectangleF(x,         mid + offFooter, w, hFooter), sfR);
            }
        }

        // Gripper visual (sempre no canto inferior direito)
        using var rp = new Pen(Color.FromArgb(90, ColBorder), 1f);
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