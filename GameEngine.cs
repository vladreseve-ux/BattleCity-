using System.IO;
using System.Text.Json;

namespace BattleCity;

/// <summary>Направление движения на сетке.</summary>
public enum Direction { Up, Down, Left, Right }

/// <summary>Объект, занимающий клетку игровой сетки.</summary>
public interface IGridEntity { int X { get; } int Y { get; } }

/// <summary>Объект, которому можно нанести урон.</summary>
public interface IDamageable { void TakeDamage(int amount = 1); }

/// <summary>Абстракция сохранения и загрузки игры.</summary>
public interface IStateStore { void Save(GameEngine game, string path); GameEngine Load(string path); }

/// <summary>Попадание снаряда (для визуальных эффектов): клетка и признак «большого» взрыва.</summary>
public readonly record struct Impact(int X, int Y, bool Big);

/// <summary>Стена: кирпичная разрушается, бетонная неуязвима.</summary>
public sealed class Wall(int x, int y, bool isConcrete = false, int hp = 1) : IGridEntity, IDamageable
{
    public int X { get; set; } = x;
    public int Y { get; set; } = y;
    public bool IsConcrete { get; set; } = isConcrete;
    public int Hp { get; set; } = hp;
    public void TakeDamage(int amount = 1) { if (!IsConcrete) Hp -= amount; }
}

/// <summary>База (орёл): попадание в неё завершает игру.</summary>
public sealed class Base(int x, int y, int hp = 1) : IGridEntity, IDamageable
{
    public int X { get; set; } = x;
    public int Y { get; set; } = y;
    public int Hp { get; set; } = hp;
    public void TakeDamage(int amount = 1) => Hp -= amount;
}

/// <summary>Танк игрока или врага.</summary>
public sealed class Tank(int x, int y, Direction direction, string team, int hp = 2) : IGridEntity, IDamageable
{
    public int X { get; set; } = x;
    public int Y { get; set; } = y;
    [System.Text.Json.Serialization.JsonIgnore]
    public double VisualX { get; set; } = x;
    [System.Text.Json.Serialization.JsonIgnore]
    public double VisualY { get; set; } = y;
    public Direction Direction { get; set; } = direction;
    public string Team { get; set; } = team;
    public int Hp { get; set; } = hp;
    /// <summary>Тактов до следующего выстрела.</summary>
    public int Cooldown { get; set; }

    public void TakeDamage(int amount = 1) => Hp -= amount;

    /// <summary>Поворачивает танк и сдвигает его на клетку, если она свободна.</summary>
    public bool Move(Direction d, GameEngine g)
    {
        Direction = d;
        var (dx, dy) = Vector(d);
        if (g.IsBlocked(X + dx, Y + dy, this)) return false;
        X += dx; Y += dy;
        return true;
    }

    /// <summary>Создаёт снаряд в клетке перед стволом.</summary>
    public Projectile Shoot()
    {
        var (dx, dy) = Vector(Direction);
        return new Projectile(X + dx, Y + dy, Direction, Team)
        {
            VisualX = VisualX + dx * 0.4,
            VisualY = VisualY + dy * 0.4
        };
    }

    internal static (int X, int Y) Vector(Direction d) => d switch
    {
        Direction.Up => (0, -1),
        Direction.Down => (0, 1),
        Direction.Left => (-1, 0),
        _ => (1, 0)
    };
}

/// <summary>Снаряд в полёте.</summary>
public sealed class Projectile(int x, int y, Direction direction, string team) : IGridEntity
{
    public int X { get; set; } = x;
    public int Y { get; set; } = y;
    [System.Text.Json.Serialization.JsonIgnore]
    public double VisualX { get; set; } = x;
    [System.Text.Json.Serialization.JsonIgnore]
    public double VisualY { get; set; } = y;
    public Direction Direction { get; set; } = direction;
    public string Team { get; set; } = team;
}

