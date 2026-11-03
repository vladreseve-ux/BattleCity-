using BattleCity;
using Xunit;

namespace BattleCity.Tests;

/// <summary>Проверяет правила игры, жизни, уровни и сохранение состояния.</summary>
public sealed class GameEngineTests
{
    static Tank PlayerOf(GameEngine g) => g.Tanks.First(t => t.Team == GameEngine.PlayerTeam);

    [Fact]
    public void TankCannotMoveThroughWall()
    {
        var game = new GameEngine { Walls = [new Wall(2, 1)] };
        var player = PlayerOf(game);
        Assert.False(player.Move(Direction.Right, game));
        Assert.Equal((1, 1), (player.X, player.Y));
    }

    [Fact]
    public void ProjectileDestroysBrickWall()
    {
        var game = new GameEngine { Walls = [new Wall(2, 1)] };
        game.Projectiles.Add(new Projectile(1, 1, Direction.Right, "player"));
        game.Step();
        Assert.Empty(game.Walls);
        Assert.Empty(game.Projectiles);
    }

    [Fact]
    public void BrickWallWithMultipleHpTakesMultipleHits()
    {
        var game = new GameEngine { Walls = [new Wall(2, 1, false, 3)] };
        game.Projectiles.Add(new Projectile(1, 1, Direction.Right, "player"));
        game.Step();
        Assert.Single(game.Walls);
        Assert.Equal(2, game.Walls[0].Hp);

        game.Projectiles.Add(new Projectile(1, 1, Direction.Right, "player"));
        game.Step();
        Assert.Single(game.Walls);
        Assert.Equal(1, game.Walls[0].Hp);

        game.Projectiles.Add(new Projectile(1, 1, Direction.Right, "player"));
        game.Step();
        Assert.Empty(game.Walls);
    }

    [Fact]
    public void ConcreteWallIsIndestructible()
    {
        var game = new GameEngine { Walls = [new Wall(2, 1, true, 999)] };
        game.Projectiles.Add(new Projectile(1, 1, Direction.Right, "player"));
        game.Step();
        Assert.Single(game.Walls);
        Assert.Equal(999, game.Walls[0].Hp);
    }

    [Fact]
    public void ProjectileDestroyingBaseEndsGame()
    {
        var game = new GameEngine();
        game.Projectiles.Add(new Projectile(4, 9, Direction.Right, "enemy"));
        game.Step();
        Assert.True(game.GameOver);
        Assert.Equal(0, game.Base.Hp);
    }

    [Fact]
    public void PointBlankShotHitsAdjacentWall()
    {
        var game = new GameEngine { Walls = [new Wall(2, 1)] };
        var player = PlayerOf(game);
        player.Direction = Direction.Right;
        Assert.True(game.Fire(player));
        Assert.Empty(game.Walls);
        Assert.Empty(game.Projectiles);
    }

    [Fact]
    public void FireRespectsCooldown()
    {
        var game = new GameEngine();
        var player = PlayerOf(game);
        Assert.True(game.Fire(player));
        Assert.False(game.Fire(player));
        Assert.Single(game.Projectiles);
    }

    [Fact]
    public void HitCostsArmorThenLifeAndRespawns()
    {
        var game = new GameEngine();
        var player = PlayerOf(game);
        game.Projectiles.Add(new Projectile(player.X, player.Y - 1, Direction.Down, "enemy"));
        game.Step();
        Assert.Equal(1, player.Hp);

        game.Projectiles.Add(new Projectile(player.X, player.Y - 1, Direction.Down, "enemy"));
        game.Step();
        Assert.Equal(2, game.Lives);
        Assert.Equal(2, player.Hp);
        Assert.False(game.GameOver);
        Assert.Equal((3, 9), (player.X, player.Y));
    }

    [Fact]
    public void LosingLastLifeEndsGame()
    {
        var game = new GameEngine { Lives = 1 };
        var player = PlayerOf(game);
        player.Hp = 1;
        game.Projectiles.Add(new Projectile(player.X, player.Y - 1, Direction.Down, "enemy"));
        game.Step();
        Assert.True(game.GameOver);
    }

    [Fact]
    public void KillingEnemyGivesScore()
    {
        var game = new GameEngine { Walls = [] };
        var enemy = game.Tanks.First(t => t.Team == GameEngine.EnemyTeam);
        enemy.Hp = 1;
        game.Projectiles.Add(new Projectile(enemy.X - 1, enemy.Y, Direction.Right, "player"));
        game.Step();
        Assert.Equal(100, game.Score);
        Assert.DoesNotContain(enemy, game.Tanks);
    }

    [Fact]
    public void OpposingProjectilesCancelEachOther()
    {
        var game = new GameEngine { Walls = [] };
        game.Projectiles.Add(new Projectile(3, 5, Direction.Right, "player"));
        game.Projectiles.Add(new Projectile(5, 5, Direction.Left, "enemy"));
        game.Step();
        Assert.Empty(game.Projectiles);
    }

    [Fact]
    public void ClearingAllEnemiesStartsNextLevel()
    {
        var game = new GameEngine { Reserve = 0 };
        game.Tanks.RemoveAll(t => t.Team == GameEngine.EnemyTeam);
        game.ThinkEnemies();
        Assert.Equal(2, game.Level);
        Assert.True(game.Reserve > 0);
        Assert.False(game.Won);
    }

    [Fact]
    public void ClearingFinalLevelWinsGame()
    {
        var game = new GameEngine { Level = GameEngine.MaxLevel, Reserve = 0 };
        game.Tanks.RemoveAll(t => t.Team == GameEngine.EnemyTeam);
        game.ThinkEnemies();
        Assert.True(game.Won);
        Assert.True(game.GameOver);
    }

    [Fact]
    public void SaveLoadRestoresFullState()
    {
        var game = new GameEngine { Score = 250, Lives = 2, Level = 3, Reserve = 7, Won = false };
        game.Projectiles.Add(new Projectile(7, 4, Direction.Up, "enemy"));
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        try
        {
            var store = new JsonStateStore();
            store.Save(game, path);
            var loaded = store.Load(path);
            Assert.Equal(250, loaded.Score);
            Assert.Equal((2, 3, 7), (loaded.Lives, loaded.Level, loaded.Reserve));
            Assert.Equal(game.Tanks.Count, loaded.Tanks.Count);
            Assert.Equal(game.Walls.Count, loaded.Walls.Count);
            Assert.Equal(Direction.Up, loaded.Projectiles[0].Direction);
            Assert.False(loaded.Won);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LoadRejectsStateWithoutPlayer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "{\"Tanks\":[]}");
            Assert.Throws<InvalidDataException>(() => new JsonStateStore().Load(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
