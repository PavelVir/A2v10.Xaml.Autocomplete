// Copyright © 2026 Virich Pavlo. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace A2v10XamlAutocomplete;

internal sealed class XamlSchema
{
    private static readonly Lazy<XamlSchema> _instance =
        new Lazy<XamlSchema>(() => Load());

    public static XamlSchema Instance => _instance.Value;

    // Элементы по имени тега
    public ImmutableDictionary<string, XamlElementInfo> Elements { get; private set; }
        = ImmutableDictionary<string, XamlElementInfo>.Empty;

    // Enum-ы по полному CLR-имени типа
    public ImmutableDictionary<string, ImmutableArray<string>> Enums { get; private set; }
        = ImmutableDictionary<string, ImmutableArray<string>>.Empty;

    // Допустимые дочерние элементы:
    // ключ = полное CLR-имя типа, значение = список имён тегов
    public ImmutableDictionary<string, ImmutableArray<string>> AllowedChildElements
    { get; private set; }
        = ImmutableDictionary<string, ImmutableArray<string>>.Empty;

    // Attached properties: ключ = "Owner.PropertyName" (например "Grid.Row")
    public ImmutableDictionary<string, AttachedPropertyInfo> AttachedProperties
    { get; private set; }
        = ImmutableDictionary<string, AttachedPropertyInfo>.Empty;

    // Все имена тегов (для быстрого доступа)
    public ImmutableArray<string> AllTagNames { get; private set; }
        = ImmutableArray<string>.Empty;

    // Версия схемы
    public int SchemaVersion { get; private set; }
    public string PlatformVersion { get; private set; }

    private static XamlSchema Load()
    {
        try
        {
            var rawSchema = XamlSchemaLoader.LoadFromResource();
            return BuildFromRaw(rawSchema);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[A2v10.Xaml.Autocomplete] Failed to load schema: {ex}");
            return CreateFallback();
        }
    }

    private static XamlSchema BuildFromRaw(RawSchema raw)
    {
        var schema = new XamlSchema
        {
            SchemaVersion = raw.SchemaVersion,
            PlatformVersion = raw.PlatformVersion ?? ""
        };

        schema.Elements = BuildElements(raw);
        schema.AllTagNames = schema.Elements.Keys
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToImmutableArray();
        schema.Enums = BuildEnums(raw);
        schema.AllowedChildElements = BuildAllowedChildren(raw);
        schema.AttachedProperties = BuildAttachedProperties(raw);

        return schema;
    }

