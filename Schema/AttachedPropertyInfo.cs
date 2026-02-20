// Copyright © 2026 Virich Pavlo. All rights reserved.

using System.Collections.Immutable;

namespace A2v10XamlAutocomplete;

internal sealed class AttachedPropertyInfo
{
    // Полное CLR-имя типа (например "System.Int32")
    public string Type { get; set; }

    public bool IsEnum { get; set; }
    public bool CanBeAttribute { get; set; }

    // Список допустимых тегов (имена без namespace)
    public ImmutableArray<string> ApplicableTags { get; set; }
        = ImmutableArray<string>.Empty;
}
