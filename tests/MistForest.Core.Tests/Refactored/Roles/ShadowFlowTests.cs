using MistForest.Core.Consts;
using MistForest.Core.GameMap;
using MistForest.Core.GameRefactored;
using MistForest.Core.GameRefactored.Roles;
using MistForest.Core.Utils;
using Xunit;
using static MistForest.Core.GameRefactored.PlayerBuilder;
using GameAction = MistForest.Core.GameRefactored.Action;

namespace MistForest.Core.Tests.Refactored.Roles
{
    public class ShadowFlowTests
    {
        private static Game MakeGame()
        {
            GameMode mode = GameMode.SelfDefine.New(7, 7);
            Board board = Board.FromMode(mode, out _)!;
            GameBuilder builder = new();
            builder.SetMode(Box.New(mode));
            builder.SetBoard(Box.New(board));
            builder.SetPlayers(Box.New(new List<PlayerBuilder>
            {
                New(new List<RoleBuilder>
                {
                    ShadowBuilder.New(new UnitBuilder[] { new ShadowUnitBuilder() }, board.At(3, 3)!.Value)!,
                })!,
                New(new List<RoleBuilder>
                {
                    WolfBuilder.New(new UnitBuilder[] { new WolfUnitBuilder() }, board.At(7, 7)!.Value)!,
                })!,
            }));
            builder.SetFirstPlayerId(0);
            return Assert.IsType<Game>(builder.Build());
        }

        private static Shadow Role(Game game) => Assert.IsType<Shadow>(game.Roles[0]);
        private static ShadowUnitView View(Game game) => Assert.IsType<ShadowUnitView>(Role(game).Unit.GetView(game));

        private static void AssertNoActions(UnitView view)
        {
            Assert.Empty(view.CanMoveGrids);
            Assert.Empty(view.CanBuildFortGrids);
            Assert.Empty(view.CanBuildWallGrids);
            Assert.Empty(view.CanDemolishGrids);
        }

        [Fact]
        public static void Builder_CreatesShadowAndIndependentClones()
        {
            Game game = MakeGame();
            Shadow role = Role(game);
            Shadow clone = Assert.IsType<Shadow>(role.Clone());
            ShadowUnit unitClone = Assert.IsType<ShadowUnit>(role.Unit.Clone());
            Assert.Equal(0, role.Id);
            Assert.Equal(0, role.PlayerId);
            Assert.Equal(role.Id, role.Unit.RoleId);
            Assert.Equal(0, role.Unit.Id);
            Assert.Equal(6, role.Unit.Health);
            Assert.Equal(0, role.Unit.Armor);
            Assert.Equal(0, role.Unit.TemporaryShield);
            Assert.Equal(1, role.Unit.AttackRange);
            Assert.Equal(game.Board.At(3, 3)!.Value, role.Unit.Position);
            Assert.Same(role.Unit, Assert.Single(role.GetUnits().ToArray()));
            Assert.Equal(new[] { 0 }, game.Board.GetGrid(role.Unit.Position)!.Units);
            Assert.NotSame(role.Unit, clone.Unit);
            Assert.NotSame(role.Unit, unitClone);
            Assert.Equal(role.Unit.Health, unitClone.Health);
            Assert.Equal(role.PlayerId, clone.PlayerId);
            Assert.True(clone.IsFirstAction);
            Assert.Contains(View(game).CanMoveGrids, entry => entry.distance == 2);
        }