    private static ImmutableDictionary<string, XamlElementInfo> BuildElements(
        RawSchema raw)
    {
        var builder =
            ImmutableDictionary.CreateBuilder<string, XamlElementInfo>();

        if (raw.Elements == null)
            return builder.ToImmutable();

        foreach (var kvp in raw.Elements)
        {
            var rawElem = kvp.Value;
            var props = BuildProperties(rawElem.Properties);

            builder.Add(kvp.Key, new XamlElementInfo
            {
                Name = kvp.Key,
                ElementClrType = rawElem.ElementClrType ?? "",
                ContentProperty = rawElem.ContentProperty,
                ContentAsXamlAttr = rawElem.ContentAsXamlAttr,
                Description = rawElem.Description,
                Properties = props
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<string, XamlPropertyInfo> BuildProperties(
        Dictionary<string, RawPropertyInfo> rawProps)
    {
        var builder =
            ImmutableDictionary.CreateBuilder<string, XamlPropertyInfo>();

        if (rawProps == null)
            return builder.ToImmutable();

        foreach (var kvp in rawProps)
        {
            var rp = kvp.Value;
            builder.Add(kvp.Key, new XamlPropertyInfo
            {
                DeclaredType = rp.DeclaredType ?? "",
                IsEnum = rp.IsEnum,
                IsElement = rp.IsElement,
                IsCollection = rp.IsCollection,
                CollectionItemType = rp.CollectionItemType,
                CanBeAttribute = rp.CanBeAttribute,
                Description = rp.Description
            });
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<string, ImmutableArray<string>> BuildEnums(
        RawSchema raw)
    {
        var builder =
            ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>();

        if (raw.Enums == null)
            return builder.ToImmutable();

        foreach (var kvp in raw.Enums)
            builder.Add(kvp.Key, kvp.Value.ToImmutableArray());

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<string, ImmutableArray<string>>
        BuildAllowedChildren(RawSchema raw)
    {
        var builder =
            ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>();

        if (raw.AllowedChildElements == null)
            return builder.ToImmutable();

        foreach (var kvp in raw.AllowedChildElements)
            builder.Add(kvp.Key, kvp.Value.ToImmutableArray());

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<string, AttachedPropertyInfo>
        BuildAttachedProperties(RawSchema raw)
    {
        var builder =
            ImmutableDictionary.CreateBuilder<string, AttachedPropertyInfo>();

        if (raw.AttachedProperties == null)
            return builder.ToImmutable();

        foreach (var kvp in raw.AttachedProperties)
        {
            var rp = kvp.Value;
            builder.Add(kvp.Key, new AttachedPropertyInfo
            {
                Type = rp.Type ?? "",
                IsEnum = rp.IsEnum,
                CanBeAttribute = rp.CanBeAttribute,
                ApplicableTags = rp.ApplicableTags != null
                    ? rp.ApplicableTags.ToImmutableArray()
                    : ImmutableArray<string>.Empty
            });
        }

        return builder.ToImmutable();
    }

    private static XamlSchema CreateFallback()
    {
        var schema = new XamlSchema
        {
            SchemaVersion = 0,
            PlatformVersion = "fallback"
        };

        var elemBuilder =
            ImmutableDictionary.CreateBuilder<string, XamlElementInfo>();
        var fallbackTags = new[]
        {
            "Page", "Dialog", "Alert", "Button",
            "TextBox", "Grid", "StackPanel"
        };

        foreach (var name in fallbackTags)
        {
            elemBuilder.Add(name, new XamlElementInfo
            {
                Name = name,
                ElementClrType = $"A2v10.Xaml.{name}",
                Properties = ImmutableDictionary<string, XamlPropertyInfo>.Empty
            });
        }

        schema.Elements = elemBuilder.ToImmutable();
        schema.AllTagNames = schema.Elements.Keys
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToImmutableArray();

        return schema;
    }

    #region Helpers

    public ImmutableArray<string> GetAllowedChildTags(string parentTagName)
    {
        if (!Elements.TryGetValue(parentTagName, out var parentElem))
            return AllTagNames;

        var contentProp = parentElem.ContentProperty;
        if (contentProp == null)
            return AllTagNames;

        if (!parentElem.Properties.TryGetValue(contentProp, out var propInfo))
            return AllTagNames;

        if (!propInfo.IsCollection)
        {
            if (AllowedChildElements.TryGetValue(
                    propInfo.DeclaredType, out var allowed))
                return allowed;
            return AllTagNames;
        }

        var itemType = propInfo.CollectionItemType;
        if (itemType != null
            && AllowedChildElements.TryGetValue(itemType, out var allowedItems))
            return allowedItems;

        return AllTagNames;
    }

    public ImmutableArray<string> GetAllowedChildTagsForProperty(
        string tagName, string propertyName)
    {
        if (!Elements.TryGetValue(tagName, out var elem))
            return AllTagNames;

        if (!elem.Properties.TryGetValue(propertyName, out var propInfo))
            return AllTagNames;

        if (propInfo.IsCollection && propInfo.CollectionItemType != null)
        {
            if (AllowedChildElements.TryGetValue(
                    propInfo.CollectionItemType, out var allowed))
                return allowed;
        }
        else if (propInfo.IsElement)
        {
            if (AllowedChildElements.TryGetValue(
                    propInfo.DeclaredType, out var allowed))
                return allowed;
        }

        return AllTagNames;
    }

    public ImmutableArray<string> GetEnumValues(string enumClrType)
    {
        if (Enums.TryGetValue(enumClrType, out var values))
            return values;
        return ImmutableArray<string>.Empty;
    }

    public ImmutableArray<string> GetAttributeNames(
        string tagName, ISet<string> existingAttrs)
    {
        if (!Elements.TryGetValue(tagName, out var elem))
            return ImmutableArray<string>.Empty;

        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var kvp in elem.Properties)
        {
            // In A2v10 XAML any non-collection property can be an attribute
            // (via bind expressions like Command="{BindCmd Execute, ...}").
            // Only exclude collections — they require child elements.
            if (kvp.Value.IsCollection)
                continue;
            if (existingAttrs != null && existingAttrs.Contains(kvp.Key))
                continue;
            builder.Add(kvp.Key);
        }

        return builder.ToImmutable();
    }

    public ImmutableArray<string> GetElementPropertyNames(string tagName)
    {
        if (!Elements.TryGetValue(tagName, out var elem))
            return ImmutableArray<string>.Empty;

        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var kvp in elem.Properties)
        {
            if (!kvp.Value.IsElement && !kvp.Value.IsCollection)
                continue;
            if (kvp.Key == elem.ContentProperty)
                continue;
            builder.Add(kvp.Key);
        }

        return builder.ToImmutable();
    }

    #endregion
}
