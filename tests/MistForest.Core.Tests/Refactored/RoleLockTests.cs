using MistForest.Core.GameMap;
using MistForest.Core.GameRefactored;
using MistForest.Core.GameRefactored.Roles;
using MistForest.Core.Utils;
using Xunit;
using static MistForest.Core.GameRefactored.PlayerBuilder;
using GameAction = MistForest.Core.GameRefactored.Action;

namespace MistForest.Core.Tests.Refactored
{
    public class RoleLockTests
    {
        private static Game MakeGame(string skill)
        {
            GameMode mode = GameMode.SelfDefine.New(7, 7);
            Board board = Board.FromMode(mode, out _)!;
            Board.Node origin = board.At(3, 3)!.Value;
            RoleBuilder active = skill == "transform"
                ? HiddenWolfBuilder.New(new UnitBuilder[] { new HiddenWolfUnitBuilder() }, origin)!
                : ShadowBuilder.New(new UnitBuilder[] { new ShadowUnitBuilder() }, origin)!;
            GameBuilder builder = new();
            builder.SetMode(Box.New(mode));
            builder.SetBoard(Box.New(board));
            Assert.NotNull(builder.SetPlayers(Box.New(new List<PlayerBuilder>
            {
                New(new List<RoleBuilder>
                {
                    active,
                    SniperBuilder.New(new UnitBuilder[] { new SniperUnitBuilder() }, board.At(7, 7)!.Value)!,
                })!,
                New(new List<RoleBuilder>
                {
                    SniperBuilder.New(new UnitBuilder[] { new SniperUnitBuilder() }, board.At(3, 4)!.Value)!,
                    InkBuilder.New(new UnitBuilder[] { new InkUnitBuilder() }, board.At(6, 6)!.Value)!,
                })!,
            })));
            builder.SetFirstPlayerId(0);
            return Assert.IsType<Game>(builder.Build());
        }

        private static GameAction Skill(string skill) => skill switch
        {
            "follow" => ShadowFollow.New(0, 2),
            "drag" => ShadowDrag.New(0, 2),
            _ => HiddenWolfTransform.New(0),
        };

        [Theory]
        [InlineData("follow", false)]
        [InlineData("follow", true)]
        [InlineData("drag", false)]
        [InlineData("drag", true)]
        [InlineData("transform", false)]
        [InlineData("transform", true)]
        public static void DeadRole_DoesNotPreventSurvivingRoleFromMovingOrEndingNextTurn(string skill, bool end)
        {
            Game game = MakeGame(skill);
            Board.Node origin = game.GetUnit(0)!.Position;
            Assert.True(game.ExecuteAction(Skill(skill)));
            Assert.True(game.ExecuteAction(GeneralEnd.New(0)));
            Assert.Equal(1, game.CurrentPlayerId);
            Assert.True(game.ExecuteAction(SniperExecute.New(2, origin)));
            Assert.False(game.Roles[0].Alive);
            Assert.True(game.GetUnit(1)!.Alive);
            Assert.Equal(0, game.CurrentPlayerId);
            Assert.False(game.ExecuteAction(GeneralEnd.New(0)));

            Board.Node target = game.Board.At(7, 6)!.Value;
            GameAction action = end ? GeneralEnd.New(1) : GeneralMove.New(1, target);
            Assert.True(game.ExecuteAction(action));
            if (!end)
            {
                Assert.Equal(target, game.GetUnit(1)!.Position);
                Assert.Contains(1, game.Board.GetGrid(target)!.Units);
            }
            Assert.Equal(1, game.CurrentPlayerId);
        }

        [Theory]
        [InlineData("follow")]
        [InlineData("drag")]
        [InlineData("transform")]
        public static void LivingRole_StillLocksOtherRolesAfterUsingSkill(string skill)
        {
            Game game = MakeGame(skill);
            Assert.True(game.ExecuteAction(Skill(skill)));
            Assert.True(game.Roles[0].Alive);
            Assert.False(game.ExecuteAction(GeneralEnd.New(1)));
            Assert.False(game.ExecuteAction(GeneralMove.New(1, game.Board.At(7, 6)!.Value)));
            Assert.Equal(0, game.CurrentPlayerId);
            Assert.True(game.ExecuteAction(GeneralEnd.New(0)));
        }
    }
}
