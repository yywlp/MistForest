using System;
using System.Collections.Generic;
using MistForest.Core.Consts;
using MistForest.Core.GameMap;
using MistForest.Core.Utils;
using ZLinq;
using static MistForest.Core.GameMap.Board;
using static MistForest.Core.GameRefactored.PlayerBuilder;

namespace MistForest.Core.GameRefactored.Roles
{
    public interface IHiddenWolfAction { }

    public enum HiddenWolfForm
    {
        Human,
        Wolf,
    }

    public class HiddenWolfTransform : Action, IHiddenWolfAction
    {
        public static HiddenWolfTransform New(int callerUnitId) => new() { CallerUnitId = callerUnitId };
    }

    public class HiddenWolfAttack : Action, IHiddenWolfAction
    {
        public Node Target { get; private set; }

        public static HiddenWolfAttack New(int callerUnitId, Node target)
        {
            return new() { CallerUnitId = callerUnitId, Target = target };
        }
    }

    public class HiddenWolf : Role
    {
        private readonly Unit[] _units;
        public HiddenWolfUnit Unit => (HiddenWolfUnit)_units[0];
        public override bool HasStartedTurn => Unit.HasTransformedThisTurn || base.HasStartedTurn;

        private HiddenWolf(HiddenWolfUnit unit) => _units = new Unit[] { unit };

        internal static HiddenWolf New(int id, int playerId, HiddenWolfUnit unit)
        {
            HiddenWolf target = new(unit);
            NewBase(target, id, playerId);
            return target;
        }

        public override Role Clone()
        {
            return new HiddenWolf((HiddenWolfUnit)Unit.Clone()) { Id = Id, PlayerId = PlayerId };
        }

        public override bool JudgeAction(Action action, Game game)
        {
            if (!Unit.Alive || Unit.End)
            {
                return false;
            }
            return action switch
            {
                HiddenWolfTransform _ => Unit.CanTransform(),
                GeneralMove move => Unit.CanMove(move, game),
                GeneralEnd _ => Unit.CanEnd(),
                GeneralSurrender _ => Unit.CanSurrender(),
                GeneralBuildFort buildFort => Unit.CanBuildFort(buildFort, game),
                GeneralBuildWall buildWall => Unit.CanBuildWall(buildWall, game),
                HiddenWolfAttack attack => Unit.CanAttack(attack, game),
                GeneralDemolish demolish => Unit.CanDemolish(demolish, game),
                _ => false,
            };
        }

        public override void ExecuteAction(Action action, Game game)
        {
            if (action is HiddenWolfTransform)
            {
                Unit.Transform(game);
                return;
            }
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
                case HiddenWolfAttack attack:
                    Unit.HiddenWolfAttack(attack, game);
                    break;
                case GeneralDemolish demolish:
                    Unit.GeneralDemolish(demolish, game);
                    break;
            }
        }

        public override bool HasActionLeft(Game game) => !End(game);

