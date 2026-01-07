using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace AutoEdit.UI;

public enum AppTheme
{
    Nebula,
    Solstice,
    Graphite
}

public static class ThemeManager
{
    private static readonly Dictionary<AppTheme, Uri> ThemeUris = new()
    {
        [AppTheme.Nebula] = new Uri("Themes/ThemeNebula.xaml", UriKind.Relative),
        [AppTheme.Solstice] = new Uri("Themes/ThemeSolstice.xaml", UriKind.Relative),
        [AppTheme.Graphite] = new Uri("Themes/ThemeGraphite.xaml", UriKind.Relative)
    };

    public static void ApplyTheme(AppTheme theme)
    {
        var app = Application.Current;
        if (app == null)
            return;

        var dictionaries = app.Resources.MergedDictionaries;
        var targetUri = ThemeUris[theme];

        var existing = dictionaries.FirstOrDefault(d =>
            d.Source != null &&
            d.Source.OriginalString.Contains("Themes/Theme", StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            var index = dictionaries.IndexOf(existing);
            dictionaries.RemoveAt(index);
            dictionaries.Insert(index, new ResourceDictionary { Source = targetUri });
        }
        else
        {
            dictionaries.Insert(0, new ResourceDictionary { Source = targetUri });
        }
    }
}
