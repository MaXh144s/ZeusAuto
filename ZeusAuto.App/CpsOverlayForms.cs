using System.Runtime.InteropServices;

namespace ZeusAuto.App;

// ─────────────────────────────────────────────────────────────────────────────
//  CpsOverlayForm
//
//  Janela flutuante TopMost, separada do MainForm.
//  Exibe um painel filho por macro ativo, lado a lado, cada um mostrando:
//    • Nome do botão
//    • CPS real com 1 casa decimal  (0.0 quando inativo)
//    • Intervalo em ms
//
//  Visibilidade controlada pelo booleano state.settings.cpsOverlay do JS,
//  recebido via bridge → MainForm.ApplyProfile() → CpsOverlayForm.Apply().
//
//  Design:
//    • FormBorderStyle.None + TopMost + ShowInTaskbar=false
//    • Bordas arredondadas via Region + OnPaint (borda + glow)
//    • Painéis filhos em TableLayoutPanel horizontal (crescem uniformemente)
//    • Resize nativo segurando nas bordas (WM_NCHITTEST)
//    • Drag arrastando pela área do formulário
//    • Timer de 200 ms para refresh dos valores
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CpsOverlayForm : Form
{
    // ── Win32 ────────────────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern int  SendMessage(IntPtr hWnd, int msg, int wp, int lp);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION       = 0x2;
    private const int WM_NCHITTEST     = 0x84;
    private const int HT_LEFT = 10, HT_RIGHT = 11;
    private const int HT_TOP  = 12, HT_TOPLEFT = 13, HT_TOPRIGHT = 14;
    private const int HT_BOTTOM = 15, HT_BOTTOMLEFT = 16, HT_BOTTOMRIGHT = 17;
    private const int B = 7; // resize border em px

    // ── Paleta ───────────────────────────────────────────────────────────────
    private static readonly Color ColBg        = Color.FromArgb(225, 13, 13, 20);
    private static readonly Color ColBorder    = Color.FromArgb(170, 75,  95, 255);
    private static readonly Color ColGlow      = Color.FromArgb(45,  55,  75, 255);
    private static readonly Color ColDivider   = Color.FromArgb(42,  42,  62);
    private static readonly Color ColActive    = Color.FromArgb(75,  215, 145);
    private static readonly Color ColIdle      = Color.FromArgb(75,  75,  112);
    private static readonly Color ColName      = Color.FromArgb(205, 205, 250);
    private static readonly Color ColMuted     = Color.FromArgb(90,  90,  130);
    private const int R = 12; // corner radius

    // ── Layout ───────────────────────────────────────────────────────────────
    private readonly TableLayoutPanel _table = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 200 };

    private IReadOnlyList<EngineSlot> _slots = Array.Empty<EngineSlot>();

    // ─────────────────────────────────────────────────────────────────────────
    public CpsOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        BackColor       = Color.FromArgb(13, 13, 20);
        TopMost         = true;
        DoubleBuffered  = true;
        ShowInTaskbar   = false;
        Width           = 220;
        Height          = 80;
        MinimumSize     = new Size(160, 68);
        StartPosition   = FormStartPosition.Manual;

        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(wa.Right - Width - 20, wa.Top + 20);

        _table.Dock      = DockStyle.Fill;
        _table.BackColor = Color.Transparent;
        Controls.Add(_table);

        // Arrastar a janela
        MouseDown        += OnDrag;
        _table.MouseDown += OnDrag;

        Resize += (_, _) => { ApplyRegion(); Invalidate(); };
        ApplyRegion();

        _timer.Tick += (_, _) => RefreshSlots();
        _timer.Start();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  API pública (chamada pelo MainForm)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Recebe a lista de slots ativos do MainForm e reconstrói os painéis.
    /// Chamado sempre que o perfil muda.
    /// </summary>
    internal void Apply(IReadOnlyList<EngineSlot> slots, bool visible)
    {
        if (InvokeRequired) { Invoke(() => Apply(slots, visible)); return; }
        _slots = slots;
        RebuildPanels();
        ApplyVisibility(visible);
    }

    /// <summary>
    /// Mostra ou oculta a janela conforme o flag cpsOverlay.
    /// Chamado isoladamente quando só o setting muda (sem mudança de perfil).
    /// </summary>
    public void ApplyVisibility(bool visible)
    {
        if (InvokeRequired) { Invoke(() => ApplyVisibility(visible)); return; }
        if (visible && !Visible) Show();
        else if (!visible && Visible) Hide();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Painéis filhos lado a lado
    // ─────────────────────────────────────────────────────────────────────────
    private void RebuildPanels()
    {
        _table.SuspendLayout();

        foreach (Control c in _table.Controls.Cast<Control>().ToList()) c.Dispose();
        _table.Controls.Clear();
        _table.ColumnStyles.Clear();
        _table.RowStyles.Clear();
        _table.RowCount    = 1;
        _table.ColumnCount = 0;
        _table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        int count = _slots.Count;

        if (count == 0)
        {
            // Sem macros: painel único de placeholder
            _table.ColumnCount = 1;
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var ph = new Label
            {
                Text      = "Sem macros",
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                ForeColor = ColMuted,
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            ph.MouseDown += OnDrag;
            _table.Controls.Add(ph, 0, 0);

            Width  = 200;
            Height = 72;
        }
        else
        {
            _table.ColumnCount = count;
            float pct = 100f / count;

            for (int i = 0; i < count; i++)
            {
                _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, pct));
                var sp = new SlotPanel(_slots[i], ColActive, ColIdle, ColName, ColMuted);
                sp.Dock      = DockStyle.Fill;
                sp.MouseDown += OnDrag;
                _table.Controls.Add(sp, i, 0);
            }

            // Largura: 190 px por slot, entre 220 e 900
            Width  = Math.Clamp(count * 190, 220, 900);
            Height = 80;
        }

        _table.ResumeLayout();
        ApplyRegion();
        Invalidate();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Refresh (timer 200 ms)
    // ─────────────────────────────────────────────────────────────────────────
    private void RefreshSlots()
    {
        foreach (Control c in _table.Controls)
            if (c is SlotPanel sp) sp.UpdateValues();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Bordas arredondadas
    // ─────────────────────────────────────────────────────────────────────────
    private void ApplyRegion()
    {
        Region?.Dispose();
        Region = new Region(RoundPath(new Rectangle(0, 0, Width, Height), R));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Fundo sólido (opaco dentro da region arredondada)
        using (var bg = new SolidBrush(ColBg))
            g.FillPath(bg, RoundPath(new Rectangle(0, 0, Width, Height), R));

        // Glow externo
        using (var glow = new Pen(ColGlow, 3f))
            g.DrawPath(glow, RoundPath(new Rectangle(-1, -1, Width + 1, Height + 1), R + 2));

        // Borda principal
        using (var pen = new Pen(ColBorder, 1.5f))
            g.DrawPath(pen, RoundPath(new Rectangle(1, 1, Width - 3, Height - 3), R));

        // Divisores verticais entre slots
        if (_slots.Count > 1)
        {
            using var div = new Pen(ColDivider, 1f);
            float colW = (float)Width / _slots.Count;
            for (int i = 1; i < _slots.Count; i++)
                g.DrawLine(div, colW * i, 10, colW * i, Height - 10);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Drag + Resize nativo
    // ─────────────────────────────────────────────────────────────────────────
    private void OnDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            var c  = PointToClient(Cursor.Position);
            int x  = c.X, y = c.Y, w = Width, h = Height;
            bool L = x <= B, Ri = x >= w - B, T = y <= B, Bo = y >= h - B;

            if (T && L)  { m.Result = (IntPtr)HT_TOPLEFT;     return; }
            if (T && Ri) { m.Result = (IntPtr)HT_TOPRIGHT;    return; }
            if (Bo && L) { m.Result = (IntPtr)HT_BOTTOMLEFT;  return; }
            if (Bo && Ri){ m.Result = (IntPtr)HT_BOTTOMRIGHT; return; }
            if (T)       { m.Result = (IntPtr)HT_TOP;         return; }
            if (Bo)      { m.Result = (IntPtr)HT_BOTTOM;      return; }
            if (L)       { m.Result = (IntPtr)HT_LEFT;        return; }
            if (Ri)      { m.Result = (IntPtr)HT_RIGHT;       return; }
            return;
        }
        base.WndProc(ref m);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Fechar = esconder (overlay persiste enquanto o app estiver aberto)
    // ─────────────────────────────────────────────────────────────────────────
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        { e.Cancel = true; Hide(); return; }
        _timer.Stop();
        base.OnFormClosing(e);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helper
    // ─────────────────────────────────────────────────────────────────────────
    private static System.Drawing.Drawing2D.GraphicsPath RoundPath(Rectangle r, int rad)
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

// ─────────────────────────────────────────────────────────────────────────────
//  SlotPanel  –  painel filho de um único macro dentro do overlay
//
//  Layout vertical centralizado:
//    [dot]  NomeBotão          ← topo
//    13.2 CPS                  ← centro grande
//    75 ms                     ← rodapé muted
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class SlotPanel : Panel
{
    private readonly EngineSlot _slot;
    private readonly Color      _activeColor;
    private readonly Color      _idleColor;

    private readonly Panel _dot;
    private readonly Label _lblName;
    private readonly Label _lblCps;
    private readonly Label _lblMs;

    public SlotPanel(EngineSlot slot, Color active, Color idle, Color nameColor, Color mutedColor)
    {
        _slot        = slot;
        _activeColor = active;
        _idleColor   = idle;
        BackColor    = Color.Transparent;
        Padding      = new Padding(12, 6, 12, 6);
        SetDoubleBuffered(this);

        // Dot de status
        _dot = new Panel { Size = new Size(8, 8), BackColor = idle };
        var dp = new System.Drawing.Drawing2D.GraphicsPath();
        dp.AddEllipse(0, 0, 8, 8);
        _dot.Region = new Region(dp);
        Controls.Add(_dot);

        // Nome do botão
        _lblName = new Label
        {
            Text      = Friendly(slot.MacroKey),
            Font      = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = nameColor,
            AutoSize  = false,
            Height    = 16,
            TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(_lblName);

        // CPS — grande, o valor principal
        _lblCps = new Label
        {
            Text      = "0.0 CPS",
            Font      = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = idle,
            AutoSize  = false,
            Height    = 30,
            TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(_lblCps);

        // Intervalo em ms
        _lblMs = new Label
        {
            Text      = $"{slot.IntervalMs} ms",
            Font      = new Font("Segoe UI", 7.5f),
            ForeColor = mutedColor,
            AutoSize  = false,
            Height    = 16,
            TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(_lblMs);

        Resize += (_, _) => ArrangeChildren();
        ArrangeChildren();
    }

    private void ArrangeChildren()
    {
        int px   = Padding.Left;
        int w    = Math.Max(1, Width - Padding.Left - Padding.Right);
        int midY = Height / 2;

        _dot.Location    = new Point(px, midY - 18);
        _lblName.Bounds  = new Rectangle(px + 12, midY - 20, w - 12, 16);
        _lblCps.Bounds   = new Rectangle(px,       midY - 4,  w,      30);
        _lblMs.Bounds    = new Rectangle(px,       midY + 28, w,      16);
    }

    /// <summary>Atualiza os valores exibidos a partir do slot. Chamado pelo timer.</summary>
    public void UpdateValues()
    {
        double cps    = _slot.RealCps;
        bool   active = _slot.State == ZeusAuto.Engine.Core.MacroState.Running;
        var    col    = active ? _activeColor : _idleColor;

        _lblCps.Text      = $"{cps:F1} CPS";
        _lblCps.ForeColor = col;
        _lblMs.Text       = $"{_slot.IntervalMs} ms";
        _dot.BackColor    = col;
    }

    private static void SetDoubleBuffered(Control c)
    {
        typeof(Control)
            .GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(c, true);
    }

    private static string Friendly(string key) =>
        key.Trim().ToUpperInvariant() switch
        {
            "MOUSELEFT"   or "TECLA ESQUERDA" => "Esquerdo",
            "MOUSERIGHT"  or "TECLA DIREITA"  => "Direito",
            "MOUSEMIDDLE" or "TECLA SCROLL"   => "Scroll",
            "MOUSEX1"     or "TECLA XBUTTON4" => "X1",
            "MOUSEX2"     or "TECLA XBUTTON5" => "X2",
            _                                 => key
        };
}