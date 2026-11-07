using System;
using System.Collections.Generic;
using System.Linq;
using MistForest.Core.Consts;
using static MistForest.Core.GameMap.Board;

namespace MistForest.Core.GameRefactored.Roles
{
    public enum ShadowLinkMode
    {
        None,
        Follow,
        Drag,
    }

    public class ShadowFollow : Action, IShadowAction
    {
        public int TargetUnitId { get; private set; }

        public static ShadowFollow New(int callerUnitId, int targetUnitId)
            => new() { CallerUnitId = callerUnitId, TargetUnitId = targetUnitId };
    }

    public class ShadowDrag : Action, IShadowAction
    {
        public int TargetUnitId { get; private set; }

        public static ShadowDrag New(int callerUnitId, int targetUnitId)
            => new() { CallerUnitId = callerUnitId, TargetUnitId = targetUnitId };
    }

    /// <summary>Records attachment selection without disclosing either unit's position.</summary>
    public class ShadowLinkEvent : Event
    {
        public int UnitId { get; }
        public int TargetUnitId { get; }
        public ShadowLinkMode Mode { get; }

        private ShadowLinkEvent(int unitId, int targetUnitId, ShadowLinkMode mode)
        {
            UnitId = unitId;
            TargetUnitId = targetUnitId;
            Mode = mode;
        }

        public static ShadowLinkEvent New(int unitId, int targetUnitId, ShadowLinkMode mode)
            => new(unitId, targetUnitId, mode);
    }

    /// <summary>Reports a copied step's result without exposing the target's coordinates.</summary>
    public class ShadowAttachmentEvent : Event
    {
        public int ShadowUnitId { get; }
        public int MovedUnitId { get; }
        public ShadowLinkMode Mode { get; }
        public bool Blocked { get; }

        private ShadowAttachmentEvent(int shadowUnitId, int movedUnitId, ShadowLinkMode mode, bool blocked)
        {
            ShadowUnitId = shadowUnitId;
            MovedUnitId = movedUnitId;
            Mode = mode;
            Blocked = blocked;
        }

        public static ShadowAttachmentEvent New(int shadowUnitId, int movedUnitId, ShadowLinkMode mode, bool blocked)
            => new(shadowUnitId, movedUnitId, mode, blocked);
    }

    /// <summary>Movement details for the copied unit; session code decides who receives them.</summary>
    public class ShadowCopyMoveEvent : Event
    {
        public int UnitId { get; }
        public Node[] Path { get; }

        private ShadowCopyMoveEvent(int unitId, Node[] path)
        {
            UnitId = unitId;
            Path = (Node[])path.Clone();
        }

        public static ShadowCopyMoveEvent New(int unitId, Node[] path) => new(unitId, path);
    }

    /// <summary>Resolves attachment once per original cardinal step, without recursive copies.</summary>
    internal static class ShadowAttachment
    {
        private static ShadowUnit[] Shadows(Game game)
            => game.Roles.OfType<Shadow>().Select(role => role.Unit).ToArray();

        internal static void OnMoved(Unit actor, Node from, Node to, Game game)
        {
            if (from == to || !actor.Alive || !actor.CanBeAttached)
            {
                return;
            }
            int dx = game.Board.RowOf(to)!.Value - game.Board.RowOf(from)!.Value;
            int dy = game.Board.ColOf(to)!.Value - game.Board.ColOf(from)!.Value;
            if (actor is ShadowUnit)
            {
                // Match the same direction order used to construct Shadow's validated path.
                bool found = false;
                foreach (var direction in MapConsts.MoveManhattan)
                {
                    if (ShadowUnit.Step(from, direction.dx, direction.dy, game) == to)
                    {
                        (dx, dy) = direction;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    return;
                }
            }
            // Teleports need their own displacement rules when those roles are migrated.
            if (Math.Abs(dx) + Math.Abs(dy) != 1)
            {
                return;
            }
            Dictionary<int, bool> outcomes = new();
            foreach (ShadowUnit shadow in Shadows(game))
            {
                if (!shadow.Alive || shadow.TargetUnitId is not int targetId)
                {
                    continue;
                }
                Unit? moved = shadow.LinkMode switch
                {
                    ShadowLinkMode.Follow when targetId == actor.Id => shadow,
                    ShadowLinkMode.Drag when shadow.Id == actor.Id
                        && game.PlayerIdFromUnitId(shadow.Id) == game.CurrentPlayerId => game.GetUnit(targetId),
                    _ => null,
                };
                if (moved is null || !moved.Alive || !moved.CanBeAttached)
                {
                    continue;
                }
                if (outcomes.TryGetValue(moved.Id, out bool previousAllowed))
                {
                    game.Events.Add(ShadowAttachmentEvent.New(shadow.Id, moved.Id, shadow.LinkMode, !previousAllowed));
                    continue;
                }
                Node origin = moved.Position;
                Node? destination = ShadowUnit.Step(origin, dx, dy, game);
                bool allowed = destination is Node target && target != origin
                    && game.Board.GetGrid(target)!.CanStay(game.PlayerIdFromUnitId(moved.Id), game);
                if (allowed)
                {
                    moved.ChangePos(destination!.Value, game, notifyAttachments: false);
                    game.Events.Add(ShadowCopyMoveEvent.New(moved.Id, new[] { origin, destination.Value }));
                }
                outcomes.Add(moved.Id, allowed);
                game.Events.Add(ShadowAttachmentEvent.New(shadow.Id, moved.Id, shadow.LinkMode, !allowed));
            }
        }

        internal static void EndTurn(int playerId, Game game)
        {
            foreach (ShadowUnit shadow in Shadows(game))
            {
                if (shadow.TargetUnitId is int targetId
                    && (shadow.LinkMode == ShadowLinkMode.Follow && game.PlayerIdFromUnitId(targetId) == playerId
                        || shadow.LinkMode == ShadowLinkMode.Drag && game.PlayerIdFromUnitId(shadow.Id) == playerId))
                {
                    shadow.ClearLink();
                }
            }
        }

        internal static void RemoveUnit(int unitId, Game game)
        {
            foreach (ShadowUnit shadow in Shadows(game))
            {
                if (shadow.Id == unitId || shadow.TargetUnitId == unitId)
                {
                    shadow.ClearLink();
                }
            }
        }
    }
}
