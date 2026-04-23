namespace Content.Server.Bible.Components
{
    public sealed partial class BibleComponent
    {
        /// <summary>
        /// Chance to revive a dead body when struck with the bible.
        /// </summary>
        [DataField]
        [ViewVariables(VVAccess.ReadWrite)]
        public float ReviveDeadChance;

        /// <summary>
        /// The percentage of the dead threshold to leave after a successful revival.
        /// A value of 0.99 means the target comes back with roughly 99% of lethal damage still on them.
        /// </summary>
        [DataField]
        [ViewVariables(VVAccess.ReadWrite)]
        public float ReviveDeadDamageFraction = 0.99f;

        /// <summary>
        /// Refill the target's bloodstream back to its normal blood volume on a successful revival.
        /// This does not stop bleeding.
        /// </summary>
        [DataField]
        [ViewVariables(VVAccess.ReadWrite)]
        public bool RestoreBloodOnRevive;

        /// <summary>
        /// If true, each corpse can only be targeted by bible revival once.
        /// </summary>
        [DataField]
        [ViewVariables(VVAccess.ReadWrite)]
        public bool ReviveDeadOncePerBody = true;
    }
}
