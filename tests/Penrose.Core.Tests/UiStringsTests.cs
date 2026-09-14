using System.Reflection;
using Penrose.Core.Ui;

namespace Penrose.Core.Tests;

public sealed class UiStringsTests
{
    [Fact]
    public void Language_prefix_selects_catalog()
    {
        Assert.Equal(UiStrings.Zh.Settings, UiStrings.For(null).Settings);
        Assert.Equal(UiStrings.Zh.Settings, UiStrings.For("zh-CN").Settings);
        Assert.Equal(UiStrings.En.Settings, UiStrings.For("en").Settings);
        Assert.Equal(UiStrings.En.Settings, UiStrings.For("en-US").Settings);
        Assert.True(UiStrings.IsEnglish("en-GB"));
        Assert.False(UiStrings.IsEnglish("zh-CN"));
        Assert.NotEqual(UiStrings.Zh.Settings, UiStrings.En.Settings);
        Assert.NotEqual(UiStrings.Zh.IdleStatus, UiStrings.En.IdleStatus);
    }

    [Fact]
    public void Both_packs_fill_every_string()
    {
        foreach (PropertyInfo property in typeof(UiStrings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType != typeof(string) || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            Assert.False(
                string.IsNullOrWhiteSpace((string?)property.GetValue(UiStrings.Zh)),
                property.Name + " zh");
            Assert.False(
                string.IsNullOrWhiteSpace((string?)property.GetValue(UiStrings.En)),
                property.Name + " en");
        }
    }
}
