using MistForest.Core.Consts;
using MistForest.Core.GameMap;
using MistForest.Core.GameRefactored;
using MistForest.Core.GameRefactored.Roles;
using MistForest.Core.Utils;
using Xunit;
using static MistForest.Core.GameRefactored.PlayerBuilder;

namespace MistForest.Core.Tests.Refactored.Roles
{
    public class ShadowCombatTests
    {
        private static Game MakeGame(int height = 7, int width = 7, int row = 1, int col = 1,
            string victim = "wolf", bool allyAtTarget = false, bool twoVictims = false)
        {
            GameMode mode = GameMode.SelfDefine.New(height, width);
            Board board = Board.FromMode(mode, out _)!;
            Board.Node target = board.At(row, col == 1 ? width : 1)!.Value;
            List<RoleBuilder> own = new()
            {
                ShadowBuilder.New(new UnitBuilder[] { new ShadowUnitBuilder() }, board.At(row, col)!.Value)!,
            };
            if (allyAtTarget)
            {
                own.Add(WolfBuilder.New(new UnitBuilder[] { new WolfUnitBuilder() }, target)!);
            }
            RoleBuilder enemy = victim switch
            {
                "heavy" => HeavyBuilder.New(new UnitBuilder[] { new HeavyUnitBuilder() }, target)!,
                "hidden" => HiddenWolfBuilder.New(new UnitBuilder[] { new HiddenWolfUnitBuilder() }, target)!,
                "sniper" => SniperBuilder.New(new UnitBuilder[] { new SniperUnitBuilder() }, target)!,
                _ => WolfBuilder.New(new UnitBuilder[] { new WolfUnitBuilder() }, target)!,
            };
            List<RoleBuilder> enemies = new() { enemy };
            if (twoVictims)
            {
                enemies.Add(WolfBuilder.New(new UnitBuilder[] { new WolfUnitBuilder() }, target)!);
            }
            GameBuilder builder = new();
            builder.SetMode(Box.New(mode));
            builder.SetBoard(Box.New(board));
            builder.SetPlayers(Box.New(new List<PlayerBuilder> { New(own)!, New(enemies)! }));
            builder.SetFirstPlayerId(0);
            return Assert.IsType<Game>(builder.Build());
        }

        private static Shadow Role(Game game) => Assert.IsType<Shadow>(game.Roles[0]);
        private static ShadowUnitView View(Game game) => Assert.IsType<ShadowUnitView>(Role(game).Unit.GetView(game));

        private static void AddLineWall(Board board, Board.Node lowerEdge, bool horizontal)
        {
            int index = (horizontal ? board.HWallAt(lowerEdge) : board.VWallAt(lowerEdge))!.Value;
            Assert.True((horizontal ? board.HorizontalWalls : board.VerticalWalls)
                .SetInner(index / 64, 1ul << (index % 64), out _));
        }

        [Theory]
        [InlineData(1, 3, 7, 3)]
        [InlineData(7, 3, 1, 3)]
        [InlineData(3, 1, 3, 7)]
        [InlineData(3, 7, 3, 1)]
        public static void Move_WrapsAcrossEachEdgeAndRecordsTheActualPath(int row, int col, int toRow, int toCol)
        {
            Game game = MakeGame(row: row, col: col);
            Board.Node origin = Role(game).Unit.Position;
            Board.Node target = game.Board.At(toRow, toCol)!.Value;
            Assert.Contains(View(game).CanMoveGrids, entry => entry.node == target && entry.distance == 1);
            Assert.True(game.ExecuteAction(GeneralMove.New(0, target)));
            Assert.Equal(target, Role(game).Unit.Position);
            Assert.Empty(game.Board.GetGrid(origin)!.Units);
            Assert.Contains(0, game.Board.GetGrid(target)!.Units);
            Assert.Equal(new[] { origin, target }, Assert.IsType<GeneralMoveEvent>(Assert.Single(game.Events)).Path);
            Assert.Empty(View(game).CanAttackGrids);
            Assert.False(game.ExecuteAction(ShadowAttack.New(0, origin)));
            Assert.Equal(0, game.CurrentPlayerId);
        }

