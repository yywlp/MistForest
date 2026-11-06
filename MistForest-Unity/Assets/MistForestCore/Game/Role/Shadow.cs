using System;
using MistForest.Core.Consts;
using MistForest.Core.GameMap;
using MistForest.Core.Utils;
using static MistForest.Core.GameMap.Board;
using static MistForest.Core.GameRefactored.PlayerBuilder;

namespace MistForest.Core.GameRefactored.Roles
{
    public interface IShadowAction { }

    /// <summary>Dispatches Shadow's general actions.</summary>
    public class Shadow : Role
    {
        private readonly Unit[] _units;
        public ShadowUnit Unit => (ShadowUnit)_units[0];

        private Shadow(ShadowUnit unit) => _units = new Unit[] { unit };

        internal static Shadow New(int id, int playerId, ShadowUnit unit)
        {
            Shadow target = new(unit);
            NewBase(target, id, playerId);
            return target;
        }

        public override Role Clone()
        {
            return new Shadow((ShadowUnit)Unit.Clone()) { Id = Id, PlayerId = PlayerId };
        }

        public override bool JudgeAction(Action action, Game game)
        {
            if (!Unit.Alive || Unit.End)
            {
                return false;
            }
            return action switch
            {
                GeneralMove move => Unit.CanMove(move, game),
                GeneralEnd _ => Unit.CanEnd(),
                GeneralSurrender _ => Unit.CanSurrender(),
                GeneralBuildFort buildFort => Unit.CanBuildFort(buildFort, game),
                GeneralBuildWall buildWall => Unit.CanBuildWall(buildWall, game),
                GeneralDemolish demolish => Unit.CanDemolish(demolish, game),
                _ => false,
            };
        }

        public override void ExecuteAction(Action action, Game game)
        {
            Unit.StartExecuteAction();
            switch (action)
            {
                case GeneralMove move:
                    Unit.GeneralMove(move, game);
                    break;
                case GeneralEnd end:
                    Unit.GeneralEnd(end, game);
                    break;
                case GeneralSurrender _:
                    GeneralSurrender(game);
                    break;
                case GeneralBuildFort buildFort:
                    Unit.GeneralBuildFort(buildFort, game);
                    break;
                case GeneralBuildWall buildWall:
                    Unit.GeneralBuildWall(buildWall, game);
                    break;
                case GeneralDemolish demolish:
                    Unit.GeneralDemolish(demolish, game);
                    break;
            }
        }

        public override bool HasActionLeft(Game game) => !End(game);

        public override ReadOnlySpan<Unit> GetUnits() => _units;
    }

    /// <summary>Stores Shadow's ordinary movement allowance and shared unit state.</summary>
    /// <remarks>
    /// TODO: Add boundary-wrapping movement and melee attacks, mutually exclusive with movement.
    /// TODO: Add follow and drag links, movement triggers, expiry and human-form HiddenWolf immunity.
    /// TODO: Integrate skill availability and link suspension with the shared silence system.
    /// </remarks>
    public class ShadowUnit : Unit
    {
        protected override int MoveDistance => ShadowConsts.ShadowMoveDistance;
        public override int AttackRange => ShadowConsts.ShadowAttackRange;

        private ShadowUnit() { }

        public static ShadowUnit New(int id, int roleId, Node position)
        {
            ShadowUnit target = new() { Health = ShadowConsts.ShadowHealth };
            NewBase(target, id, roleId, position);
            return target;
        }

        public override Unit Clone()
        {
            ShadowUnit target = new();
            CloneBase(target);
            return target;
        }

        public override bool HasAction(Action action) => action is IShadowAction;

        public override bool HasActionLeft(Game game)
        {
            if (!Alive || !base.HasActionLeft(game))
            {
                return false;
            }
            UnitView view = GetView(game);
            return view.CanMoveGrids.Length > 0
                || view.CanBuildFortGrids.Length > 0
                || view.CanBuildWallGrids.Length > 0
                || view.CanDemolishGrids.Length > 0;
        }

        public override UnitView GetView(Game game)
        {
            bool active = Alive && !End;
            bool canBuild = active && IsFirstAction
                && !game.Board.GetGrid(Position)!.HasBlockingBuilding(game);
            return ShadowUnitView.New(
                canMoveGrids: Box.New(active && MoveUsed < MoveDistance
                    ? MoveEntries(game)
                    : Array.Empty<(Node node, int distance, Node? lastNode)>()),
                canBuildFortGrids: Box.New(canBuild ? BuildFortNodes(game) : Array.Empty<Node>()),
                canBuildWallGrids: Box.New(canBuild ? BuildWallNodes(game) : Array.Empty<Node>()),
                canDemolishGrids: Box.New(DemolishNodes(game))
            )!;
        }
    }

    public class ShadowUnitView : UnitView
    {
        private ShadowUnitView() : base(null!, null!, null!, null!) { }

        public static ShadowUnitView? New(
            Box<(Node node, int distance, Node? lastNode)[]> canMoveGrids,
            Box<Node[]> canBuildFortGrids,
            Box<Node[]> canBuildWallGrids,
            Box<Node[]> canDemolishGrids)
        {
            ShadowUnitView target = new();
            return NewBase(target, canMoveGrids, canBuildFortGrids, canBuildWallGrids, canDemolishGrids)
                ? target : null;
        }

        public override UnitView Clone()
        {
            ShadowUnitView target = new();
            CloneBase(target);
            return target;
        }
    }

    public class ShadowUnitBuilder : UnitBuilder
    {
        public override Unit? Build()
        {
            if (Built is not null)
            {
                return Built;
            }
            if (Id is not int id || RoleId is not int roleId || Position is not Node position)
            {
                return null;
            }
            Built = ShadowUnit.New(id, roleId, position);
            return Built;
        }
    }

    public class ShadowBuilder : RoleBuilder
    {
        public Node Position { get; private set; }

        private ShadowBuilder(UnitBuilder[] unitBuilder) : base(unitBuilder) { }

        public static ShadowBuilder? New(UnitBuilder[] unitBuilder, Node position)
        {
            if (unitBuilder.Length != 1 || unitBuilder[0] is not ShadowUnitBuilder)
            {
                return null;
            }
            return new(unitBuilder) { Position = position };
        }

        public override bool CanInitialStay(Board board)
        {
            return board.GetGrid(Position) is Grid grid && grid.CanInitialStay();
        }

        public override Role? Build()
        {
            if (Built is not null)
            {
                return Built;
            }
            if (Id is not int id || PlayerId is not int playerId)
            {
                return null;
            }
            UnitBuilder unitBuilder = UnitBuilder[0];
            if (unitBuilder.Position is null)
            {
                unitBuilder.SetPosition(Position);
            }
            if (unitBuilder.Position != Position || unitBuilder.Build() is not ShadowUnit unit)
            {
                return null;
            }
            Built = Shadow.New(id, playerId, unit);
            return Built;
        }
    }
}
