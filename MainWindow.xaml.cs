using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace BattleCity;

public partial class MainWindow : Window
{
    readonly string saveFile = System.IO.Path.Combine(AppContext.BaseDirectory, "save.json");
    readonly IStateStore store = new JsonStateStore();
    readonly System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
    readonly List<Key> held = [];

    GameEngine game = new();
    bool paused;
    double lastTime;
    double stepAccumulator;
    int frame, bannerUntil = 20, toastUntil, knownLevel = 1;

    const double StepInterval = 0.125; // 125 мс — один логический такт
    const double PlayerSpeed = 3.8;    // клеток в секунду для игрока (~0.26 с на клетку)
    const double EnemySpeed = 2.2;     // клеток в секунду для врагов (~0.45 с на клетку)
    const double BulletSpeed = 8.0;    // клеток в секунду для снарядов (1 клетка за такт)

    public MainWindow()
    {
        InitializeComponent();
        TryLoadOnStart();
        lastTime = stopwatch.Elapsed.TotalSeconds;
        System.Windows.Media.CompositionTarget.Rendering += OnRenderFrame;
        RefreshUI();
    }

    // ---------- запуск / выход ----------
    void TryLoadOnStart()
    {
        try
        {
            if (!File.Exists(saveFile)) return;
            var loaded = store.Load(saveFile);
            if (loaded.GameOver || loaded.Won) return;
            game = loaded;
            knownLevel = game.Level;
            paused = true; // даём оглядеться после загрузки
            Toast("Игра восстановлена из save.json");
        }
        catch { /* битое сохранение — просто начинаем новую игру */ }
    }

