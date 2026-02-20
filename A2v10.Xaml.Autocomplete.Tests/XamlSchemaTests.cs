// Copyright © 2026 Virich Pavlo. All rights reserved.

using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace A2v10XamlAutocomplete.Tests;

public class XamlSchemaTests
{
    private static XamlSchema Schema => XamlSchema.Instance;

    #region Loading

    [Fact]
    public void Schema_LoadsWithoutError()
    {
        var schema = Schema;
        Assert.NotNull(schema);
    }

    [Fact]
    public void Schema_HasPositiveVersion()
    {
        Assert.True(Schema.SchemaVersion > 0,
            $"SchemaVersion should be > 0, got {Schema.SchemaVersion}");
    }

    [Fact]
    public void Schema_HasPlatformVersion()
    {
        Assert.False(string.IsNullOrEmpty(Schema.PlatformVersion));
    }

    #endregion

    #region Elements

    [Fact]
    public void Elements_IsPopulated()
    {
        Assert.True(Schema.Elements.Count > 0,
            "Elements dictionary should not be empty");
    }

    [Fact]
    public void AllTagNames_IsPopulated()
    {
        Assert.True(Schema.AllTagNames.Length > 0,
            "AllTagNames should not be empty");
    }

    [Fact]
    public void AllTagNames_MatchesElementKeys()
    {
        var expected = Schema.Elements.Keys
            .OrderBy(k => k, System.StringComparer.Ordinal);
        Assert.Equal(expected, Schema.AllTagNames);
    }

    [Fact]
    public void KnownElement_Page_Exists()
    {
        Assert.True(Schema.Elements.ContainsKey("Page"));
    }

    [Fact]
    public void KnownElement_Button_Exists()
    {
        Assert.True(Schema.Elements.ContainsKey("Button"));
    }

    [Fact]
    public void Element_HasClrType()
    {
        var page = Schema.Elements["Page"];
        Assert.False(string.IsNullOrEmpty(page.ElementClrType));
    }

    [Fact]
    public void Element_HasProperties()
    {
        var page = Schema.Elements["Page"];
        Assert.True(page.Properties.Count > 0,
            "Page should have properties");
    }

    #endregion

    #region GetAllowedChildTags

    [Fact]
    public void GetAllowedChildTags_ReturnsNonEmpty()
    {
        var tags = Schema.GetAllowedChildTags("Page");
        Assert.True(tags.Length > 0);
    }

    [Fact]
    public void GetAllowedChildTags_UnknownTag_ReturnsAllTags()
    {
        var tags = Schema.GetAllowedChildTags("NonExistentTag");
        Assert.Equal(Schema.AllTagNames.Length, tags.Length);
    }

    #endregion

    #region GetAttributeNames

    [Fact]
    public void GetAttributeNames_ReturnsNonEmpty()
    {
        var attrs = Schema.GetAttributeNames("Page", ImmutableHashSet<string>.Empty);
        Assert.True(attrs.Length > 0);
    }

    [Fact]
    public void GetAttributeNames_ExcludesCollections()
    {
        var attrs = Schema.GetAttributeNames("Page", ImmutableHashSet<string>.Empty);

        foreach (var attrName in attrs)
        {
            Assert.True(
                Schema.Elements["Page"].Properties.TryGetValue(attrName, out var prop),
                $"Property '{attrName}' returned by GetAttributeNames not found in Page.Properties");
            Assert.False(prop.IsCollection,
                $"Attribute '{attrName}' should not be a collection");
        }
    }

    [Fact]
    public void GetAttributeNames_ExcludesExistingAttributes()
    {
        var all = Schema.GetAttributeNames("Page", ImmutableHashSet<string>.Empty);
        Assert.True(all.Length > 0);

        var first = all[0];
        var existing = ImmutableHashSet.Create(first);
        var filtered = Schema.GetAttributeNames("Page", existing);

        Assert.DoesNotContain(first, filtered);
        Assert.Equal(all.Length - 1, filtered.Length);
    }

    [Fact]
    public void GetAttributeNames_UnknownTag_ReturnsEmpty()
    {
        var attrs = Schema.GetAttributeNames(
            "NonExistentTag", ImmutableHashSet<string>.Empty);
        Assert.True(attrs.IsEmpty);
    }

    #endregion

    #region Enums

    [Fact]
    public void Enums_IsPopulated()
    {
        Assert.True(Schema.Enums.Count > 0,
            "Enums should not be empty");
    }

    [Fact]
    public void GetEnumValues_KnownEnum_ReturnsValues()
    {
        var firstEnum = Schema.Enums.Keys.First();
        var values = Schema.GetEnumValues(firstEnum);
        Assert.True(values.Length > 0,
            $"Enum '{firstEnum}' should have values");
    }

    [Fact]
    public void GetEnumValues_UnknownEnum_ReturnsEmpty()
    {
        var values = Schema.GetEnumValues("Non.Existent.Enum");
        Assert.True(values.IsEmpty);
    }

    #endregion

    #region AttachedProperties

    [Fact]
    public void AttachedProperties_IsPopulated()
    {
        Assert.True(Schema.AttachedProperties.Count > 0,
            "AttachedProperties should not be empty");
    }

    [Fact]
    public void AttachedProperty_GridRow_Exists()
    {
        Assert.True(Schema.AttachedProperties.ContainsKey("Grid.Row"),
            "Grid.Row attached property should exist");
    }

    [Fact]
    public void AttachedProperty_HasType()
    {
        var gridRow = Schema.AttachedProperties["Grid.Row"];
        Assert.False(string.IsNullOrEmpty(gridRow.Type));
    }

    #endregion

    #region GetElementPropertyNames

    [Fact]
    public void GetElementPropertyNames_UnknownTag_ReturnsEmpty()
    {
        var props = Schema.GetElementPropertyNames("NonExistentTag");
        Assert.True(props.IsEmpty);
    }

    [Fact]
    public void GetElementPropertyNames_KnownTag_ReturnsElementAndCollectionProperties()
    {
        var entry = Schema.Elements
            .First(kvp => kvp.Value.Properties
                .Any(p => (p.Value.IsElement || p.Value.IsCollection)
                    && p.Key != kvp.Value.ContentProperty));
        var props = Schema.GetElementPropertyNames(entry.Key);
        Assert.True(props.Length > 0,
            $"'{entry.Key}' should have at least one element/collection property");
    }

    [Fact]
    public void GetElementPropertyNames_DoesNotIncludeContentProperty()
    {
        var entry = Schema.Elements
            .FirstOrDefault(kvp =>
                kvp.Value.ContentProperty != null
                && kvp.Value.Properties.ContainsKey(kvp.Value.ContentProperty));

        if (entry.Key == null)
            return;

        var props = Schema.GetElementPropertyNames(entry.Key);
        Assert.DoesNotContain(entry.Value.ContentProperty, props);
    }

    #endregion
}
