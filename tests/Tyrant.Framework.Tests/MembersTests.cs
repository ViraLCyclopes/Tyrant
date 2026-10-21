using System.Reflection;
using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class MembersTests
{
    // Shaped like uGUI: Toggle.onValueChanged is a public field, Slider's and TMP_InputField's are properties.
    private class Changed { public int Listeners; }
    private class ToggleLike { public Changed onValueChanged = new() { Listeners = 1 }; }
    private class SliderLike
    {
        private Changed _changed = new() { Listeners = 1 };
        public Changed onValueChanged { get => _changed; set => _changed = value; }
        public Changed onReadOnly => _changed;
    }
    private class DerivedToggle : ToggleLike { }
    private static class Statics
    {
        public static string[] Languages = ["English", "French"];
        public static string Current => "English";
    }

    [Fact]
    public void A_field_is_found_as_a_field_and_a_property_as_a_property()
    {
        Assert.IsAssignableFrom<FieldInfo>(Members.Find(typeof(ToggleLike), "onValueChanged"));
        Assert.IsAssignableFrom<PropertyInfo>(Members.Find(typeof(SliderLike), "onValueChanged"));
        Assert.IsAssignableFrom<FieldInfo>(Members.Find(typeof(DerivedToggle), "onValueChanged"));
        Assert.Null(Members.Find(typeof(ToggleLike), "onClick"));
        Assert.Equal(typeof(Changed), Members.TypeOf(Members.Find(typeof(SliderLike), "onValueChanged")!));
    }

    [Fact]
    public void Get_and_set_work_on_either_kind()
    {
        var toggle = new ToggleLike();
        var slider = new SliderLike();
        Assert.Equal(1, ((Changed)Members.Get(toggle, "onValueChanged")!).Listeners);
        Assert.True(Members.Set(toggle, "onValueChanged", new Changed()));
        Assert.True(Members.Set(slider, "onValueChanged", new Changed()));
        Assert.Equal(0, toggle.onValueChanged.Listeners);
        Assert.Equal(0, slider.onValueChanged.Listeners);
        Assert.False(Members.Set(slider, "onReadOnly", new Changed()));
        Assert.False(Members.Set(slider, "missing", new Changed()));
        Assert.Null(Members.Get(slider, "missing"));
    }

    [Fact]
    public void Replace_puts_a_fresh_empty_instance_of_the_members_type()
    {
        var toggle = new ToggleLike();
        var old = toggle.onValueChanged;
        var fresh = Members.Replace(toggle, "onValueChanged");
        Assert.Same(toggle.onValueChanged, fresh);
        Assert.NotSame(old, fresh);
        Assert.Equal(0, toggle.onValueChanged.Listeners);
        Assert.Null(Members.Replace(new SliderLike(), "onReadOnly"));
    }

    [Fact]
    public void Static_members_read_either_kind()
    {
        Assert.Equal(new[] { "English", "French" }, Members.GetStatic(typeof(Statics), "Languages"));
        Assert.Equal("English", Members.GetStatic(typeof(Statics), "Current"));
        Assert.Null(Members.GetStatic(typeof(Statics), "Missing"));
    }
}
