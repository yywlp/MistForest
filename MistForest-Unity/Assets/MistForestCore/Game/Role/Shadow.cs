using System;
using System.Collections.Generic;
using System.Linq;
using MistForest.Core.Consts;
using MistForest.Core.GameMap;
using MistForest.Core.Utils;
using static MistForest.Core.GameMap.Board;
using static MistForest.Core.GameRefactored.PlayerBuilder;

namespace MistForest.Core.GameRefactored.Roles
{
    public interface IShadowAction { }

    public class ShadowAttack : Action, IShadowAction
    {
        public Node Target { get; private set; }

        public static ShadowAttack New(int callerUnitId, Node target)
            => new() { CallerUnitId = callerUnitId, Target = target };
    }

    public class ShadowAttackEvent : AttackEvent
    {
        private ShadowAttackEvent(int attackerId, int[] unitIds, int? buildingId, Node target)
            : base(attackerId, unitIds, buildingId, target) { }

        public static ShadowAttackEvent New(int attackerId, int[] unitIds, int? buildingId, Node target)
            => new(attackerId, unitIds, buildingId, target);
    }

    /// <summary>Dispatches Shadow's general actions and melee attack.</summary>
    public class Shadow : Role
    {
        private readonly Unit[] _units;
        public ShadowUnit Unit => (ShadowUnit)_units[0];
        public override bool HasStartedTurn => Unit.HasSelectedLinkThisTurn || base.HasStartedTurn;

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
                ShadowAttack attack => Unit.CanAttack(attack, game),
                ShadowFollow follow => Unit.CanSelectLink(follow.TargetUnitId, game),
                ShadowDrag drag => Unit.CanSelectLink(drag.TargetUnitId, game),
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
            if (action is ShadowFollow follow)
            {
                Unit.SelectLink(ShadowLinkMode.Follow, follow.TargetUnitId, game);
                return;
            }
            if (action is ShadowDrag drag)
            {
                Unit.SelectLink(ShadowLinkMode.Drag, drag.TargetUnitId, game);
                return;
            }
            Unit.StartExecuteAction();
            switch (action)
            {
                case GeneralMove move:
                    Unit.GeneralMove(move, game);
                    break;
                case ShadowAttack attack:
                    Unit.ShadowAttack(attack, game);
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

    /// <summary>Resolves Shadow's boundary-wrapping movement and melee attacks.</summary>
    /// <remarks>
    /// TODO: Integrate skill availability and link suspension with the shared silence system.
    /// </remarks>
    public class ShadowUnit : Unit
    {
        protected override int MoveDistance => ShadowConsts.ShadowMoveDistance;
        public override int AttackRange => ShadowConsts.ShadowAttackRange;
        public ShadowLinkMode LinkMode { get; private set; }
        public int? TargetUnitId { get; private set; }
        public bool HasSelectedLinkThisTurn { get; private set; }

        private ShadowUnit() { }

        public static ShadowUnit New(int id, int roleId, Node position)
        {
            ShadowUnit target = new() { Health = ShadowConsts.ShadowHealth };
            NewBase(target, id, roleId, position);
            return target;
        }

        public override Unit Clone()
        {
            ShadowUnit target = new()
            {
                LinkMode = LinkMode,
                TargetUnitId = TargetUnitId,
                HasSelectedLinkThisTurn = HasSelectedLinkThisTurn,
            };
            CloneBase(target);
            return target;
        }

        public override bool HasAction(Action action) => action is IShadowAction;

        private bool CanSelectLinkGeneral => Alive && !End && IsFirstAction && !HasSelectedLinkThisTurn;

        internal bool CanSelectLink(int targetUnitId, Game game)
            => CanSelectLinkGeneral && targetUnitId != Id
                && game.GetUnit(targetUnitId) is Unit target && target.Alive && target.CanBeAttached;

        private int[] AttachmentTargets(Game game)
            => CanSelectLinkGeneral
                ? game.Roles.SelectMany(role => role.GetUnits().ToArray())
                    .Where(unit => unit.Id != Id && unit.Alive && unit.CanBeAttached)
                    .Select(unit => unit.Id).ToArray()
                : Array.Empty<int>();

        internal void SelectLink(ShadowLinkMode mode, int targetUnitId, Game game)
        {
            LinkMode = mode;
            TargetUnitId = targetUnitId;
            HasSelectedLinkThisTurn = true;
            game.Events.Add(ShadowLinkEvent.New(Id, targetUnitId, mode));
        }

        internal void ClearLink()
        {
            LinkMode = ShadowLinkMode.None;
            TargetUnitId = null;
        }

        public override void PrepareTurn(Game game)
        {
            base.PrepareTurn(game);
            HasSelectedLinkThisTurn = false;
        }

        /// <summary>Resolves a cardinal step, including wrapped steps and inner line walls.</summary>
        internal static Node? Step(Node from, int dx, int dy, Game game)
        {
            Board board = game.Board;
            if (board.Move(from, dx, dy) is Node adjacent)
            {
                return board.CanLineShot(from, adjacent, MapConsts.DiagonalBlocked, _ => true)
                    ? adjacent : null;
            }
            int row = board.RowOf(from)!.Value;
            int col = board.ColOf(from)!.Value;
            // Board stores only inner line walls; wrapping crosses no inner edge.
            return board.At((row - 1 + dx + board.Height) % board.Height + 1,
                (col - 1 + dy + board.Width) % board.Width + 1);
        }

        /// <summary>Enumerates cardinal neighbors, checking inner line walls before deduplication.</summary>
        private static IEnumerable<Node> Neighbors(Node from, Game game)
        {
            HashSet<Node> seen = new() { from };
            foreach ((int dx, int dy) in MapConsts.MoveManhattan)
            {
                if (Step(from, dx, dy, game) is Node to && seen.Add(to))
                {
                    yield return to;
                }
            }
        }

        protected override (Node node, int distance, Node? lastNode)[] MoveEntries(Game game)
        {
            List<(Node node, int distance, Node? lastNode)> entries = new();
            Queue<(Node node, int distance)> queue = new();
            HashSet<Node> visited = new() { Position };
            Func<Node, bool> canEnter = MovePredicate(game);
            queue.Enqueue((Position, 0));
            while (queue.Count > 0)
            {
                var (from, distance) = queue.Dequeue();
                if (distance >= MoveDistance - MoveUsed)
                {
                    continue;
                }
                foreach (Node to in Neighbors(from, game))
                {
                    if (visited.Contains(to) || !canEnter(to))
                    {
                        continue;
                    }
                    visited.Add(to);
                    entries.Add((to, distance + 1, from));
                    queue.Enqueue((to, distance + 1));
                }
            }
            return entries.ToArray();
        }

        internal override bool CanMove(GeneralMove move, Game game)
            => AttackUsed == 0 && base.CanMove(move, game);

        private bool CanAttackGeneral => Alive && !End && MoveUsed == 0 && AttackUsed == 0;

        internal Node[] AttackNodes(Game game)
            => Neighbors(Position, game)
                .Where(node => game.Board.GetGrid(node)!.Terrain is Terrain.Ground or Terrain.Whirlpool)
                .ToArray();

        internal bool CanAttack(ShadowAttack attack, Game game)
            => CanAttackGeneral && AttackNodes(game).Contains(attack.Target);

        internal void ShadowAttack(ShadowAttack attack, Game game)
        {
            ++AttackUsed;
            End = true;
            Grid grid = game.Board.GetGrid(attack.Target)!;
            int playerId = game.PlayerIdFromUnitId(Id);
            int? buildingId = null;
            Unit[] enemies = Array.Empty<Unit>();
            // A standing building shields its occupants even when this attack destroys it.
            if (grid.HasBlockingBuilding(game))
            {
                Building building = game.Buildings[grid.GridBuilding!.Value];
                buildingId = building.Id;
                building.ReceiveDirectDamage(ShadowConsts.ShadowAttackDamage, game, playerId);
            }
            else
            {
                enemies = grid.Units.Select(id => game.GetUnit(id)!)
                    .Where(unit => unit.Alive
                        && !game.IsPlayerAllied(playerId, game.PlayerIdFromUnitId(unit.Id)))
                    .ToArray();
                foreach (Unit enemy in enemies)
                {
                    enemy.ReceiveDirectDamage(ShadowConsts.ShadowAttackDamage, game, DamageType.Melee);
                }
            }
            game.Events.Add(ShadowAttackEvent.New(Id, enemies.Select(unit => unit.Id).ToArray(),
                buildingId, attack.Target));
        }

        public override bool HasActionLeft(Game game)
        {
            if (!Alive || !base.HasActionLeft(game))
            {
                return false;
            }
            ShadowUnitView view = (ShadowUnitView)GetView(game);
            return view.CanFollowUnitIds.Length > 0 || view.CanDragUnitIds.Length > 0
                || view.CanAttackGrids.Length > 0 || view.CanMoveGrids.Length > 0
                || view.CanBuildFortGrids.Length > 0
                || view.CanBuildWallGrids.Length > 0
                || view.CanDemolishGrids.Length > 0;
        }

        public override UnitView GetView(Game game)
        {
            bool active = Alive && !End;
            bool canBuild = active && IsFirstAction
                && !game.Board.GetGrid(Position)!.HasBlockingBuilding(game);
            int[] attachmentTargets = AttachmentTargets(game);
            return ShadowUnitView.New(
                canMoveGrids: Box.New(active && AttackUsed == 0 && MoveUsed < MoveDistance
                    ? MoveEntries(game)
                    : Array.Empty<(Node node, int distance, Node? lastNode)>()),
                canBuildFortGrids: Box.New(canBuild ? BuildFortNodes(game) : Array.Empty<Node>()),
                canBuildWallGrids: Box.New(canBuild ? BuildWallNodes(game) : Array.Empty<Node>()),
                canDemolishGrids: Box.New(DemolishNodes(game)),
                canAttackGrids: Box.New(CanAttackGeneral ? AttackNodes(game) : Array.Empty<Node>()),
                canFollowUnitIds: Box.New(attachmentTargets),
                canDragUnitIds: Box.New((int[])attachmentTargets.Clone()),
                linkMode: LinkMode,
                targetUnitId: TargetUnitId,
                hasSelectedLinkThisTurn: HasSelectedLinkThisTurn
            )!;
        }
    }

    public class ShadowUnitView : UnitView
    {
        public Node[] CanAttackGrids { get; private set; } = Array.Empty<Node>();
        public int[] CanFollowUnitIds { get; private set; } = Array.Empty<int>();
        public int[] CanDragUnitIds { get; private set; } = Array.Empty<int>();
        public ShadowLinkMode LinkMode { get; private set; }
        public int? TargetUnitId { get; private set; }
        public bool HasSelectedLinkThisTurn { get; private set; }

        private ShadowUnitView() : base(null!, null!, null!, null!) { }

        public static ShadowUnitView? New(
            Box<(Node node, int distance, Node? lastNode)[]> canMoveGrids,
            Box<Node[]> canBuildFortGrids,
            Box<Node[]> canBuildWallGrids,
            Box<Node[]> canDemolishGrids,
            Box<Node[]> canAttackGrids,
            Box<int[]> canFollowUnitIds,
            Box<int[]> canDragUnitIds,
            ShadowLinkMode linkMode = ShadowLinkMode.None,
            int? targetUnitId = null,
            bool hasSelectedLinkThisTurn = false)
        {
            if (canAttackGrids.TryTake() is not Node[] attackGrids
                || canFollowUnitIds.TryTake() is not int[] followIds
                || canDragUnitIds.TryTake() is not int[] dragIds)
            {
                return null;
            }
            ShadowUnitView target = new()
            {
                CanAttackGrids = attackGrids, CanFollowUnitIds = followIds, CanDragUnitIds = dragIds,
                LinkMode = linkMode, TargetUnitId = targetUnitId, HasSelectedLinkThisTurn = hasSelectedLinkThisTurn,
            };
            return NewBase(target, canMoveGrids, canBuildFortGrids, canBuildWallGrids, canDemolishGrids)
                ? target : null;
        }

        public override UnitView Clone()
        {
            ShadowUnitView target = new()
            {
                CanAttackGrids = (Node[])CanAttackGrids.Clone(),
                CanFollowUnitIds = (int[])CanFollowUnitIds.Clone(),
                CanDragUnitIds = (int[])CanDragUnitIds.Clone(),
                LinkMode = LinkMode, TargetUnitId = TargetUnitId, HasSelectedLinkThisTurn = HasSelectedLinkThisTurn,
            };
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