        public override ReadOnlySpan<Unit> GetUnits() => _units;
    }

    /// <summary>
    /// Stores both forms, per-turn transformation and the movement allowance after a wolf attack.
    /// </summary>
    public class HiddenWolfUnit : Unit
    {
        public HiddenWolfForm Form { get; private set; }
        public bool HasTransformedThisTurn { get; private set; }
        // MoveUsed counts movement within the current pre-attack or post-attack phase.
        protected override int MoveDistance => Form == HiddenWolfForm.Human
            ? HiddenWolfConsts.HiddenWolfHumanMoveDistance
            : AttackUsed == 0 ? HiddenWolfConsts.HiddenWolfWolfMoveDistance
            : HiddenWolfConsts.HiddenWolfPostAttackMoveDistance;
        public override int AttackRange => Form == HiddenWolfForm.Human
            ? HiddenWolfConsts.HiddenWolfHumanAttackRange : HiddenWolfConsts.HiddenWolfWolfAttackRange;
        public int AttackDamage => HiddenWolfConsts.HiddenWolfAttackDamage;
        public override bool CanBeDetected => Form != HiddenWolfForm.Human;
        public override bool CanBeAttached => Form != HiddenWolfForm.Human;
        private bool CanMoveGeneral => Alive && !End && MoveUsed < MoveDistance
            && (Form == HiddenWolfForm.Wolf || AttackUsed == 0);
        private bool CanAttackGeneral => Alive && !End && AttackUsed == 0
            && MoveUsed <= (Form == HiddenWolfForm.Wolf ? HiddenWolfConsts.HiddenWolfPreAttackMoveDistance : 0);

        private HiddenWolfUnit() { }

        public static HiddenWolfUnit New(int id, int roleId, Node position)
        {
            HiddenWolfUnit target = new() { Health = HiddenWolfConsts.HiddenWolfHealth };
            NewBase(target, id, roleId, position);
            return target;
        }

        public override Unit Clone()
        {
            HiddenWolfUnit target = new()
            {
                Form = Form,
                HasTransformedThisTurn = HasTransformedThisTurn,
            };
            CloneBase(target);
            return target;
        }

        public override bool HasAction(Action action) => action is IHiddenWolfAction;

        // TODO: Integrate shared silence: prevent transformation while silenced. Human form
        // still receives silence, but must not give the caster a silence-success response.
        internal bool CanTransform() => Alive && !End && IsFirstAction && !HasTransformedThisTurn;

        internal void Transform(Game game)
        {
            Form = Form == HiddenWolfForm.Human ? HiddenWolfForm.Wolf : HiddenWolfForm.Human;
            _lastMoveEntries = null;
            HasTransformedThisTurn = true;
            if (Form == HiddenWolfForm.Human)
            {
                TemporaryShield = Math.Max(TemporaryShield, HiddenWolfConsts.HiddenWolfHumanShield);
            }
            game.Events.Add(HiddenWolfTransformEvent.New(Id, Form));
        }

        protected override int ReduceDirectDamage(int strength, DamageType damageType)
        {
            return (Form == HiddenWolfForm.Human && damageType == DamageType.Ranged)
                || (Form == HiddenWolfForm.Wolf && damageType == DamageType.Melee)
                ? strength / 2 : strength;
        }

        public override void PrepareTurn(Game game)
        {
            base.PrepareTurn(game);
            HasTransformedThisTurn = false;
        }

        internal override bool CanMove(GeneralMove move, Game game)
        {
            return CanMoveGeneral && base.CanMove(move, game);
        }

        internal Node[] AttackNodes(Game game)
        {
            return FarAttackNodes(game);
        }

        internal bool CanAttack(HiddenWolfAttack attack, Game game)
        {
            return CanAttackGeneral
                && AttackNodes(game).AsValueEnumerable().Any(node => node == attack.Target);
        }

        public override bool HasActionLeft(Game game)
        {
            if (!Alive || End)
            {
                return false;
            }
            HiddenWolfUnitView view = (HiddenWolfUnitView)GetView(game);
            return view.CanTransform || view.CanMoveGrids.Length > 0
                || view.CanAttackGrids.Length > 0
                || view.CanBuildFortGrids.Length > 0
                || view.CanBuildWallGrids.Length > 0
                || view.CanDemolishGrids.Length > 0;
        }

        public override UnitView GetView(Game game)
        {
            bool active = Alive && !End;
            bool canBuild = active && IsFirstAction
                && !game.Board.GetGrid(Position)!.HasBlockingBuilding(game);
            return HiddenWolfUnitView.New(
                canMoveGrids: Box.New(CanMoveGeneral
                    ? MoveEntries(game)
                    : Array.Empty<(Node node, int distance, Node? lastNode)>()),
                canAttackGrids: Box.New(CanAttackGeneral ? AttackNodes(game) : Array.Empty<Node>()),
                canBuildFortGrids: Box.New(canBuild ? BuildFortNodes(game) : Array.Empty<Node>()),
                canBuildWallGrids: Box.New(canBuild ? BuildWallNodes(game) : Array.Empty<Node>()),
                canDemolishGrids: Box.New(active ? DemolishNodes(game) : Array.Empty<Node>()),
                form: Form,
                canTransform: CanTransform(),
                temporaryShield: TemporaryShield
            )!;
        }

        internal void HiddenWolfAttack(HiddenWolfAttack attack, Game game)
        {
            ++AttackUsed;
            Grid grid = game.Board.GetGrid(attack.Target)!;
            int playerId = game.PlayerIdFromUnitId(Id);
            int? attackedBuildingId = null;
            Unit[] enemies = Array.Empty<Unit>();

            // A standing building shields its units, including on the destroying hit.
            if (grid.HasBlockingBuilding(game))
            {
                Building building = game.Buildings[grid.GridBuilding!.Value];
                attackedBuildingId = building.Id;
                building.ReceiveDirectDamage(AttackDamage, game, playerId);
            }
            else
            {
                enemies = grid.Units
                    .AsValueEnumerable()
                    .Select(unitId => game.GetUnit(unitId)!)
                    .Where(unit => unit.Alive
                        && !game.IsPlayerAllied(playerId, game.PlayerIdFromUnitId(unit.Id)))
                    .ToArray();
                foreach (Unit enemy in enemies)
                {
                    enemy.ReceiveDirectDamage(AttackDamage, game,
                        Form == HiddenWolfForm.Human ? DamageType.Ranged : DamageType.Melee);
                }
            }
            game.Events.Add(HiddenWolfAttackEvent.New(
                attackerId: Id,
                unitIds: enemies.AsValueEnumerable().Select(unit => unit.Id).ToArray(),
                buildingId: attackedBuildingId,
                target: attack.Target));
            if (Form == HiddenWolfForm.Wolf)
            {
                MoveUsed = 0;
                _lastMoveEntries = null;
                Unit? nearest = NearestEnemy(attack.Target, game);
                int? distance = nearest is null ? null : game.Board.Distance(attack.Target, nearest.Position);
                game.Events.Add(HiddenWolfInstinctEvent.New(playerId, Id, distance));
            }
        }

        private Unit? NearestEnemy(Node origin, Game game)
        {
            int playerId = game.PlayerIdFromUnitId(Id);
            int nearestDistance = int.MaxValue;
            List<Unit> nearest = new();
            foreach (Player player in game.Players)
            {
                if (game.IsPlayerAllied(playerId, player.Id))
                {
                    continue;
                }
                foreach (Role role in player.Roles)
                {
                    foreach (Unit unit in role.GetUnits())
                    {
                        if (!unit.Alive || !unit.CanBeDetected
                            || game.Board.Distance(origin, unit.Position) is not int distance)
                        {
                            continue;
                        }
                        if (distance < nearestDistance)
                        {
                            nearestDistance = distance;
                            nearest.Clear();
                        }
                        if (distance == nearestDistance)
                        {
                            nearest.Add(unit);
                        }
                    }
                }
            }
            return nearest.Count switch
            {
                0 => null,
                1 => nearest[0],
                _ => nearest[game.Board.Random.NextInt(0, nearest.Count)!.Value],
            };
        }
    }

    public class HiddenWolfUnitView : UnitView
    {
        public Node[] CanAttackGrids { get; private set; }
        public HiddenWolfForm Form { get; private set; }
        public bool CanTransform { get; private set; }
        public int TemporaryShield { get; private set; }

        private HiddenWolfUnitView(Node[] canAttackGrids) : base(null!, null!, null!, null!)
        {
            CanAttackGrids = canAttackGrids;
        }

        public static HiddenWolfUnitView? New(
            Box<(Node node, int distance, Node? lastNode)[]> canMoveGrids,
            Box<Node[]> canAttackGrids,
            Box<Node[]> canBuildFortGrids,
            Box<Node[]> canBuildWallGrids,
            Box<Node[]> canDemolishGrids,
            HiddenWolfForm form = HiddenWolfForm.Human,
            bool canTransform = false,
            int temporaryShield = 0)
        {
            if (canAttackGrids.TryTake() is not Node[] attackGrids)
            {
                return null;
            }
            HiddenWolfUnitView target = new(attackGrids)
            {
                Form = form,
                CanTransform = canTransform,
                TemporaryShield = temporaryShield,
            };
            return NewBase(target, canMoveGrids, canBuildFortGrids, canBuildWallGrids, canDemolishGrids)
                ? target : null;
        }

        public override UnitView Clone()
        {
            HiddenWolfUnitView target = new((Node[])CanAttackGrids.Clone())
            {
                Form = Form,
                CanTransform = CanTransform,
                TemporaryShield = TemporaryShield,
            };
            CloneBase(target);
            return target;
        }
    }

    public class HiddenWolfAttackEvent : AttackEvent
    {
        private HiddenWolfAttackEvent(int attackerId, int[] unitIds, int? buildingId, Node target)
            : base(attackerId, unitIds, buildingId, target) { }

        public static HiddenWolfAttackEvent New(int attackerId, int[] unitIds, int? buildingId, Node target)
        {
            return new(attackerId, unitIds, buildingId, target);
        }
    }

    /// <summary>Public form change, without disclosing the unit's position.</summary>
    public class HiddenWolfTransformEvent : Event
    {
        public int UnitId { get; }
        public HiddenWolfForm Form { get; }

        private HiddenWolfTransformEvent(int unitId, HiddenWolfForm form)
        {
            UnitId = unitId;
            Form = form;
        }

        public static HiddenWolfTransformEvent New(int unitId, HiddenWolfForm form) => new(unitId, form);
    }

    /// <summary>Distance from the attack grid to the nearest detectable enemy after a wolf attack.</summary>
    public class HiddenWolfInstinctEvent : Event
    {
        public int PlayerId { get; }
        public int UnitId { get; }
        public int? Distance { get; }

        private HiddenWolfInstinctEvent(int playerId, int unitId, int? distance)
        {
            PlayerId = playerId;
            UnitId = unitId;
            Distance = distance;
        }

        public override bool IsVisibleTo(int playerId) => playerId == PlayerId;

        public static HiddenWolfInstinctEvent New(int playerId, int unitId, int? distance)
            => new(playerId, unitId, distance);
    }

    public class HiddenWolfUnitBuilder : UnitBuilder
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
            Built = HiddenWolfUnit.New(id, roleId, position);
            return Built;
        }
    }

    public class HiddenWolfBuilder : RoleBuilder
    {
        public Node Position { get; private set; }

        private HiddenWolfBuilder(UnitBuilder[] unitBuilder) : base(unitBuilder) { }

        public static HiddenWolfBuilder? New(UnitBuilder[] unitBuilder, Node position)
        {
            if (unitBuilder.Length != 1 || unitBuilder[0] is not HiddenWolfUnitBuilder)
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
            if (unitBuilder.Position != Position || unitBuilder.Build() is not HiddenWolfUnit unit)
            {
                return null;
            }
            Built = HiddenWolf.New(id, playerId, unit);
            return Built;
        }
    }
}