        [Fact]
        public static void Move_TwoStepsCanWrapBothAxesAndConsumeTheWholeBudget()
        {
            Game game = MakeGame();
            Board.Node origin = Role(game).Unit.Position;
            Board.Node target = game.Board.At(7, 7)!.Value;
            Assert.Contains(View(game).CanMoveGrids, entry => entry.node == target && entry.distance == 2);
            Assert.True(game.ExecuteAction(GeneralMove.New(0, target)));
            Board.Node[] path = Assert.IsType<GeneralMoveEvent>(Assert.Single(game.Events)).Path;
            Assert.Equal(new[] { origin, game.Board.At(7, 1)!.Value, target }, path);
            Assert.Equal(1, game.CurrentPlayerId);
            Assert.Empty(View(game).CanMoveGrids);
            Assert.Empty(View(game).CanAttackGrids);
            Assert.True(game.ExecuteAction(GeneralEnd.New(1)));
            Assert.Equal(0, game.CurrentPlayerId);
            Assert.NotEmpty(View(game).CanAttackGrids);
            Assert.Contains(View(game).CanMoveGrids, entry => entry.distance == 2);
        }

        [Theory]
        [InlineData("lake")]
        [InlineData("terrain-wall")]
        [InlineData("enemy-fort")]
        [InlineData("allied-wall")]
        public static void Move_CannotEnterBlockedWrappedDestination(string obstruction)
        {
            Game game = MakeGame();
            Board.Node target = game.Board.At(1, 7)!.Value;
            if (obstruction == "lake" || obstruction == "terrain-wall")
                game.Board.SetTerrain(target, obstruction == "lake" ? Terrain.Lake : Terrain.Wall);
            else if (obstruction == "enemy-fort")
                game.AddBuilding(FortBuilder.New().SetOwner(1)!.SetHealth(MapConsts.FortHealth)!.SetPos(target)!);
            else
                game.AddBuilding(WallBuilder.New().SetOwner(0)!.SetHealth(MapConsts.WallHealth)!.SetPos(target)!);
            Assert.DoesNotContain(View(game).CanMoveGrids, entry => entry.node == target);
            Assert.False(game.ExecuteAction(GeneralMove.New(0, target)));
            Assert.Empty(game.Events);
            Assert.True(Role(game).IsFirstAction);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public static void Move_ChecksInnerLineWallAfterWrapping(bool horizontal)
        {
            Game game = MakeGame();
            Board.Node middle = game.Board.At(horizontal ? 7 : 1, horizontal ? 1 : 7)!.Value;
            Board.Node target = game.Board.At(horizontal ? 6 : 1, horizontal ? 1 : 6)!.Value;
            AddLineWall(game.Board, target, horizontal);
            Assert.Contains(View(game).CanMoveGrids, entry => entry.node == middle);
            Assert.DoesNotContain(View(game).CanMoveGrids, entry => entry.node == target);
            Assert.False(game.ExecuteAction(GeneralMove.New(0, target)));
            Assert.True(game.ExecuteAction(GeneralMove.New(0, middle)));
            Assert.DoesNotContain(View(game).CanMoveGrids, entry => entry.node == target);
            Assert.False(game.ExecuteAction(GeneralMove.New(0, target)));
        }

        [Theory]
        [InlineData(1, 1, 0)]
        [InlineData(1, 2, 1)]
        [InlineData(2, 1, 1)]
        [InlineData(2, 2, 2)]
        [InlineData(1, 3, 2)]
        [InlineData(3, 1, 2)]
        public static void SmallMaps_DeduplicateTargetsAndExcludeSelf(int height, int width, int attacks)
        {
            Game game = MakeGame(height, width);
            ShadowUnitView view = View(game);
            Assert.Equal(attacks, view.CanAttackGrids.Length);
            Assert.Equal(attacks, view.CanAttackGrids.Distinct().Count());
            Assert.Equal(view.CanMoveGrids.Length, view.CanMoveGrids.Select(entry => entry.node).Distinct().Count());
            Assert.DoesNotContain(Role(game).Unit.Position, view.CanAttackGrids);
            Assert.DoesNotContain(view.CanMoveGrids, entry => entry.node == Role(game).Unit.Position);
            Assert.False(game.ExecuteAction(ShadowAttack.New(0, Role(game).Unit.Position)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public static void TwoCellAxis_AnInnerWallDoesNotBlockTheAlternateWrappedEdge(bool horizontal)
        {
            Game game = MakeGame(horizontal ? 2 : 1, horizontal ? 1 : 2);
            Board.Node origin = Role(game).Unit.Position;
            Board.Node target = game.Board.At(horizontal ? 2 : 1, horizontal ? 1 : 2)!.Value;
            AddLineWall(game.Board, origin, horizontal);
            Assert.Equal(new[] { target }, View(game).CanAttackGrids);
            Assert.Contains(View(game).CanMoveGrids, entry => entry.node == target && entry.distance == 1);
            Assert.True(game.ExecuteAction(GeneralMove.New(0, target)));
        }

        [Theory]
        [InlineData(1, 3, 7, 3)]
        [InlineData(7, 3, 1, 3)]
        [InlineData(3, 1, 3, 7)]
        [InlineData(3, 7, 3, 1)]
        public static void Attack_CanTargetAcrossEachEdgeAndEndsTurn(int row, int col, int toRow, int toCol)
        {
            Game game = MakeGame(row: row, col: col);
            Board.Node origin = Role(game).Unit.Position;
            Board.Node target = game.Board.At(toRow, toCol)!.Value;
            Assert.Contains(target, View(game).CanAttackGrids);
            Assert.True(game.ExecuteAction(ShadowAttack.New(0, target)));
            Assert.Equal(origin, Role(game).Unit.Position);
            Assert.True(Role(game).Unit.End);
            Assert.Equal(1, game.CurrentPlayerId);
            Assert.Empty(View(game).CanMoveGrids);
            Assert.Empty(View(game).CanAttackGrids);
            ShadowAttackEvent result = Assert.IsType<ShadowAttackEvent>(Assert.Single(game.Events));
            Assert.Equal(0, result.AttackerId);
            Assert.Equal(target, result.Target);
            Assert.Null(result.BuildingId);
            Assert.False(game.ExecuteAction(GeneralMove.New(0, target)));
            Assert.False(game.ExecuteAction(ShadowAttack.New(0, target)));
        }

        [Theory]
        [InlineData("self")]
        [InlineData("diagonal")]
        [InlineData("two-steps")]
        [InlineData("lake")]
        [InlineData("terrain-wall")]
        [InlineData("line-wall")]
        [InlineData("out-of-board")]
        public static void Attack_RejectsIllegalTargetsWithoutConsumingTurn(string kind)
        {
            Game game = MakeGame(row: 3, col: 3);
            Board.Node target = game.Board.At(3, 4)!.Value;
            switch (kind)
            {
                case "self": target = Role(game).Unit.Position; break;
                case "diagonal": target = game.Board.At(2, 2)!.Value; break;
                case "two-steps": target = game.Board.At(3, 5)!.Value; break;
                case "lake": game.Board.SetTerrain(target, Terrain.Lake); break;
                case "terrain-wall": game.Board.SetTerrain(target, Terrain.Wall); break;
                case "line-wall": AddLineWall(game.Board, Role(game).Unit.Position, false); break;
                case "out-of-board": target = Board.New(8, 8).At(8, 8)!.Value; break;
            }
            Assert.DoesNotContain(target, View(game).CanAttackGrids);
            Assert.False(game.ExecuteAction(ShadowAttack.New(0, target)));
            Assert.True(Role(game).IsFirstAction);
            Assert.Empty(game.Events);
            Assert.Equal(0, game.CurrentPlayerId);
        }

        [Theory]
        [InlineData("wolf", 2, 0)]
        [InlineData("heavy", 8, 0)]
        [InlineData("hidden", 2, 0)]
        public static void Attack_UsesSharedMeleeDamageAndArmor(string victim, int health, int armor)
        {
            Game game = MakeGame(victim: victim);
            Unit enemy = game.GetUnit(1)!;
            Assert.True(game.ExecuteAction(ShadowAttack.New(0, enemy.Position)));
            Assert.Equal(health, enemy.Health);
            Assert.Equal(armor, enemy.Armor);
            Assert.Equal(ShadowConsts.ShadowAttackDamage, enemy.HighestDirectDamage);
            Assert.Equal(new[] { 1 }, Assert.IsType<ShadowAttackEvent>(Assert.Single(game.Events)).UnitIds);
        }

        [Fact]
        public static void Attack_SnapshotsMultipleVictimsBeforeKillingAndPreservesAllies()
        {
            Game game = MakeGame(victim: "sniper", allyAtTarget: true, twoVictims: true);
            Board.Node target = game.Board.At(1, 7)!.Value;
            Assert.True(game.ExecuteAction(ShadowAttack.New(0, target)));
            Assert.Equal(6, game.GetUnit(1)!.Health);
            Assert.False(game.GetUnit(2)!.Alive);
            Assert.Equal(2, game.GetUnit(3)!.Health);
            Assert.Equal(new[] { 1, 3 }, game.Board.GetGrid(target)!.Units);
            Assert.Equal(new[] { 2, 3 }, Assert.IsType<ShadowAttackEvent>(Assert.Single(game.Events)).UnitIds);
        }

        [Theory]
        [InlineData(false, 0, 4)]
        [InlineData(false, 1, 4)]
        [InlineData(true, 0, 4)]
        [InlineData(true, 1, 4)]
        [InlineData(false, 1, 8)]
        public static void Attack_DamagesAlliedAndEnemyBuildingsWithoutSpilling(bool wall, int owner, int health)
        {
            Game game = MakeGame();
            Board.Node target = game.GetUnit(1)!.Position;
            if (wall)
                game.AddBuilding(WallBuilder.New().SetOwner(owner)!.SetHealth(health)!.SetPos(target)!);
            else
                game.AddBuilding(FortBuilder.New().SetOwner(owner)!.SetHealth(health)!.SetPos(target)!);
            Assert.True(game.ExecuteAction(ShadowAttack.New(0, target)));
            Assert.Equal(6, game.GetUnit(1)!.Health);
            Assert.Equal(0, game.GetUnit(1)!.HighestDirectDamage);
            if (health == 4)
                Assert.IsType<Building.Rubble>(game.Buildings[0]);
            else
                Assert.Equal(4, game.Buildings[0].Health);
            ShadowAttackEvent result = Assert.IsType<ShadowAttackEvent>(Assert.Single(game.Events));
            Assert.Equal(0, result.BuildingId);
            Assert.Empty(result.UnitIds);
        }

        [Fact]
        public static void Attack_OnlySupplementsTheHighestDirectStrengthAcrossAttackers()
        {
            GameMode mode = GameMode.SelfDefine.New(7, 7);
            Board board = Board.FromMode(mode, out _)!;
            Board.Node target = board.At(1, 7)!.Value;
            GameBuilder builder = new();
            builder.SetMode(Box.New(mode));
            builder.SetBoard(Box.New(board));
            builder.SetPlayers(Box.New(new List<PlayerBuilder>
            {
                New(new List<RoleBuilder> { ShadowBuilder.New(new UnitBuilder[] { new ShadowUnitBuilder() }, board.At(1, 1)!.Value)! })!,
                New(new List<RoleBuilder> { ShadowBuilder.New(new UnitBuilder[] { new ShadowUnitBuilder() }, board.At(1, 6)!.Value)! })!,
                New(new List<RoleBuilder> { WolfBuilder.New(new UnitBuilder[] { new WolfUnitBuilder() }, target)! })!,
            }));
            builder.SetFirstPlayerId(0);
            Game game = builder.Build()!;
            Assert.True(game.ExecuteAction(ShadowAttack.New(0, target)));
            Assert.Equal(2, game.GetUnit(2)!.Health);
            Assert.True(game.ExecuteAction(ShadowAttack.New(1, target)));
            Assert.Equal(2, game.GetUnit(2)!.Health);
            Assert.Equal(4, game.GetUnit(2)!.HighestDirectDamage);
        }

        [Fact]
        public static void ViewQueriesAndClone_DoNotReplaceAValidatedMovePath()
        {
            Game game = MakeGame();
            Board.Node target = game.Board.At(7, 7)!.Value;
            Assert.True(Role(game).JudgeAction(GeneralMove.New(0, target), game));
            ShadowUnitView view = View(game);
            ShadowUnitView clone = Assert.IsType<ShadowUnitView>(view.Clone());
            clone.CanAttackGrids[0] = Role(game).Unit.Position;
            Assert.NotEqual(clone.CanAttackGrids[0], view.CanAttackGrids[0]);
            Role(game).ExecuteAction(GeneralMove.New(0, target), game);
            Assert.Equal(target, Role(game).Unit.Position);
            Assert.Equal(3, Assert.IsType<GeneralMoveEvent>(Assert.Single(game.Events)).Path.Length);
            Shadow roleClone = Assert.IsType<Shadow>(Role(game).Clone());
            Assert.Empty(roleClone.Unit.GetView(game).CanMoveGrids);
            Assert.Empty(Assert.IsType<ShadowUnitView>(roleClone.Unit.GetView(game)).CanAttackGrids);
        }
    }
}
