// Copyright © 2026 Virich Pavlo. All rights reserved.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;

namespace A2v10XamlAutocomplete;

internal class XamlCompletionSource : IAsyncCompletionSource
{
    // Cached context from InitializeCompletion to avoid double parsing.
    // Single volatile reference ensures atomic read/write of the entire cache.
    private sealed class CachedParseResult
    {
        public readonly XmlContext Context;
        public readonly int Version;
        public readonly int Position;

        public CachedParseResult(XmlContext context, int version, int position)
        {
            Context = context;
            Version = version;
            Position = position;
        }
    }

    private volatile CachedParseResult _cached;

    public CompletionStartData InitializeCompletion(
        CompletionTrigger trigger, SnapshotPoint triggerLocation,
        CancellationToken token)
    {
        var snapshot = triggerLocation.Snapshot;

        if (!IsA2v10XamlFile(snapshot.TextBuffer))
            return CompletionStartData.DoesNotParticipateInCompletion;

        if (trigger.Reason == CompletionTriggerReason.Deletion)
            return CompletionStartData.DoesNotParticipateInCompletion;

        if (trigger.Reason == CompletionTriggerReason.Insertion)
        {
            char ch = trigger.Character;
            if (ch != ' ' && ch != '"' && ch != '\''
                && ch != '.' && ch != '/' && !char.IsLetter(ch))
                return CompletionStartData.DoesNotParticipateInCompletion;
        }

        string text = snapshot.GetText();
        int position = triggerLocation.Position;
        var context = XmlContextParser.Parse(text, position);

        if (context.Type == XmlContextType.None)
            return CompletionStartData.DoesNotParticipateInCompletion;

        int length = position - context.PartialInputStart;
        if (length < 0)
            return CompletionStartData.DoesNotParticipateInCompletion;

        _cached = new CachedParseResult(
            context, snapshot.Version.VersionNumber, position);

        var span = new SnapshotSpan(
            snapshot, context.PartialInputStart, length);

        return new CompletionStartData(
            CompletionParticipation.ProvidesItems, span);
    }

    public Task<CompletionContext> GetCompletionContextAsync(
        IAsyncCompletionSession session, CompletionTrigger trigger,
        SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var snapshot = triggerLocation.Snapshot;
        int position = triggerLocation.Position;

        XmlContext context;
        var cached = _cached;
        if (cached != null
            && cached.Version == snapshot.Version.VersionNumber
            && cached.Position == position)
        {
            context = cached.Context;
        }
        else
        {
            string text = snapshot.GetText();
            context = XmlContextParser.Parse(text, position);
        }
        _cached = null;

        var schema = XamlSchema.Instance;
        string prefix = context.A2v10Prefix;

        token.ThrowIfCancellationRequested();

        ImmutableArray<CompletionItem> items;
        switch (context.Type)
        {
            case XmlContextType.TagName:
            case XmlContextType.Content:
                items = BuildTagNameItems(schema, context, prefix);
                break;
            case XmlContextType.ClosingTag:
                items = BuildClosingTagItems(context, prefix);
                break;
            case XmlContextType.ElementProperty:
                items = BuildElementPropertyItems(schema, context);
                break;
            case XmlContextType.AttributeName:
                items = BuildAttributeNameItems(schema, context);
                break;
            case XmlContextType.AttributeValue:
                items = BuildAttributeValueItems(schema, context);
                break;
            default:
                items = ImmutableArray<CompletionItem>.Empty;
                break;
        }

        return Task.FromResult(new CompletionContext(items));
    }

    public Task<object> GetDescriptionAsync(
        IAsyncCompletionSession session, CompletionItem item,
        CancellationToken token)
    {
        if (!item.Properties.TryGetProperty<Element>(
                nameof(Element), out var elem))
            return Task.FromResult<object>(string.Empty);

        var schema = XamlSchema.Instance;
        string description;

        switch (elem.Kind)
        {
            case Element.ElemKind.Tag:
                string tagName = StripPrefix(elem.Name);
                if (schema.Elements.TryGetValue(tagName, out var info))
                    description = FormatElementDescription(info);
                else
                    description = elem.Name;
                break;
            case Element.ElemKind.ClosingTag:
                description = $"Closing tag: </{elem.Name}>";
                break;
            case Element.ElemKind.Property:
            case Element.ElemKind.AttachedProperty:
                description = TryGetPropertyDescription(
                    schema, elem.Name, out var propDesc)
                    ? propDesc
                    : elem.Name;
                break;
            default:
                description = elem.Name;
                break;
        }

        return Task.FromResult<object>(description);
    }

