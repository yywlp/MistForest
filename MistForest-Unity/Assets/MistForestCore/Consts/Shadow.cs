namespace MistForest.Core.Consts
{
    /// <summary>Defines Shadow's base values. Health uses quarter-point units.</summary>
    public class ShadowConsts
    {
        public const int ShadowHealth = CombatConsts.UnitsPerPoint + CombatConsts.HalfPoint;
        public const int ShadowMoveDistance = 2;
        public const int ShadowAttackRange = 1;
        public const int ShadowAttackDamage = CombatConsts.UnitsPerPoint;
    }
}