        [Fact]
        public static void Builder_RejectsWrongStructureAndConflictingPositionAndCachesResults()
        {
            Board board = Board.New(3, 3);
            Board.Node position = board.At(2, 2)!.Value;
            Assert.Null(ShadowBuilder.New(Array.Empty<UnitBuilder>(), position));
            Assert.Null(ShadowBuilder.New(new UnitBuilder[] { new WolfUnitBuilder() }, position));
            Assert.Null(ShadowBuilder.New(new UnitBuilder[]
            {
                new ShadowUnitBuilder(), new ShadowUnitBuilder(),
            }, position));
            ShadowUnitBuilder unitBuilder = new();
            ShadowBuilder builder = ShadowBuilder.New(new UnitBuilder[] { unitBuilder }, position)!;
            Assert.Null(unitBuilder.Build());
            Assert.Null(builder.Build());
            builder.SetId(0);
            Assert.Null(builder.Build());
            builder.SetPlayerId(0);
            Assert.Null(builder.Build());
            unitBuilder.SetId(0)!.SetRoleId(0);
            Shadow role = Assert.IsType<Shadow>(builder.Build());
            Assert.Same(role, builder.Build());
            Assert.Same(role.Unit, unitBuilder.Build());

            unitBuilder = new();
            unitBuilder.SetId(0)!.SetRoleId(0)!.SetPosition(board.At(1, 1)!.Value);
            builder = ShadowBuilder.New(new UnitBuilder[] { unitBuilder }, position)!;
            builder.SetId(0)!.SetPlayerId(0);
            Assert.Null(builder.Build());
        }

