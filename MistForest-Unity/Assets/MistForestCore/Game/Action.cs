using MistForest.Core.Consts;
using MistForest.Core.GameRefactored.Roles;
using static MistForest.Core.GameMap.Board;

namespace MistForest.Core.GameRefactored
{
    public abstract class Action
    {
        public int CallerUnitId { get; protected set; }

        protected static void NewBase(Action target, int callerUnitId)
        {
            target.CallerUnitId = callerUnitId;
        }
    }

    public interface IScoutAction { }

    public interface IScoutOnlyAction: IScoutAction { }

    public class GeneralMove : Action, IScoutAction, ISniperAction, IWolfAction, IAlphaAction, IInkAction, IArmigerAction, IHeavyAction, IHiddenWolfAction, IShadowAction
    {
        public Node Target { get; private set; }

        public static GeneralMove New(int callerUnitId, Node target)
        {
            GeneralMove newTarget = new() { Target = target };
            NewBase(newTarget, callerUnitId);
            return newTarget;
        }
    }

    public class GeneralEnd : Action, IScoutAction, ISniperAction, IWolfAction, IAlphaAction, IInkAction, IArmigerAction, IHeavyAction, IHiddenWolfAction, IShadowAction
    {
        public static GeneralEnd New(int callerUnitId)
        {
            GeneralEnd newTarget = new();
            NewBase(newTarget, callerUnitId);
            return newTarget;
        }
    }

    public class GeneralSurrender : Action, IScoutAction, ISniperAction, IWolfAction, IAlphaAction, IInkAction, IArmigerAction, IHeavyAction, IHiddenWolfAction, IShadowAction
    {
        public static GeneralSurrender New(int callerUnitId)
        {
            GeneralSurrender newTarget = new();
            NewBase(newTarget, callerUnitId);
            return newTarget;
        }
    }

    public class GeneralBuildFort : Action, ISniperAction, IWolfAction, IAlphaAction, IInkAction, IArmigerAction, IHeavyAction, IHiddenWolfAction, IShadowAction
    {
        public Node Target { get; private set; }

        public static GeneralBuildFort New(int callerUnitId, Node target)
        {
            GeneralBuildFort newTarget = new() { Target = target };
            NewBase(newTarget, callerUnitId);
            return newTarget;
        }
    }

    public class GeneralBuildWall : Action, ISniperAction, IWolfAction, IAlphaAction, IInkAction, IArmigerAction, IHeavyAction, IHiddenWolfAction, IShadowAction
    {
        public Node[] Targets { get; private set; }

        private GeneralBuildWall(Node[] targets)
            : base()
        {
            Targets = targets;
        }

        public static GeneralBuildWall? New(int callerUnitId, Node[] targets)
        {
            if (targets.Length > RoleConsts.MaxWallCount || targets.Length < 1)
            {
                return null;
            }
            GeneralBuildWall newTarget = new(targets);
            NewBase(newTarget, callerUnitId);
            return newTarget;
        }
    }

    public class GeneralDemolish : Action, ISniperAction, IAlphaAction, IInkAction, IArmigerAction, IHeavyAction, IHiddenWolfAction, IShadowAction
    {
        public Node Target { get; private set; }

        public static GeneralDemolish New(int callerUnitId, Node target)
        {
            GeneralDemolish newTarget = new() { Target = target };
            NewBase(newTarget, callerUnitId);
            return newTarget;
        }
    }
}