/// <summary>Игровой мир и правила игры.</summary>
public sealed class GameEngine
{
    /// <summary>Размер карты (клеток по каждой стороне).</summary>
    public const int MapSize = 10;
    public const string PlayerTeam = "player";
    public const string EnemyTeam = "enemy";
    /// <summary>Максимум вражеских танков на поле одновременно.</summary>
    public const int MaxEnemiesOnField = 3;
    /// <summary>Количество уровней для полной победы в игре.</summary>
    public const int MaxLevel = 3;
    /// <summary>Прочность кирпичной стены по умолчанию (выдерживает несколько попаданий).</summary>
    public const int BrickDefaultHp = 3;

    const int PlayerSpawnX = 3, PlayerSpawnY = 9;
    static readonly (int X, int Y)[] EnemySpawns = [(0, 0), (9, 0), (4, 0)];

    /// <summary>Создаёт новую игру: карта, база, танк игрока и два врага на поле.</summary>
    public GameEngine()
    {
        Walls = DefaultWalls();
        Base = new Base(5, 9);
        Tanks =
        [
            new Tank(1, 1, Direction.Down, PlayerTeam),
            new Tank(8, 1, Direction.Left, EnemyTeam),
            new Tank(1, 7, Direction.Up, EnemyTeam)
        ];
        Projectiles = [];
    }

    public List<Wall> Walls { get; set; }
    public Base Base { get; set; }
    public List<Tank> Tanks { get; set; }
    public List<Projectile> Projectiles { get; set; }
    public int Score { get; set; }
    public bool GameOver { get; set; }
    /// <summary>Флаг победы: все уровни успешно пройдены.</summary>
    public bool Won { get; set; }
    /// <summary>Оставшиеся жизни игрока.</summary>
    public int Lives { get; set; } = 3;
    /// <summary>Номер уровня.</summary>
    public int Level { get; set; } = 1;
    /// <summary>Сколько вражеских танков ещё не выехало на поле.</summary>
    public int Reserve { get; set; } = 4;
    /// <summary>Попадания за последние такты; интерфейс забирает их для эффектов.</summary>
    public List<Impact> Impacts { get; } = [];

    static List<Wall> DefaultWalls() =>
    [
        new(3, 3, false, BrickDefaultHp), new(4, 3, false, BrickDefaultHp),
        new(2, 5, false, BrickDefaultHp), new(7, 4, false, BrickDefaultHp),
        new(3, 7, false, BrickDefaultHp), new(8, 7, false, BrickDefaultHp),
        new(6, 6, true, 999), new(1, 4, true, 999), new(8, 5, true, 999),
        new(4, 8, false, BrickDefaultHp), new(5, 8, false, BrickDefaultHp), new(6, 8, false, BrickDefaultHp),
        new(4, 9, false, BrickDefaultHp), new(6, 9, false, BrickDefaultHp)
    ];

    /// <summary>Клетка занята границей, стеной, базой или танком.</summary>
    public bool IsBlocked(int x, int y, Tank? except = null) =>
        x < 0 || y < 0 || x >= MapSize || y >= MapSize
        || Walls.Any(w => w.X == x && w.Y == y)
        || (Base.Hp > 0 && Base.X == x && Base.Y == y)
        || Tanks.Any(t => t != except && t.Hp > 0 && t.X == x && t.Y == y);

    /// <summary>Выстрел танка. Возвращает false, если стрелять нельзя (перезарядка, игра окончена).</summary>
    public bool Fire(Tank tank)
    {
        if (GameOver || tank.Hp <= 0 || tank.Cooldown > 0) return false;
        tank.Cooldown = tank.Team == PlayerTeam ? 5 : 12;
        var shot = tank.Shoot();
        // Стартовая клетка проверяется сразу: стрельба в упор работает.
        if (!Resolve(shot)) Projectiles.Add(shot);
        return true;
    }

