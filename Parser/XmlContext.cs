// Copyright © 2026 Virich Pavlo. All rights reserved.

using System.Collections.Immutable;

namespace A2v10XamlAutocomplete;

internal enum XmlContextType
{
    /// <summary>
    /// No context (outside XML, in comment, in CDATA, in PI).
    /// </summary>
    None,

    /// <summary>
    /// After '&lt;' — suggest tag names.
    /// </summary>
    TagName,

    /// <summary>
    /// After '&lt;/' — suggest closing tag.
    /// </summary>
    ClosingTag,

    /// <summary>
    /// Inside opening tag after whitespace — suggest attributes.
    /// </summary>
    AttributeName,

    /// <summary>
    /// Inside attribute value (after '"') — suggest values.
    /// </summary>
    AttributeValue,

    /// <summary>
    /// After '&lt;Tag.' — suggest element properties (e.g. &lt;Page.Toolbar&gt;).
    /// </summary>
    ElementProperty,

    /// <summary>
    /// Inside element content (between tags) — suggest child tags.
    /// </summary>
    Content
}

internal sealed class XmlContext
{
    public XmlContextType Type { get; set; } = XmlContextType.None;

    /// <summary>
    /// Name of the current (nearest) tag, e.g. "Button" when cursor is inside &lt;Button ...&gt;.
    /// Namespace prefix is stripped.
    /// </summary>
    public string CurrentTag { get; set; }

    /// <summary>
    /// Name of the current attribute when Type == AttributeValue.
    /// </summary>
    public string CurrentAttribute { get; set; }

    /// <summary>
    /// Name of the parent tag — the nearest unclosed tag from the tag stack.
    /// Namespace prefix is stripped.
    /// </summary>
    public string ParentTag { get; set; }

    /// <summary>
    /// Attributes already defined on the current tag (to avoid suggesting duplicates).
    /// </summary>
    public ImmutableHashSet<string> ExistingAttributes { get; set; }
        = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Partially typed text (for filtering completions).
    /// </summary>
    public string PartialInput { get; set; }

    /// <summary>
    /// Start position of partial input (for computing ApplicableToSpan).
    /// </summary>
    public int PartialInputStart { get; set; }

    /// <summary>
    /// Xmlns prefix used in the document for A2v10 namespace,
    /// or null if A2v10 is the default xmlns.
    /// </summary>
    public string A2v10Prefix { get; set; }
}
