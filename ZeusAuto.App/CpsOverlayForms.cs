using System.Runtime.InteropServices;

namespace ZeusAuto.App;

// ─────────────────────────────────────────────────────────────────────────────
//  CpsOverlayForm
//
//  Janela flutuante TopMost, separada do MainForm.
//  Exibe um painel filho por macro ativo, lado a lado, cada um mostrando:
//    • Nome do botão
//    • CPS real com 1 casa decimal  (0.0 quando inativo)
//    • DoubleClickWindowMs configurado pelo usuário
//
//  Interação:
//    • Drag:   clicar e arrastar em qualquer área do conteúdo
//    • Resize: clicar e arrastar no gripper (canto inferior direito)
//              → apenas esse elemento aciona HT_BOTTOMRIGHT
//    • Nenhum WndProc customizado necessário
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CpsOverlayForm : Form
{
    // ── Win32 ────────────────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern int  SendMessage(IntPtr hWnd, int msg, int wp, int lp);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION       = 0x2;
    private const int HT_BOTTOMRIGHT   = 17;

    private bool _isForceHiddenByEmpty = false;

    // ── Paleta ───────────────────────────────────────────────────────────────
    private static readonly Color ColBg      = Color.FromArgb(18, 20, 28);
    private static readonly Color ColBorder  = Color.FromArgb(70, 110, 255);
    private static readonly Color ColGlow    = Color.FromArgb(25, 110, 255);
    private static readonly Color ColActive  = Color.FromArgb(60, 225, 140);
    private static readonly Color ColIdle    = Color.FromArgb(120, 120, 140);
    private static readonly Color ColName    = Color.FromArgb(240, 242, 255);
    private static readonly Color ColMuted   = Color.FromArgb(140, 145, 165);
    private const int R = 12; // corner radius

    // ── Layout ───────────────────────────────────────────────────────────────
    private readonly TableLayoutPanel _table = new();
    private readonly Panel            _gripper;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };

    private IReadOnlyList<EngineSlot> _slots = Array.Empty<EngineSlot>();

    // Altura base usada como referência para o fator de escala das fontes
    private const int BaseHeight = 110;

    // ─────────────────────────────────────────────────────────────────────────
    public CpsOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        BackColor       = Color.FromArgb(13, 13, 20);
        TopMost         = true;
        DoubleBuffered  = true;
        Opacity         = 0.92;
        ShowInTaskbar   = false;
        Width           = 220;
        Height          = 110;
        MinimumSize     = new Size(140, 100);
        StartPosition   = FormStartPosition.Manual;

        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(wa.Right - Width - 20, wa.Top + 20);

        // ── Tabela de slots ──────────────────────────────────────────────────
        _table.Dock      = DockStyle.Fill;
        _table.BackColor = Color.Transparent;
        Controls.Add(_table);

        // ── Gripper de resize (canto inferior direito, 24×24 px) ────────────
        _gripper = new Panel
        {
            Size      = new Size(24, 24),
            BackColor = Color.Transparent,
            Cursor    = Cursors.SizeNWSE,
        };
        _gripper.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_BOTTOMRIGHT, 0); }
        };
        Controls.Add(_gripper);
        Controls.SetChildIndex(_gripper, 0); // fica na frente

        // ── Drag: arrastar pelo form/table ───────────────────────────────────
        MouseDown        += OnDrag;
        _table.MouseDown += OnDrag;

        Resize += (_, _) => { PositionGripper(); ApplyRegion(); ScaleSlotFonts(); Invalidate(); };
        ApplyRegion();
        PositionGripper();

        _timer.Tick += (_, _) => RefreshSlots();
        _timer.Start();
    }

    private void PositionGripper() =>
        _gripper.Location = new Point(Width - _gripper.Width, Height - _gripper.Height);

    // ─────────────────────────────────────────────────────────────────────────
    //  Drag pelo conteúdo
    // ─────────────────────────────────────────────────────────────────────────
    private void OnDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); }
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
    /// </summary>
    public void ApplyVisibility(bool visible)
    {
        if (InvokeRequired) { Invoke(() => ApplyVisibility(visible)); return; }

        if (_isForceHiddenByEmpty) { if (Visible) Hide(); return; }
        if (visible) Show(); else Hide();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Painéis filhos lado a lado
    // ─────────────────────────────────────────────────────────────────────────
    private void RebuildPanels()
    {
        _table.SuspendLayout();

        foreach (Control c in _table.Controls.Cast<Control>().ToList())
            c.Dispose();

        _table.Controls.Clear();
        _table.ColumnStyles.Clear();
        _table.RowStyles.Clear();
        _table.RowCount    = 1;
        _table.ColumnCount = 0;
        _table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        int count = _slots.Count;

        if (count == 0)
        {
            _isForceHiddenByEmpty = true;
            _slots = Array.Empty<EngineSlot>();
            if (Visible) Hide();
            _table.ResumeLayout();
            return;
        }

        if (_isForceHiddenByEmpty)
        {
            _isForceHiddenByEmpty = false;
            if (!Visible) Show();
        }

        _table.ColumnCount = count;
        float pct = 100f / count;

        for (int i = 0; i < count; i++)
        {
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, pct));

            var sp = new SlotPanel(_slots[i], ColActive, ColIdle, ColName, ColMuted);
            sp.Dock      = DockStyle.Fill;
            sp.Margin    = new Padding(6);
            sp.MouseDown += OnDrag; // arrastar pelo conteúdo do slot também move a janela

            _table.Controls.Add(sp, i, 0);
        }

        Width  = Math.Clamp(count * 190, 220, 900);
        Height = 110;

        _table.ResumeLayout();
        ApplyRegion();
        PositionGripper();
        Invalidate();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Escala de fontes ao redimensionar
    // ─────────────────────────────────────────────────────────────────────────
    private void ScaleSlotFonts()
    {
        float factor = Math.Clamp((float)Height / BaseHeight, 0.5f, 3.0f);
        foreach (Control c in _table.Controls)
            if (c is SlotPanel sp) sp.ScaleFonts(factor);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Refresh (timer 100 ms)
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

        // Fundo
        using (var bg = new SolidBrush(ColBg))
            g.FillPath(bg, RoundPath(new Rectangle(0, 0, Width, Height), R));

        // Glow
        using (var glow = new Pen(Color.FromArgb(20, ColGlow), 6f))
            g.DrawPath(glow, RoundPath(new Rectangle(-1, -1, Width + 1, Height + 1), R + 2));

        // Borda
        using (var pen = new Pen(ColBorder, 1.2f))
            g.DrawPath(pen, RoundPath(new Rectangle(1, 1, Width - 3, Height - 3), R));

        // Gripper visual (3 linhas diagonais no canto inferior direito)
        using var rp = new Pen(Color.FromArgb(90, ColBorder), 1f);
        g.DrawLine(rp, Width - 16, Height - 6,  Width - 6, Height - 16);
        g.DrawLine(rp, Width - 22, Height - 6,  Width - 6, Height - 22);
        g.DrawLine(rp, Width - 28, Height - 6,  Width - 6, Height - 28);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Fechar = esconder
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
//    140 ms                    ← rodapé (DoubleClickWindowMs)
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
        Padding      = new Padding(14, 10, 14, 10);
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
            Font      = new Font("Segoe UI Semibold", 8.5f),
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
            Font      = new Font("Segoe UI Semibold", 20f),
            ForeColor = idle,
            AutoSize  = false,
            Height    = 36,
            TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(_lblCps);

        // DoubleClickWindowMs configurado pelo usuário
        _lblMs = new Label
        {
            Text      = slot.DoubleClickWindowMs.HasValue ? $"{slot.DoubleClickWindowMs} ms" : "-- ms",
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

        int halfCps  = _lblCps.Height  / 2;

        _dot.Location   = new Point(px, midY - halfCps - _dot.Height - 2);
        _lblName.Bounds = new Rectangle(px + _dot.Width + 4, midY - halfCps - _lblName.Height - 2, w - _dot.Width - 4, _lblName.Height);
        _lblCps.Bounds  = new Rectangle(px, midY - halfCps, w, _lblCps.Height);
        _lblMs.Bounds   = new Rectangle(px, midY + halfCps + 2, w, _lblMs.Height);
    }

    /// <summary>Escala fontes proporcionalmente à altura da janela. Chamado pelo form ao redimensionar.</summary>
    public void ScaleFonts(float scaleFactor)
    {
        float cpsSize  = Math.Clamp(20f  * scaleFactor, 9f, 48f);
        float nameSize = Math.Clamp(8.5f * scaleFactor, 5f, 20f);
        float msSize   = Math.Clamp(7.5f * scaleFactor, 5f, 18f);

        _lblCps.Font  = new Font("Segoe UI Semibold", cpsSize);
        _lblName.Font = new Font("Segoe UI Semibold", nameSize);
        _lblMs.Font   = new Font("Segoe UI",          msSize);

        _lblCps.Height  = (int)(36 * scaleFactor);
        _lblName.Height = (int)(16 * scaleFactor);
        _lblMs.Height   = (int)(16 * scaleFactor);

        int dotSize = Math.Clamp((int)(8 * scaleFactor), 5, 16);
        _dot.Size = new Size(dotSize, dotSize);
        var dp = new System.Drawing.Drawing2D.GraphicsPath();
        dp.AddEllipse(0, 0, dotSize, dotSize);
        _dot.Region = new Region(dp);

        ArrangeChildren();
    }

    /// <summary>Atualiza os valores exibidos a partir do slot. Chamado pelo timer.</summary>
    public void UpdateValues()
    {
        double cps    = _slot.RealCps;
        bool   active = _slot.State == ZeusAuto.Engine.Core.MacroState.Running;
        var    col    = active ? _activeColor : _idleColor;

        double truncated  = Math.Truncate(cps * 10.0) / 10.0;
        _lblCps.Text      = active ? $"{truncated:F1} CPS" : "0.0 CPS";
        _lblCps.ForeColor = col;
        _lblMs.Text       = _slot.DoubleClickWindowMs.HasValue ? $"{_slot.DoubleClickWindowMs} ms" : "-- ms";
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