    /// <summary>Такт снарядов: перезарядка, полёт, попадания, столкновения снарядов.</summary>
    public void Step()
    {
        if (GameOver) return;
        foreach (var t in Tanks) if (t.Cooldown > 0) t.Cooldown--;

        var remaining = new List<Projectile>();
        var previous = new Dictionary<Projectile, (int X, int Y)>();
        foreach (var shot in Projectiles)
        {
            previous[shot] = (shot.X, shot.Y);
            var (dx, dy) = Tank.Vector(shot.Direction);
            shot.X += dx; shot.Y += dy;
            if (!Resolve(shot)) remaining.Add(shot);
        }

        // Встречные снаряды гасят друг друга (в одной клетке или «разминувшись»).
        var doomed = new HashSet<Projectile>();
        for (int i = 0; i < remaining.Count; i++)
            for (int j = i + 1; j < remaining.Count; j++)
            {
                var a = remaining[i]; var b = remaining[j];
                if (a.Team == b.Team) continue;
                bool sameCell = a.X == b.X && a.Y == b.Y;
                bool swapped = a.X == previous[b].X && a.Y == previous[b].Y
                               && b.X == previous[a].X && b.Y == previous[a].Y;
                if (sameCell || swapped)
                {
                    doomed.Add(a); doomed.Add(b);
                    Mark(a.X, a.Y, false);
                }
            }
        remaining.RemoveAll(doomed.Contains);
        Projectiles = remaining;
        Tanks.RemoveAll(t => t.Team == EnemyTeam && t.Hp <= 0);
    }

    /// <summary>Такт врагов: появление из резерва, движение, стрельба, переход на следующий уровень или победа.</summary>
    public void ThinkEnemies()
    {
        if (GameOver) return;
        if (Reserve == 0 && !Tanks.Any(t => t.Team == EnemyTeam && t.Hp > 0))
        {
            if (Level >= MaxLevel)
            {
                Won = true;
                GameOver = true;
                return;
            }
            NextLevel();
            return;
        }
        SpawnEnemy();
        var player = Tanks.FirstOrDefault(t => t.Team == PlayerTeam && t.Hp > 0);
        foreach (var e in Tanks.Where(t => t.Team == EnemyTeam && t.Hp > 0).ToList())
        {
            int roll = Random.Shared.Next(10);
            Direction dir;
            if (roll < 3) dir = (Direction)Random.Shared.Next(4);
            else if (roll < 6 && player != null) dir = Toward(e, player.X, player.Y);
            else dir = Toward(e, Base.X, Base.Y);

            if (!e.Move(dir, this))
            {
                // Упёрлись: пробиваем стену или выбираем другой путь.
                if (Random.Shared.Next(2) == 0) Fire(e);
                else e.Move((Direction)Random.Shared.Next(4), this);
            }
            else if (Random.Shared.Next(5) == 0) Fire(e);

            if ((player != null && Faces(e, player.X, player.Y)) || Faces(e, Base.X, Base.Y)) Fire(e);
        }
    }

    void SpawnEnemy()
    {
        if (Reserve <= 0) return;
        if (Tanks.Count(t => t.Team == EnemyTeam && t.Hp > 0) >= MaxEnemiesOnField) return;
        foreach (var (sx, sy) in EnemySpawns.OrderBy(_ => Random.Shared.Next()))
        {
            if (IsBlocked(sx, sy)) continue;
            Tanks.Add(new Tank(sx, sy, Direction.Down, EnemyTeam) { VisualX = sx, VisualY = sy });
            Reserve--;
            return;
        }
    }

    void NextLevel()
    {
        Level++;
        Walls = DefaultWalls();
        if (Base.Hp <= 0) Base.Hp = 1;
        Projectiles.Clear();
        Tanks.RemoveAll(t => t.Team == EnemyTeam);
        Reserve = Math.Min(4 + 2 * Level, 16);
        Lives = Math.Min(Lives + 1, 9);
        var player = Tanks.FirstOrDefault(t => t.Team == PlayerTeam);
        if (player != null) Respawn(player);
    }

    static void Respawn(Tank t)
    {
        t.Hp = 2; t.X = PlayerSpawnX; t.Y = PlayerSpawnY;
        t.VisualX = PlayerSpawnX; t.VisualY = PlayerSpawnY;
        t.Direction = Direction.Up; t.Cooldown = 0;
    }

    static Direction Toward(Tank e, int tx, int ty)
    {
        int dx = tx - e.X, dy = ty - e.Y;
        if (Math.Abs(dx) > Math.Abs(dy)) return dx > 0 ? Direction.Right : Direction.Left;
        return dy > 0 ? Direction.Down : Direction.Up;
    }

