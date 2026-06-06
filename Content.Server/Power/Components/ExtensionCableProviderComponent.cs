using Content.Server.Power.EntitySystems;

using Content.Server.Power.EntitySystems;
using Content.Server.Power.NodeGroups;

namespace Content.Server.Power.Components
{
    [RegisterComponent]
    [Access(typeof(ExtensionCableSystem))]
    public sealed partial class ExtensionCableProviderComponent : BaseApcNetComponent
    {
        /// <summary>
        ///     The max distance this can connect to <see cref="ExtensionCableReceiverComponent"/>s from.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("transferRange")]
        public int TransferRange { get; set; } = 3;

        [ViewVariables] public List<ExtensionCableReceiverComponent> LinkedReceivers { get; } = new();

        /// <summary>
        ///     If <see cref="ExtensionCableReceiverComponent"/>s should consider connecting to this.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        public bool Connectable { get; set; } = true;

        protected override void AddSelfToNet(IApcNet apcNet)
        {
            // Extension cables don't add themselves to the net directly in the same way APCs do.
        }

        protected override void RemoveSelfFromNet(IApcNet apcNet)
        {
        }
    }
}