        [Theory]
        [InlineData(Terrain.Ground, true)]
        [InlineData(Terrain.Whirlpool, true)]
        [InlineData(Terrain.Wall, false)]
        [InlineData(Terrain.Lake, false)]
        public static void Builder_ChecksInitialTerrain(Terrain terrain, bool allowed)
        {
            Board board = Board.New(3, 3);
            Board.Node position = board.At(2, 2)!.Value;
            board.SetTerrain(position, terrain);
            ShadowBuilder builder = ShadowBuilder.New(new UnitBuilder[] { new ShadowUnitBuilder() }, position)!;
            Assert.Equal(allowed, builder.CanInitialStay(board));
            Assert.False(builder.CanInitialStay(Board.New(1, 1)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public static void Move_ConsumesTwoStepBudgetAndRestoresItNextRound(bool split)
        {
            Game game = MakeGame();
            Shadow role = Role(game);
            Board.Node origin = role.Unit.Position;
            Board.Node middle = game.Board.At(3, 4)!.Value;
            Board.Node target = game.Board.At(3, 5)!.Value;
            Shadow? cloneAfterOneStep = null;
            if (split)
            {
                Assert.True(game.ExecuteAction(GeneralMove.New(0, middle)));
                Assert.Equal(0, game.CurrentPlayerId);
                Assert.False(role.IsFirstAction);
                Assert.All(View(game).CanMoveGrids, entry => Assert.Equal(1, entry.distance));
                cloneAfterOneStep = Assert.IsType<Shadow>(role.Clone());
            }
            Assert.True(game.ExecuteAction(GeneralMove.New(0, target)));
            Assert.Equal(target, role.Unit.Position);
            Assert.Empty(game.Board.GetGrid(origin)!.Units);
            Assert.Empty(game.Board.GetGrid(middle)!.Units);
            Assert.Equal(new[] { 0 }, game.Board.GetGrid(target)!.Units);
            Assert.Equal(1, game.CurrentPlayerId);
            Assert.False(role.HasActionLeft(game));
            AssertNoActions(View(game));
            GeneralMoveEvent[] moves = game.Events.OfType<GeneralMoveEvent>().ToArray();
            Assert.Equal(split ? 2 : 1, moves.Length);
            Assert.Equal(split ? new[] { middle, target } : new[] { origin, middle, target }, moves.Last().Path);
            Assert.False(game.ExecuteAction(GeneralMove.New(0, middle)));
            Assert.True(game.ExecuteAction(GeneralEnd.New(1)));
            Assert.Equal(0, game.CurrentPlayerId);
            Assert.True(role.IsFirstAction);
            Assert.Contains(View(game).CanMoveGrids, entry => entry.distance == 2);
            if (cloneAfterOneStep is not null)
            {
                Assert.Equal(middle, cloneAfterOneStep.Unit.Position);
                Assert.False(cloneAfterOneStep.IsFirstAction);
                Assert.All(cloneAfterOneStep.Unit.GetView(game).CanMoveGrids,
                    entry => Assert.Equal(1, entry.distance));
            }
        }

        [Theory]
        [InlineData("self")]
        [InlineData("three-steps")]
        [InlineData("lake")]
        [InlineData("terrain-wall")]
        [InlineData("line-wall")]
        [InlineData("enemy-fort")]
        [InlineData("allied-wall")]
        [InlineData("out-of-board")]
        public static void Move_RejectsInvalidTargetsWithoutSideEffects(string obstruction)
        {
            Game game = MakeGame();
            Board.Node origin = Role(game).Unit.Position;
            Board.Node target = game.Board.At(3, 4)!.Value;
            switch (obstruction)
            {
                case "self": target = origin; break;
                case "three-steps": target = game.Board.At(3, 6)!.Value; break;
                case "lake": game.Board.SetTerrain(target, Terrain.Lake); break;
                case "terrain-wall": game.Board.SetTerrain(target, Terrain.Wall); break;
                case "line-wall":
                    int wall = game.Board.VWallAt(origin)!.Value;
                    Assert.True(game.Board.VerticalWalls.SetInner(wall / 64, 1ul << (wall % 64), out _));
                    break;
                case "enemy-fort":
                    game.AddBuilding(FortBuilder.New().SetOwner(1)!.SetHealth(MapConsts.FortHealth)!.SetPos(target)!);
                    break;
                case "allied-wall":
                    game.AddBuilding(WallBuilder.New().SetOwner(0)!.SetHealth(MapConsts.WallHealth)!.SetPos(target)!);
                    break;
                case "out-of-board": target = Board.New(8, 8).At(8, 8)!.Value; break;
            }
            Assert.DoesNotContain(View(game).CanMoveGrids, entry => entry.node == target);
            Assert.False(game.ExecuteAction(GeneralMove.New(0, target)));
            Assert.Equal(origin, Role(game).Unit.Position);
            Assert.True(Role(game).IsFirstAction);
            Assert.Empty(game.Events);
            Assert.Equal(0, game.CurrentPlayerId);
        }

        [Fact]
        public static void Move_CanDetourThroughAlliedFort()
        {
            Game game = MakeGame();
            Board.Node origin = Role(game).Unit.Position;
            Board.Node middle = game.Board.At(4, 3)!.Value;
            Board.Node target = game.Board.At(4, 4)!.Value;
            game.Board.SetTerrain(game.Board.At(3, 4)!.Value, Terrain.Wall);
            game.AddBuilding(FortBuilder.New().SetOwner(0)!.SetHealth(MapConsts.FortHealth)!.SetPos(middle)!);
            Assert.True(game.ExecuteAction(GeneralMove.New(0, target)));
            Assert.Equal(new[] { origin, middle, target },
                Assert.IsType<GeneralMoveEvent>(Assert.Single(game.Events)).Path);
            Assert.Equal(MapConsts.FortHealth, game.Buildings[0].Health);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public static void Build_EndsTurnAndIsRejectedAfterMovement(bool wall)
        {
            Game game = MakeGame();
            Board.Node target = game.Board.At(2, 3)!.Value;
            GameAction build = wall ? GeneralBuildWall.New(0, new[] { target })! : GeneralBuildFort.New(0, target);
            Assert.True(game.ExecuteAction(build));
            Building built = Assert.Single(game.Buildings);
            Assert.Equal(wall ? typeof(Building.Wall) : typeof(Building.Fort), built.GetType());
            Assert.Equal(target, built.Pos);
            Assert.Equal(wall ? MapConsts.WallHealth : MapConsts.FortHealth, built.Health);
            Assert.Equal(1, game.CurrentPlayerId);
            AssertNoActions(View(game));

            game = MakeGame();
            Assert.True(game.ExecuteAction(GeneralMove.New(0, game.Board.At(3, 4)!.Value)));
            target = game.Board.At(2, 4)!.Value;
            build = wall ? GeneralBuildWall.New(0, new[] { target })! : GeneralBuildFort.New(0, target);
            Assert.False(game.ExecuteAction(build));
            Assert.Empty(game.Buildings);
            Assert.NotEmpty(View(game).CanMoveGrids);
        }

        [Fact]
        public static void Demolish_OnlyAllowsAlliedBuildingsAndEndsTurn()
        {
            Game game = MakeGame();
            Board.Node allied = game.Board.At(2, 3)!.Value;
            Board.Node enemy = game.Board.At(3, 4)!.Value;
            game.AddBuilding(FortBuilder.New().SetOwner(0)!.SetHealth(MapConsts.FortHealth)!.SetPos(allied)!);
            game.AddBuilding(FortBuilder.New().SetOwner(1)!.SetHealth(MapConsts.FortHealth)!.SetPos(enemy)!);
            Assert.Equal(new[] { allied }, View(game).CanDemolishGrids);
            Assert.False(game.ExecuteAction(GeneralDemolish.New(0, enemy)));
            Assert.True(game.ExecuteAction(GeneralDemolish.New(0, allied)));
            Assert.IsType<Building.Rubble>(game.Buildings[0]);
            Assert.IsType<Building.Fort>(game.Buildings[1]);
            Assert.Equal(1, game.CurrentPlayerId);
            AssertNoActions(View(game));
            Assert.IsType<GeneralDemolishEvent>(Assert.Single(game.Events));
        }

        [Fact]
        public static void ViewClone_OwnsEveryActionArray()
        {
            Game game = MakeGame();
            Board.Node target = game.Board.At(2, 3)!.Value;
            ShadowUnitView view = ShadowUnitView.New(
                Box.New(View(game).CanMoveGrids),
                Box.New(new[] { target }), Box.New(new[] { target }), Box.New(new[] { target }))!;
            ShadowUnitView clone = Assert.IsType<ShadowUnitView>(view.Clone());
            var originalMove = view.CanMoveGrids[0];
            clone.CanMoveGrids[0] = (Role(game).Unit.Position, 0, null);
            clone.CanBuildFortGrids[0] = Role(game).Unit.Position;
            clone.CanBuildWallGrids[0] = Role(game).Unit.Position;
            clone.CanDemolishGrids[0] = Role(game).Unit.Position;
            Assert.Equal(originalMove, view.CanMoveGrids[0]);
            Assert.Equal(target, view.CanBuildFortGrids[0]);
            Assert.Equal(target, view.CanBuildWallGrids[0]);
            Assert.Equal(target, view.CanDemolishGrids[0]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public static void EndAndSurrender_ClearAvailableActions(bool surrender)
        {
            Game game = MakeGame();
            Assert.True(game.ExecuteAction(surrender ? GeneralSurrender.New(0) : GeneralEnd.New(0)));
            AssertNoActions(View(game));
            Assert.False(Role(game).HasActionLeft(game));
            Assert.Equal(!surrender, Role(game).Unit.Alive);
            Assert.False(Role(game).JudgeAction(GeneralMove.New(0, game.Board.At(3, 4)!.Value), game));
            Assert.Single(game.Events);
            if (surrender)
            {
                Assert.False(game.Players[0].Alive);
                Assert.Empty(game.Board.GetGrid(Role(game).Unit.Position)!.Units);
            }
            else
            {
                Assert.Equal(1, game.CurrentPlayerId);
            }
        }

        [Fact]
        public static void Shadow_RejectsOtherRolesAttacksAndSkills()
        {
            Game game = MakeGame();
            Board.Node target = game.Board.At(3, 4)!.Value;
            GameAction[] actions =
            {
                WolfBite.New(0, target), SniperExecute.New(0, target), AlphaAttack.New(0, target),
                AlphaBlood.New(0), InkAttack.New(0, target), InkDash.New(0, target),
                ArmigerArmorUp.New(0), ArmigerThrust.New(0, target),
                HeavyChargeStart.New(0, target), HiddenWolfTransform.New(0), HiddenWolfAttack.New(0, target),
            };
            Assert.All(actions, action =>
            {
                Assert.False(Role(game).Unit.HasAction(action));
                Assert.False(Role(game).JudgeAction(action, game));
                Assert.False(game.ExecuteAction(action));
            });
            Assert.True(Role(game).IsFirstAction);
            Assert.Empty(game.Events);
        }
    }
}