    #region Item builders

    private ImmutableArray<CompletionItem> BuildTagNameItems(
        XamlSchema schema, XmlContext context, string prefix)
    {
        var tags = context.ParentTag != null
            ? schema.GetAllowedChildTags(context.ParentTag)
            : schema.AllTagNames;

        var builder = ImmutableArray.CreateBuilder<CompletionItem>(
            tags.Length + 2);

        foreach (string name in tags)
            builder.Add(Element.Create(
                AddPrefix(name, prefix), Element.ElemKind.Tag, this));

        builder.Add(Element.Create(
            "!--", Element.ElemKind.Comment, this));
        builder.Add(Element.Create(
            "![CDATA[", Element.ElemKind.CData, this));

        return builder.ToImmutable();
    }

    private ImmutableArray<CompletionItem> BuildClosingTagItems(
        XmlContext context, string prefix)
    {
        if (context.ParentTag == null)
            return ImmutableArray<CompletionItem>.Empty;

        return ImmutableArray.Create(
            Element.Create(
                AddPrefix(context.ParentTag, prefix),
                Element.ElemKind.ClosingTag, this));
    }

    private ImmutableArray<CompletionItem> BuildElementPropertyItems(
        XamlSchema schema, XmlContext context)
    {
        if (context.CurrentTag == null)
            return ImmutableArray<CompletionItem>.Empty;

        var names = schema.GetElementPropertyNames(context.CurrentTag);
        var builder = ImmutableArray.CreateBuilder<CompletionItem>(
            names.Length);

        foreach (string name in names)
            builder.Add(Element.Create(
                $"{context.CurrentTag}.{name}",
                Element.ElemKind.Property, this));

        return builder.ToImmutable();
    }

