using System;
using System.Collections.Generic;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Cantus.Client.Views;

/// <summary>
/// Builds and syncs the theme menu content: one radio item per mode in
/// <see cref="ThemeManager.AvailableModes"/>, a separator, then one toggle per
/// effect in <see cref="VisualEffectsViewModel.AvailableEffects"/>. Every theme
/// menu uses this, so they list the same items in the same order.
/// </summary>
internal static class ThemeMenuItems
{
    public static void Populate(
        IList<MenuFlyoutItemBase> items,
        string groupName,
        Action<ThemeMode> onThemeSelected,
        Action<VisualEffect, bool> onEffectToggled)
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

        foreach (VisualEffect effect in VisualEffectsViewModel.AvailableEffects)
        {
            ToggleMenuFlyoutItem toggle = new()
            {
                Text = effect.GetDisplayName(),
                Tag = effect,
            };
            toggle.Click += (sender, e) => onEffectToggled(effect, toggle.IsChecked);
            items.Add(toggle);
        }
    }

    public static void Sync(IEnumerable<MenuFlyoutItemBase> items, ThemeMode currentMode, VisualEffectsViewModel effects)
    {
        foreach (MenuFlyoutItemBase item in items)
        {
            if (item is RadioMenuFlyoutItem radio && radio.Tag is ThemeMode mode)
            {
                radio.IsChecked = mode == currentMode;
            }
            else if (item is ToggleMenuFlyoutItem toggle && toggle.Tag is VisualEffect effect)
            {
                toggle.IsChecked = effects.IsEnabled(effect);
            }
        }
    }
}
