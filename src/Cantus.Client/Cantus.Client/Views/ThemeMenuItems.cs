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
/// effect in <see cref="VisualEffectsViewModel.AvailableEffects"/>, then a
/// submenu picking the VU meter's level source. Every theme menu uses this, so
/// they list the same items in the same order.
/// </summary>
internal static class ThemeMenuItems
{
    public static void Populate(
        IList<MenuFlyoutItemBase> items,
        string groupName,
        Action<ThemeMode> onThemeSelected,
        Action<VisualEffect, bool> onEffectToggled,
        Action<VuMeterSource> onMeterSourceSelected)
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

        // Filled by Sync once the view model says which sources this platform has.
        items.Add(new MenuFlyoutSubItem
        {
            Text = "VU meter levels",
            Tag = new MeterSourceMenu($"{groupName}-VuMeter", onMeterSourceSelected),
            Visibility = Microsoft.UI.Xaml.Visibility.Collapsed,
        });
    }

    public static void Sync(
        IEnumerable<MenuFlyoutItemBase> items,
        ThemeMode currentMode,
        VisualEffectsViewModel effects,
        VuMeterViewModel meter)
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
            else if (item is MenuFlyoutSubItem sub && sub.Tag is MeterSourceMenu menu)
            {
                SyncMeterSources(sub, menu, meter);
            }
        }
    }

    private static void SyncMeterSources(MenuFlyoutSubItem sub, MeterSourceMenu menu, VuMeterViewModel meter)
    {
        sub.Visibility = meter.SourcePickerVisibility;
        if (sub.Items.Count == 0)
        {
            sub.Items.Add(new MenuFlyoutItem { IsEnabled = false });
            sub.Items.Add(new MenuFlyoutSeparator());
            foreach (VuMeterSource source in meter.AvailableSources)
            {
                RadioMenuFlyoutItem radio = new()
                {
                    Text = source.GetDisplayName(),
                    GroupName = menu.GroupName,
                    Tag = source,
                };
                radio.Click += (sender, e) => menu.OnSelected(source);
                sub.Items.Add(radio);
            }
        }

        foreach (MenuFlyoutItemBase child in sub.Items)
        {
            if (child is RadioMenuFlyoutItem radio && radio.Tag is VuMeterSource source)
            {
                radio.IsChecked = source == meter.SelectedSource;
            }
            else if (child is MenuFlyoutItem summary && child is not RadioMenuFlyoutItem)
            {
                summary.Text = meter.ModeSummary;
            }
        }
    }

    private sealed record MeterSourceMenu(string GroupName, Action<VuMeterSource> OnSelected);
}
