using System;
using System.Collections.Generic;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Microsoft.UI.Xaml.Controls;

namespace Cantus.Client.Views;

/// <summary>
/// Builds and syncs the theme menu content: one radio item per mode in
/// <see cref="ThemeManager.AvailableModes"/>, a separator, then the
/// post-processing effects toggle. Every theme menu uses this, so they list
/// the same items in the same order.
/// </summary>
internal static class ThemeMenuItems
{
    public const string POST_PROCESSING_TEXT = "Post-processing effects";

    public static void Populate(
        IList<MenuFlyoutItemBase> items,
        string groupName,
        Action<ThemeMode> onThemeSelected,
        Action<bool> onPostProcessingToggled)
    {
        foreach (ThemeMode mode in ThemeManager.AvailableModes)
        {
            RadioMenuFlyoutItem item = new()
            {
                Text = mode.GetDisplayName(),
                GroupName = groupName,
                Tag = mode,
            };
            item.Click += (sender, e) => onThemeSelected(mode);
            items.Add(item);
        }

        items.Add(new MenuFlyoutSeparator());

        ToggleMenuFlyoutItem toggle = new() { Text = POST_PROCESSING_TEXT };
        toggle.Click += (sender, e) => onPostProcessingToggled(toggle.IsChecked);
        items.Add(toggle);
    }

    public static void Sync(IEnumerable<MenuFlyoutItemBase> items, ThemeMode currentMode, bool isPostProcessingEnabled)
    {
        foreach (MenuFlyoutItemBase item in items)
        {
            // RadioMenuFlyoutItem derives from ToggleMenuFlyoutItem, so it is matched first.
            if (item is RadioMenuFlyoutItem radio && radio.Tag is ThemeMode mode)
            {
                radio.IsChecked = mode == currentMode;
            }
            else if (item is ToggleMenuFlyoutItem toggle)
            {
                toggle.IsChecked = isPostProcessingEnabled;
            }
        }
    }
}