    private ImmutableArray<CompletionItem> BuildAttributeNameItems(
        XamlSchema schema, XmlContext context)
    {
        if (context.CurrentTag == null)
            return ImmutableArray<CompletionItem>.Empty;

        var names = schema.GetAttributeNames(
            context.CurrentTag, context.ExistingAttributes);

        var builder = ImmutableArray.CreateBuilder<CompletionItem>();

        foreach (string name in names)
            builder.Add(Element.Create(
                name, Element.ElemKind.Property, this));

        foreach (var kvp in schema.AttachedProperties)
        {
            if (context.ExistingAttributes.Contains(kvp.Key))
                continue;
            if (!kvp.Value.ApplicableTags.IsDefaultOrEmpty
                && kvp.Value.ApplicableTags.Contains(context.CurrentTag))
            {
                builder.Add(Element.Create(
                    kvp.Key, Element.ElemKind.AttachedProperty, this));
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<CompletionItem> BuildAttributeValueItems(
        XamlSchema schema, XmlContext context)
    {
        var builder = ImmutableArray.CreateBuilder<CompletionItem>();

        XamlPropertyInfo prop = null;

        if (context.CurrentTag != null
            && context.CurrentAttribute != null
            && schema.Elements.TryGetValue(
                context.CurrentTag, out var elemInfo))
        {
            elemInfo.Properties.TryGetValue(
                context.CurrentAttribute, out prop);
        }

        // Check attached properties if normal property not found.
        AttachedPropertyInfo attachedProp = null;
        if (prop == null && context.CurrentAttribute != null)
        {
            schema.AttachedProperties.TryGetValue(
                context.CurrentAttribute, out attachedProp);
        }

        if (prop != null)
        {
            if (prop.IsEnum)
            {
                var values = schema.GetEnumValues(prop.DeclaredType);
                foreach (string val in values)
                    builder.Add(Element.Create(
                        val, Element.ElemKind.EnumValue, this));
            }
            else if (string.Equals(prop.DeclaredType, "System.Boolean",
                         StringComparison.Ordinal))
            {
                builder.Add(Element.Create(
                    "True", Element.ElemKind.Boolean, this));
                builder.Add(Element.Create(
                    "False", Element.ElemKind.Boolean, this));
            }
        }
        else if (attachedProp != null)
        {
            if (attachedProp.IsEnum)
            {
                var values = schema.GetEnumValues(attachedProp.Type);
                foreach (string val in values)
                    builder.Add(Element.Create(
                        val, Element.ElemKind.EnumValue, this));
            }
        }

        builder.Add(Element.Create(
            "{Bind }", Element.ElemKind.EnumValue, this));
        builder.Add(Element.Create(
            "{BindCmd }", Element.ElemKind.EnumValue, this));
        builder.Add(Element.Create(
            "{StaticResource }", Element.ElemKind.EnumValue, this));

        return builder.ToImmutable();
    }

    #endregion

    #region File detection

    private static bool IsA2v10XamlFile(ITextBuffer buffer)
    {
        if (!buffer.Properties.TryGetProperty(
                typeof(ITextDocument), out ITextDocument doc))
            return false;

        string filePath = doc.FilePath;
        if (!filePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            return false;

        var snapshot = buffer.CurrentSnapshot;
        int checkLen = Math.Min(snapshot.Length, 2000);
        string header = snapshot.GetText(0, checkLen);

        if (header.Contains(
                "schemas.microsoft.com/winfx/2006/xaml/presentation")
            || header.Contains(
                "schemas.microsoft.com/dotnet/2021/maui"))
            return false;

        return header.Contains("clr-namespace:A2v10.Xaml")
            || header.Contains("<Page")
            || header.Contains("<Dialog")
            || header.Contains("<Alert");
    }

    #endregion

    #region Helpers

    private static string AddPrefix(string name, string prefix)
    {
        return prefix != null ? $"{prefix}:{name}" : name;
    }

    private static string StripPrefix(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        int colon = name.IndexOf(':');
        return colon >= 0 && colon < name.Length - 1
            ? name.Substring(colon + 1)
            : name;
    }

    private static string FormatElementDescription(XamlElementInfo info)
    {
        string desc = info.Name;
        if (!string.IsNullOrEmpty(info.ElementClrType))
            desc += $" ({info.ElementClrType})";
        if (!string.IsNullOrEmpty(info.Description))
            desc += $" \u2014 {info.Description}";
        return desc;
    }

    private static bool TryGetPropertyDescription(
        XamlSchema schema, string qualifiedName, out string description)
    {
        description = null;

        int dotPos = qualifiedName.IndexOf('.');
        if (dotPos < 0)
        {
            description = $"Property: {qualifiedName}";
            return true;
        }

        string tagName = qualifiedName.Substring(0, dotPos);
        string propName = qualifiedName.Substring(dotPos + 1);

        if (schema.Elements.TryGetValue(tagName, out var elem)
            && elem.Properties.TryGetValue(propName, out var prop))
        {
            string shortType = GetShortTypeName(prop.DeclaredType);
            description = $"{propName} : {shortType}";
            if (!string.IsNullOrEmpty(prop.Description))
                description += $" \u2014 {prop.Description}";
            return true;
        }

        if (schema.AttachedProperties.TryGetValue(
                qualifiedName, out var attached))
        {
            string shortType = GetShortTypeName(attached.Type);
            description = $"{qualifiedName} : {shortType}";
            return true;
        }

        description = qualifiedName;
        return true;
    }

    private static string GetShortTypeName(string fullTypeName)
    {
        if (string.IsNullOrEmpty(fullTypeName))
            return fullTypeName;
        int lastDot = fullTypeName.LastIndexOf('.');
        return lastDot >= 0 && lastDot < fullTypeName.Length - 1
            ? fullTypeName.Substring(lastDot + 1)
            : fullTypeName;
    }

    #endregion
}
