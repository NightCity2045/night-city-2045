using System;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.IoC;

namespace Content.Client.Administration.UI.CustomControls
{
    public sealed class UICommandButton : CommandButton
    {
        public Type? WindowType { get; set; }
        private DefaultWindow? _window;

        protected override void Execute(ButtonEventArgs obj)
        {
            if (WindowType == null)
                return;

            if (_window == null || _window.Disposed)
            {
                var instance = IoCManager.Resolve<IDynamicTypeFactory>().CreateInstance(WindowType);
                if (instance is not DefaultWindow window)
                    return;

                _window = window;
            }

            _window.OpenCentered();
        }
    }
}
