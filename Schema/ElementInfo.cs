// Copyright © 2026 Virich Pavlo. All rights reserved.

using System.Collections.Immutable;

namespace A2v10XamlAutocomplete;

internal sealed class XamlElementInfo
{
    // Имя тега (например "Page")
    public string Name { get; set; }

    // Полное CLR-имя (например "A2v10.Xaml.Page")
    public string ElementClrType { get; set; }

    // Имя content property (например "Children") или null
    public string ContentProperty { get; set; }

    // true если элемент помечен [ContentAsXamlAttr]
    public bool ContentAsXamlAttr { get; set; }

    public string Description { get; set; }

    // Свойства элемента: ключ = имя свойства
    public ImmutableDictionary<string, XamlPropertyInfo> Properties { get; set; }
        = ImmutableDictionary<string, XamlPropertyInfo>.Empty;
}