    void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        System.Windows.Media.CompositionTarget.Rendering -= OnRenderFrame;
        try
        {
            if (game.GameOver || game.Won) File.Delete(saveFile);
            else store.Save(game, saveFile);
        }
        catch { }
    }

    void Window_Deactivated(object? sender, EventArgs e)
    {
        held.Clear();
        if (!game.GameOver && !game.Won) paused = true;
        RefreshUI();
    }

    // ---------- ввод ----------
    static Direction? KeyToDirection(Key k) => k switch
    {
        Key.Up or Key.W => Direction.Up,
        Key.Down or Key.S => Direction.Down,
        Key.Left or Key.A => Direction.Left,
        Key.Right or Key.D => Direction.Right,
        _ => null
    };

    Direction? HeldDirection()
    {
        for (int i = held.Count - 1; i >= 0; i--)
        {
            var d = KeyToDirection(held[i]);
            if (d.HasValue) return d;
        }
        return null;
    }

    Tank? Player() => game.Tanks.FirstOrDefault(t => t.Team == GameEngine.PlayerTeam && t.Hp > 0);
    bool CanAct() => !paused && !game.GameOver;

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.P)
        {
            if (!e.IsRepeat && !game.GameOver && !game.Won)
            {
                paused = !paused;
                if (!paused) lastTime = stopwatch.Elapsed.TotalSeconds;
            }
            e.Handled = true; RefreshUI(); return;
        }
        if (e.Key == Key.N)
        {
            if (!e.IsRepeat) NewGame();
            e.Handled = true; return;
        }
        var dir = KeyToDirection(e.Key);
        if (dir.HasValue)
        {
            if (!e.IsRepeat)
            {
                held.Remove(e.Key); held.Add(e.Key);
                var player = Player();
                if (player != null && CanAct()) player.Direction = dir.Value;
            }
            e.Handled = true; return;
        }
        if (e.Key == Key.Space)
        {
            var player = Player();
            if (player != null && CanAct()) game.Fire(player);
            e.Handled = true;
        }
    }

    void Window_KeyUp(object sender, KeyEventArgs e) => held.Remove(e.Key);

    // ---------- игровой цикл и плавная отрисовка (60 FPS) ----------
    void OnRenderFrame(object? sender, EventArgs e)
    {
        double now = stopwatch.Elapsed.TotalSeconds;
        double dt = now - lastTime;
        lastTime = now;
        if (dt > 0.1) dt = 0.1; // защита от рывков при паузе или перетаскивании окна

        if (CanAct())
        {
            stepAccumulator += dt;
            while (stepAccumulator >= StepInterval)
            {
                stepAccumulator -= StepInterval;
                StepGame();
            }

            UpdatePlayerMovement();
        }
        else
        {
            stepAccumulator = 0;
        }

        UpdateVisuals(dt);
        RefreshUI();
    }

    void StepGame()
    {
        frame++;
        game.Step();
        if (frame % 4 == 0) game.ThinkEnemies();

        if (game.Level != knownLevel) { knownLevel = game.Level; bannerUntil = frame + 20; }
        foreach (var i in game.Impacts) Board.AddImpact(i);
        game.Impacts.Clear();
    }

    void UpdatePlayerMovement()
    {
        var player = Player();
        if (player == null) return;

        bool atTarget = Math.Abs(player.VisualX - player.X) < 0.001
                     && Math.Abs(player.VisualY - player.Y) < 0.001;

        if (atTarget)
        {
            var dir = HeldDirection();
            if (dir.HasValue)
            {
                player.Move(dir.Value, game);
            }
        }
    }

    void UpdateVisuals(double dt)
    {
        foreach (var t in game.Tanks)
        {
            double speed = t.Team == GameEngine.PlayerTeam ? PlayerSpeed : EnemySpeed;
            t.VisualX = MoveTowards(t.VisualX, t.X, speed * dt);
            t.VisualY = MoveTowards(t.VisualY, t.Y, speed * dt);
        }

        foreach (var p in game.Projectiles)
        {
            p.VisualX = MoveTowards(p.VisualX, p.X, BulletSpeed * dt);
            p.VisualY = MoveTowards(p.VisualY, p.Y, BulletSpeed * dt);
        }

        Board.AdvanceEffects(dt);
    }

    static double MoveTowards(double current, double target, double maxDelta)
    {
        double diff = target - current;
        if (Math.Abs(diff) <= maxDelta) return target;
        return current + Math.Sign(diff) * maxDelta;
    }

    void RefreshUI()
    {
        var p = Player();
        ScoreText.Text = game.Score.ToString("000000");
        LevelText.Text = $"{game.Level:00}/{GameEngine.MaxLevel:00}";
        LivesText.Text = "x " + Math.Max(game.Lives, 0);
        HpText.Text = p == null ? "—" : string.Join(" ", Enumerable.Repeat("■", Math.Max(p.Hp, 0)));
        EnemyText.Text = (game.Reserve + game.Tanks.Count(t => t.Team == GameEngine.EnemyTeam && t.Hp > 0)).ToString();
        if (frame > toastUntil) ToastText.Text = "";

        Board.Game = game;
        if (game.Won)
        {
            Board.Banner = "ПОБЕДА!";
            Board.SubBanner = $"Все {GameEngine.MaxLevel} уровня пройдены!\nСчёт: {game.Score}\nN — новая игра";
        }
        else if (game.GameOver)
        {
            Board.Banner = "GAME OVER";
            Board.SubBanner = $"Счёт: {game.Score}\nN — новая игра";
        }
        else if (paused)
        {
            Board.Banner = "ПАУЗА";
            Board.SubBanner = "P — продолжить";
        }
        else if (frame < bannerUntil)
        {
            Board.Banner = game.Level == GameEngine.MaxLevel ? "ФИНАЛ!" : $"УРОВЕНЬ {game.Level}";
            Board.SubBanner = game.Level == GameEngine.MaxLevel ? "Уничтожьте всех врагов для победы!" : $"Уровень {game.Level} из {GameEngine.MaxLevel}";
        }
        else
        {
            Board.Banner = "";
            Board.SubBanner = "";
        }
        Board.InvalidateVisual();
    }

    void Toast(string text) { ToastText.Text = text; toastUntil = frame + 25; }

    // ---------- кнопки ----------
    void NewGame()
    {
        game = new GameEngine();
        paused = false;
        held.Clear();
        knownLevel = 1;
        bannerUntil = frame + 20;
        stepAccumulator = 0;
        lastTime = stopwatch.Elapsed.TotalSeconds;
        Board.ClearEffects();
        RefreshUI();
    }

    void NewGame_Click(object sender, RoutedEventArgs e) => NewGame();

    void Save_Click(object sender, RoutedEventArgs e)
    {
        try { store.Save(game, saveFile); Toast("Игра сохранена"); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Ошибка сохранения"); }
    }

    void Load_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Сохранение Battle City (*.json)|*.json|Все файлы (*.*)|*.*",
            InitialDirectory = AppContext.BaseDirectory,
            FileName = File.Exists(saveFile) ? saveFile : ""
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            game = store.Load(dlg.FileName);
            knownLevel = game.Level;
            paused = true;
            held.Clear();
            stepAccumulator = 0;
            lastTime = stopwatch.Elapsed.TotalSeconds;
            Board.ClearEffects();
            Toast("Сохранение загружено");
            RefreshUI();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Не удалось загрузить"); }
    }
}
