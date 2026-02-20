
// Copyright © 2026 Virich Pavlo. All rights reserved.

using System;

using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text.Adornments;

namespace A2v10XamlAutocomplete;

internal record Element
{
    static Guid ImageLibraryGuid = new Guid("ae27a6b0-e345-4288-96df-5eaf394ee369");

    static ImageElement TagIcon = new ImageElement(new ImageId(ImageLibraryGuid, 1873), "Tag");
    static ImageElement PropertyIcon = new ImageElement(new ImageId(ImageLibraryGuid, 2429), "Property");
    static ImageElement EnumIcon = new ImageElement(new ImageId(ImageLibraryGuid, 1120), "Enum");
    static ImageElement XmlCommentIcon = new ImageElement(new ImageId(ImageLibraryGuid, 3568), "XmlComment");
    static ImageElement XmlCDataIcon = new ImageElement(new ImageId(ImageLibraryGuid, 3567), "XmlCData");
    static ImageElement ClosingTagIcon = new ImageElement(new ImageId(ImageLibraryGuid, 1873), "ClosingTag");
    static ImageElement AttachedPropertyIcon = new ImageElement(new ImageId(ImageLibraryGuid, 2429), "AttachedProperty");
    static ImageElement BooleanIcon = new ImageElement(new ImageId(ImageLibraryGuid, 1120), "Boolean");

    public String Name { get; }
    public ElemKind Kind { get; }
    public enum ElemKind
    {
        Tag,
        Property,
        EnumValue,
        CData,
        Comment,
        ClosingTag,
        AttachedProperty,
        Boolean
    }


    internal Element(String name, ElemKind kind)
    {
        Name = name;
        Kind = kind;
    }   

    static ImageElement GetIcon(ElemKind kind)
    {
        return kind switch
        {
            ElemKind.Tag => TagIcon,
            ElemKind.ClosingTag => ClosingTagIcon,
            ElemKind.Property => PropertyIcon,
            ElemKind.AttachedProperty => AttachedPropertyIcon,
            ElemKind.EnumValue => EnumIcon,
            ElemKind.Boolean => BooleanIcon,
            ElemKind.CData => XmlCDataIcon,
            ElemKind.Comment => XmlCommentIcon,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    internal static CompletionItem Create(String name, ElemKind kind, IAsyncCompletionSource source)
    {
        var elem = new Element(name, kind);
        var item = new CompletionItem(name, source, GetIcon(kind));
        item.Properties.AddProperty(nameof(Element), elem);
        return item;
    }
}