    static bool Faces(Tank e, int tx, int ty) => e.Direction switch
    {
        Direction.Up => e.X == tx && ty < e.Y,
        Direction.Down => e.X == tx && ty > e.Y,
        Direction.Left => e.Y == ty && tx < e.X,
        _ => e.Y == ty && tx > e.X
    };

    /// <summary>Обрабатывает попадание снаряда в его текущей клетке. true — снаряд израсходован.</summary>
    bool Resolve(Projectile s)
    {
        if (s.X < 0 || s.Y < 0 || s.X >= MapSize || s.Y >= MapSize) return true;

        var wall = Walls.FirstOrDefault(w => w.X == s.X && w.Y == s.Y);
        if (wall != null)
        {
            wall.TakeDamage();
            if (wall.Hp <= 0) Walls.Remove(wall);
            Mark(s.X, s.Y, false);
            return true;
        }
        if (Base.Hp > 0 && Base.X == s.X && Base.Y == s.Y)
        {
            Base.TakeDamage();
            GameOver = true;
            Mark(s.X, s.Y, true);
            return true;
        }
        var target = Tanks.FirstOrDefault(t => t.Team != s.Team && t.Hp > 0 && t.X == s.X && t.Y == s.Y);
        if (target != null)
        {
            target.TakeDamage();
            if (target.Hp <= 0) Destroy(target, s.Team);
            else Mark(s.X, s.Y, false);
            return true;
        }
        return false;
    }

    void Destroy(Tank target, string shooterTeam)
    {
        Mark(target.X, target.Y, true);
        if (target.Team == EnemyTeam)
        {
            if (shooterTeam == PlayerTeam) Score += 100;
            return;
        }
        Lives--;
        if (Lives <= 0) GameOver = true;
        else Respawn(target);
    }

    void Mark(int x, int y, bool big)
    {
        if (Impacts.Count < 256) Impacts.Add(new Impact(x, y, big));
    }

    /// <summary>Снимок состояния для сериализации.</summary>
    public GameState ToState() => new()
    {
        Walls = Walls, Base = Base, Tanks = Tanks, Projectiles = Projectiles,
        Score = Score, GameOver = GameOver, Won = Won, Lives = Lives, Level = Level, Reserve = Reserve
    };

    /// <summary>Восстанавливает игру из снимка.</summary>
    public static GameEngine FromState(GameState s)
    {
        var g = new GameEngine
        {
            Walls = s.Walls, Base = s.Base, Tanks = s.Tanks, Projectiles = s.Projectiles,
            Score = s.Score, GameOver = s.GameOver, Won = s.Won, Lives = s.Lives, Level = s.Level, Reserve = s.Reserve
        };
        foreach (var t in g.Tanks) { t.VisualX = t.X; t.VisualY = t.Y; }
        foreach (var p in g.Projectiles) { p.VisualX = p.X; p.VisualY = p.Y; }
        return g;
    }
}

/// <summary>DTO полного состояния игры для JSON.</summary>
public sealed class GameState
{
    public List<Wall> Walls { get; set; } = [];
    public Base Base { get; set; } = new(5, 9);
    public List<Tank> Tanks { get; set; } = [];
    public List<Projectile> Projectiles { get; set; } = [];
    public int Score { get; set; }
    public bool GameOver { get; set; }
    public bool Won { get; set; }
    public int Lives { get; set; } = 3;
    public int Level { get; set; } = 1;
    public int Reserve { get; set; } = 4;
}

/// <summary>Хранилище состояния в JSON-файле (запись через временный файл).</summary>
public sealed class JsonStateStore : IStateStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Save(GameEngine game, string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(game.ToState(), Options));
        File.Move(tmp, path, true);
    }

    public GameEngine Load(string path)
    {
        var state = JsonSerializer.Deserialize<GameState>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Файл сохранения пуст или повреждён.");
        if ((object?)state.Base is null || state.Tanks?.Any(t => t.Team == GameEngine.PlayerTeam) != true)
            throw new InvalidDataException("В сохранении нет танка игрока или базы.");
        return GameEngine.FromState(state);
    }
}
