// Copyright © 2026 Virich Pavlo. All rights reserved.

namespace A2v10XamlAutocomplete;

internal sealed class XamlPropertyInfo
{
    // Полное CLR-имя типа свойства (например "A2v10.Xaml.ButtonStyle")
    public string DeclaredType { get; set; }

    public bool IsEnum { get; set; }
    public bool IsElement { get; set; }
    public bool IsCollection { get; set; }

    // Полное CLR-имя типа элементов коллекции (например "A2v10.Xaml.UIElementBase")
    public string CollectionItemType { get; set; }

    // true если можно использовать как XML-атрибут
    // (string, bool, enum, numeric, Nullable<primitive>)
    public bool CanBeAttribute { get; set; }

    public string Description { get; set; }
}
