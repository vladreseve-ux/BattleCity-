using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace BattleCity;

/// <summary>Игровое поле: рисует пиксель-арт спрайты и эффекты без внешних картинок.</summary>
public sealed class BoardView : FrameworkElement
{
    sealed class Fx(int x, int y, bool big) { public int X = x, Y = y; public bool Big = big; public double Progress; }

    readonly List<Fx> effects = [];

    public GameEngine? Game { get; set; }
    public string Banner { get; set; } = "";
    public string SubBanner { get; set; } = "";

    const int N = GameEngine.MapSize;
    double Cell => ActualWidth / N;

    // ---------- кисти и спрайты ----------
    static SolidColorBrush B(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    static readonly Brush Ground = B("#050607");
    static readonly Brush GridLine = B("#0E1215");
    static readonly Brush Gold = B("#F2C94C");
    static readonly Brush Light = B("#E8E1C8");
    static readonly Brush ShotPlayer = B("#FFF3B0");
    static readonly Brush ShotEnemy = B("#FF8E7A");

    static readonly string[] TankArt =
    {
        ".......dd.......", ".......dd.......", ".ttt...dd...ttt.", ".TTTaaaddaaaTTT.",
        ".tttaaaddaaattt.", ".TTTaaccccaaTTT.", ".tttaccccccattt.", ".TTTaccccccaTTT.",
        ".tttaccccccattt.", ".TTTaccccccaTTT.", ".tttaaccccaattt.", ".TTTaaaaaaaaTTT.",
        ".tttaaaaaaaattt.", ".TTTaaaaaaaaTTT.", ".tttaaaaaaaattt.", ".TTT........TTT."
    };

    static readonly string[] EagleArt =
    {
        "................", ".......gg.......", "......gggg......", "..g..gggggg..g..",
        ".gg.gggggggg.gg.", ".gggggggggggggg.", "gggggggggggggggg", "ggoggoggggoggogg",
        ".gggoggggggoggg.", ".gggggggggggggg.", "..gggggggggggg..", "...gggggggggg...",
        "....gggggggg....", "...gg.gggg.gg...", "..gg..gggg..gg..", "................"
    };

    static Dictionary<char, Brush> Pal(string a, string c, string d, string t, string tt) => new()
    {
        ['a'] = B(a), ['c'] = B(c), ['d'] = B(d), ['t'] = B(t), ['T'] = B(tt)
    };

    static Drawing Sprite(string[] rows, IReadOnlyDictionary<char, Brush> pal)
    {
        var g = new DrawingGroup();
        RenderOptions.SetEdgeMode(g, EdgeMode.Aliased);
        using (var dc = g.Open())
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                    if (pal.TryGetValue(rows[r][c], out var brush))
                        dc.DrawRectangle(brush, null, new Rect(c, r, 1.02, 1.02));
        g.Freeze();
        return g;
    }

    static Drawing Build(Action<DrawingContext> draw)
    {
        var g = new DrawingGroup();
        RenderOptions.SetEdgeMode(g, EdgeMode.Aliased);
        using (var dc = g.Open()) draw(dc);
        g.Freeze();
        return g;
    }

    static void R(DrawingContext dc, Brush b, double x, double y, double w, double h) =>
        dc.DrawRectangle(b, null, new Rect(x, y, w, h));

    static readonly Drawing PlayerTank0 = Sprite(TankArt, Pal("#E3B431", "#F8E58A", "#7A5A10", "#4A3B0C", "#6B561A"));
    static readonly Drawing PlayerTank1 = Sprite(TankArt, Pal("#E3B431", "#F8E58A", "#7A5A10", "#6B561A", "#4A3B0C"));
    static readonly Drawing PlayerHurt0 = Sprite(TankArt, Pal("#D9822B", "#F5BE85", "#6E3A0C", "#4A2A0C", "#6B3F1A"));
    static readonly Drawing PlayerHurt1 = Sprite(TankArt, Pal("#D9822B", "#F5BE85", "#6E3A0C", "#6B3F1A", "#4A2A0C"));
    static readonly Drawing EnemyTank0 = Sprite(TankArt, Pal("#AEB7BE", "#E9EEF2", "#5D656B", "#2F3438", "#4A5056"));
    static readonly Drawing EnemyTank1 = Sprite(TankArt, Pal("#AEB7BE", "#E9EEF2", "#5D656B", "#4A5056", "#2F3438"));
    static readonly Drawing EnemyHurt0 = Sprite(TankArt, Pal("#C4553F", "#F09A86", "#6E2518", "#3A1C16", "#55302A"));
    static readonly Drawing EnemyHurt1 = Sprite(TankArt, Pal("#C4553F", "#F09A86", "#6E2518", "#55302A", "#3A1C16"));

    static readonly Drawing Eagle = Build(dc =>
    {
        R(dc, B("#1C2126"), 0, 0, 16, 16);
        R(dc, B("#3A434A"), 0, 0, 16, 1); R(dc, B("#3A434A"), 0, 0, 1, 16);
        dc.DrawDrawing(Sprite(EagleArt, new Dictionary<char, Brush> { ['g'] = B("#F2C94C"), ['o'] = B("#A8741A") }));
    });

    static readonly Drawing EagleDead = Build(dc =>
    {
        R(dc, B("#1C2126"), 0, 0, 16, 16);
        var rubble = B("#4B5359");
        R(dc, rubble, 2, 10, 5, 4); R(dc, rubble, 8, 8, 6, 6); R(dc, rubble, 5, 6, 3, 3);
        R(dc, B("#2B3136"), 3, 11, 2, 2); R(dc, B("#2B3136"), 10, 10, 3, 3);
    });

    static readonly Drawing Brick = Build(dc =>
    {
        R(dc, B("#B34A2B"), 0, 0, 16, 16);
        for (int i = 0; i < 4; i++)
        {
            R(dc, B("#D06A40"), 0, i * 4, 16, 1);        // светлый верх кирпича
            R(dc, B("#3A1C12"), 0, i * 4 + 3, 16, 1);    // шов
            if (i % 2 == 0) R(dc, B("#3A1C12"), 7, i * 4, 1, 3);
            else { R(dc, B("#3A1C12"), 3, i * 4, 1, 3); R(dc, B("#3A1C12"), 11, i * 4, 1, 3); }
        }
    });

    static readonly Drawing BrickDamaged1 = Build(dc =>
    {
        dc.DrawDrawing(Brick);
        var hole = B("#050607");
        var crack = B("#24130D");
        R(dc, hole, 0, 0, 3, 3);
        R(dc, hole, 13, 12, 3, 4);
        R(dc, crack, 7, 4, 4, 2);
        R(dc, crack, 5, 8, 6, 2);
    });

    static readonly Drawing BrickDamaged2 = Build(dc =>
    {
        dc.DrawDrawing(Brick);
        var hole = B("#050607");
        var crack = B("#24130D");
        R(dc, hole, 0, 0, 6, 4);
        R(dc, hole, 11, 0, 5, 5);
        R(dc, hole, 4, 5, 7, 5);
        R(dc, hole, 10, 10, 6, 6);
        R(dc, hole, 0, 12, 4, 4);
        R(dc, crack, 6, 10, 4, 3);
    });

    static Drawing GetWallSprite(Wall wall)
    {
        if (wall.IsConcrete) return Concrete;
        if (wall.Hp >= 3) return Brick;
        if (wall.Hp == 2) return BrickDamaged1;
        return BrickDamaged2;
    }

    static readonly Drawing Concrete = Build(dc =>
    {
        R(dc, B("#8E979C"), 0, 0, 16, 16);
        R(dc, B("#D7DDE0"), 0, 0, 16, 1); R(dc, B("#D7DDE0"), 0, 0, 1, 16);
        R(dc, B("#4F575C"), 0, 15, 16, 1); R(dc, B("#4F575C"), 15, 0, 1, 16);
        R(dc, B("#A9B2B7"), 3, 3, 10, 10);
        R(dc, B("#6F797E"), 3, 12, 10, 1); R(dc, B("#6F797E"), 12, 3, 1, 10);
        var rivet = B("#EEF2F4");
        R(dc, rivet, 1, 1, 2, 2); R(dc, rivet, 13, 1, 2, 2); R(dc, rivet, 1, 13, 2, 2); R(dc, rivet, 13, 13, 2, 2);
    });

    // ---------- эффекты ----------
    public void AddImpact(Impact i) => effects.Add(new Fx(i.X, i.Y, i.Big));
    public void ClearEffects() => effects.Clear();

    public void AdvanceEffects(double dt)
    {
        foreach (var f in effects) f.Progress += dt * 3.5;
        effects.RemoveAll(f => f.Progress >= 1.0);
    }

    public void Advance()
    {
        AdvanceEffects(0.125);
    }

    // ---------- отрисовка ----------
    void DrawSprite(DrawingContext dc, Drawing d, double gx, double gy, Direction? dir = null)
    {
        double cs = Cell;
        dc.PushTransform(new TranslateTransform(gx * cs, gy * cs));
        dc.PushTransform(new ScaleTransform(cs / 16, cs / 16));
        if (dir.HasValue)
        {
            double angle = dir.Value switch { Direction.Up => 0, Direction.Right => 90, Direction.Down => 180, _ => 270 };
            dc.PushTransform(new RotateTransform(angle, 8, 8));
            dc.DrawDrawing(d);
            dc.Pop();
        }
        else dc.DrawDrawing(d);
        dc.Pop();
        dc.Pop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, cs = Cell;
        dc.DrawRectangle(Ground, null, new Rect(0, 0, w, h));
        var pen = new Pen(GridLine, 1); pen.Freeze();
        for (int i = 1; i < N; i++)
        {
            dc.DrawLine(pen, new Point(i * cs, 0), new Point(i * cs, h));
            dc.DrawLine(pen, new Point(0, i * cs), new Point(w, i * cs));
        }

        var g = Game;
        if (g != null)
        {
            DrawSprite(dc, g.Base.Hp > 0 ? Eagle : EagleDead, g.Base.X, g.Base.Y);
            foreach (var wall in g.Walls) DrawSprite(dc, GetWallSprite(wall), wall.X, wall.Y);
            foreach (var t in g.Tanks)
            {
                if (t.Hp <= 0) continue;
                bool player = t.Team == GameEngine.PlayerTeam;
                bool isMoving = Math.Abs(t.X - t.VisualX) > 0.02 || Math.Abs(t.Y - t.VisualY) > 0.02;
                bool alt = isMoving && (int)Math.Floor((t.VisualX + t.VisualY) * 4.0) % 2 != 0;

                Drawing sprite;
                if (player)
                    sprite = t.Hp > 1 ? (alt ? PlayerTank1 : PlayerTank0) : (alt ? PlayerHurt1 : PlayerHurt0);
                else
                    sprite = t.Hp > 1 ? (alt ? EnemyTank1 : EnemyTank0) : (alt ? EnemyHurt1 : EnemyHurt0);

                DrawSprite(dc, sprite, t.VisualX, t.VisualY, t.Direction);
            }
            foreach (var p in g.Projectiles)
            {
                double cx = (p.VisualX + 0.5) * cs, cy = (p.VisualY + 0.5) * cs;
                var brush = p.Team == GameEngine.PlayerTeam ? ShotPlayer : ShotEnemy;
                dc.DrawRectangle(brush, null, new Rect(cx - 4, cy - 4, 8, 8));
                dc.DrawRectangle(Brushes.White, null, new Rect(cx - 2, cy - 2, 4, 4));
            }
        }

        foreach (var fx in effects)
        {
            double t = Math.Min(1.0, fx.Progress);
            double r = cs * (fx.Big ? 0.8 : 0.42) * (0.35 + 0.65 * t);
            byte a = (byte)(255 * (1.0 - t));
            var c = new Point((fx.X + 0.5) * cs, (fx.Y + 0.5) * cs);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, 255, 140, 30)), null, c, r, r);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, 255, 240, 170)), null, c, r * 0.55, r * 0.55);
        }

        if (!string.IsNullOrEmpty(Banner))
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, new Rect(0, 0, w, h));
            DrawText(dc, Banner, 46, Gold, h / 2 - 50);
            if (!string.IsNullOrEmpty(SubBanner)) DrawText(dc, SubBanner, 17, Light, h / 2 + 18);
        }
    }

    void DrawText(DrawingContext dc, string text, double size, Brush brush, double y)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = ActualWidth,
            TextAlignment = TextAlignment.Center
        };
        ft.SetFontWeight(FontWeights.Bold);
        dc.DrawText(ft, new Point(0, y));
    }
}
