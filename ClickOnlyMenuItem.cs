namespace SonarFavoritePicker;

internal class ClickOnlyMenuItem : ToolStripMenuItem
{
    private bool activating;

    public ClickOnlyMenuItem(string text) : base(text)
    {
        // WinForms normally starts a timer to show submenus on hover. Gate
        // opening as well, since moving between menu items can request it too.
        DropDown.Opening += (_, e) => e.Cancel = !activating;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        Select();
        // Skip ToolStripMenuItem's automatic hover expansion timer.
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        activating = e.Button is MouseButtons.Left or MouseButtons.Right;
        try { base.OnMouseDown(e); }
        finally { activating = false; }
    }

    protected override bool ProcessCmdKey(ref Message m, Keys keyData)
    {
        if (TryKeyboardActivation(keyData)) return true;
        return base.ProcessCmdKey(ref m, keyData);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (TryKeyboardActivation(keyData)) return true;
        return base.ProcessDialogKey(keyData);
    }

    private bool TryKeyboardActivation(Keys keyData)
    {
        if (!Selected || keyData is not (Keys.Enter or Keys.Space or Keys.Right)) return false;
        activating = true;
        try
        {
            ShowDropDown();
            if (DropDownItems.Count > 0) DropDownItems[0].Select();
            return true;
        }
        finally { activating = false; }
    }
}
