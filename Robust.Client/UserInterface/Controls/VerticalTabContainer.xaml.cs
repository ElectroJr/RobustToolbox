using System.Collections.Generic;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Maths;

namespace Robust.Client.UserInterface.Controls;

[GenerateTypedNameReferences]
public sealed partial class VerticalTabContainer : BoxContainer
{
    private readonly Dictionary<Control, BaseButton> _tabs = new();

    // Just used to order controls in case one gets removed.
    private readonly List<Control> _controls = new();

    private readonly ButtonGroup _tabGroup = new(false);

    private Control? _currentControl;

    public VerticalTabContainer()
    {
        RobustXamlLoader.Load(this);
    }

    public int AddTab(Control control, string title)
    {
        var button = new Button()
        {
            Text = title,
            Group = _tabGroup,
        };

        TabContainer.AddChild(button);
        ContentsContainer.AddChild(control);
        var index = ChildCount - 1;
        button.OnPressed += args =>
        {
            SelectTab(control);
        };

        _controls.Add(control);
        _tabs.Add(control, button);

        // Existing tabs
        if (ContentsContainer.ChildCount > 1)
        {
            control.Visible = false;
        }
        // First tab
        else
        {
            SelectTab(control);
        }

        return index;
    }

    protected override void ChildRemoved(Control child)
    {
        if (_tabs.Remove(child, out var button))
        {
            button.Dispose();
        }

        // Set the current tab to a different control
        if (_currentControl == child)
        {
            var previous = _controls.IndexOf(child) - 1;

            if (previous > -1)
            {
                var setControl = _controls[previous];
                SelectTab(setControl);
            }
            else
            {
                _currentControl = null;
            }
        }

        _controls.Remove(child);
        base.ChildRemoved(child);
    }

    private void SelectTab(Control control)
    {
        if (_currentControl != null)
        {
            _currentControl.Visible = false;
        }

        var button = _tabs[control];
        button.Pressed = true;
        control.Visible = true;
        _currentControl = control;
    }
}
