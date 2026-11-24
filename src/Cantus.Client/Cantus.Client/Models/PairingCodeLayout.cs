namespace Cantus.Client.Models;

/// <summary>
/// Places the pairing screen's QR and code in a two-column grid. Beside each other (Medium and up) the code
/// sits in the star column, so its Viewbox is measured against the width left after the QR and shrinks to fit.
/// A horizontal StackPanel measured it at infinite width instead, so a long code overflowed the card.
/// Stacked (Small) both span the two columns.
/// </summary>
public static class PairingCodeLayout
{
    public const double ITEM_SPACING = 40.0;

    public static bool IsStacked(LayoutBreakpoint breakpoint) => breakpoint == LayoutBreakpoint.Small;

    public static int GetCodeRow(LayoutBreakpoint breakpoint) => IsStacked(breakpoint) ? 1 : 0;

    public static int GetCodeColumn(LayoutBreakpoint breakpoint) => IsStacked(breakpoint) ? 0 : 1;

    public static int GetColumnSpan(LayoutBreakpoint breakpoint) => IsStacked(breakpoint) ? 2 : 1;

    public static double GetRowSpacing(LayoutBreakpoint breakpoint) => IsStacked(breakpoint) ? ITEM_SPACING : 0.0;

    public static double GetColumnSpacing(LayoutBreakpoint breakpoint) => IsStacked(breakpoint) ? 0.0 : ITEM_SPACING;
}
