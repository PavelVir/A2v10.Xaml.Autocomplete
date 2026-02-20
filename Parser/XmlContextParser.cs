// Copyright © 2026 Virich Pavlo. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace A2v10XamlAutocomplete;

internal static class XmlContextParser
{
    private const int MaxScanBack = 10000;
    private const int XmlnsScanLimit = 2000;

    private const string A2v10NamespaceMarker = "clr-namespace:A2v10.Xaml";

    public static XmlContext Parse(string text, int position)
    {
        if (string.IsNullOrEmpty(text) || position < 0 || position > text.Length)
            return new XmlContext { Type = XmlContextType.None };

        if (position == 0)
            return new XmlContext { Type = XmlContextType.Content };

        int scanStart = Math.Max(0, position - MaxScanBack);

        var result = DetermineContext(text, scanStart, position);

        int xmlnsLimit = Math.Min(position, XmlnsScanLimit);
        result.A2v10Prefix = DetectA2v10Prefix(text, xmlnsLimit);

        return result;
    }

    #region Context determination

    private static XmlContext DetermineContext(
        string text, int scanStart, int position)
    {
        // Step 1: Check if we are inside a comment, CDATA, or PI.
        // Scan from position 0 (not scanStart) to correctly detect
        // constructs that opened before the scan window.
        var specialResult = CheckSpecialContexts(text, position);
        if (specialResult != null)
            return specialResult;

        // Step 2: Find the nearest unclosed '<' by scanning backward.
        //   We skip over complete tags (those that have matching '<' and '>'),
        //   and skip quoted regions within tags.
        //   If we find '>' first (not inside quotes), we are in content.
        //   If we find '<' first (outside quotes), we are inside a tag.

        int tagOpenPos = FindNearestUnclosedTagOpen(text, scanStart, position);

        if (tagOpenPos < 0)
            return BuildContentContext(text, scanStart, position);

        return BuildTagContext(text, scanStart, tagOpenPos, position);
    }

    /// <summary>
    /// Scans backward from position to find the nearest '&lt;' or '&gt;'.
    /// If '&lt;' is found first, cursor is inside an unclosed tag (returns position of '&lt;').
    /// If '&gt;' is found first, cursor is in content between tags (returns -1).
    ///
    /// Attribute value quotes are NOT skipped because they never contain
    /// literal '&lt;' or '&gt;' in well-formed XML/XAML. This avoids the
    /// ambiguity of pairing quotes during backward scanning.
    /// </summary>
    private static int FindNearestUnclosedTagOpen(
        string text, int scanStart, int position)
    {
        for (int i = position - 1; i >= scanStart; i--)
        {
            char ch = text[i];
            if (ch == '<')
                return i;
            if (ch == '>')
                return -1;
        }

        // Exhausted scan window — assume content.
        return -1;
    }

    #endregion

    #region Special contexts (comment, CDATA, PI)

    private static XmlContext CheckSpecialContexts(
        string text, int position)
    {
        // Strategy: find the nearest opening marker of special constructs
        // scanning backward from position to the start of the document,
        // and check if it's still open at cursor position.
        // We check in order of priority (comment > CDATA > PI).

        int searchFrom = position - 1;
        const int scanLimit = 0;

        // Check comment: look for "<!--" backward, verify no "-->" before position.
        int commentOpen = FindBackward(text, "<!--", searchFrom, scanLimit);
        if (commentOpen >= 0)
        {
            int commentClose = FindForward(text, "-->", commentOpen + 4);
            if (commentClose < 0 || commentClose >= position)
                return new XmlContext { Type = XmlContextType.None };
            // Comment is closed before position — clear so it doesn't
            // affect CDATA/PI proximity checks below.
            commentOpen = -1;
        }

        // Check CDATA: look for "<![CDATA[" backward.
        int cdataOpen = FindBackward(text, "<![CDATA[", searchFrom, scanLimit);
        if (cdataOpen >= 0 && (commentOpen < 0 || cdataOpen > commentOpen))
        {
            int cdataClose = FindForward(text, "]]>", cdataOpen + 9);
            if (cdataClose < 0 || cdataClose >= position)
                return new XmlContext { Type = XmlContextType.None };
        }

        // Check PI: look for "<?" backward (but not "<!--").
        int piOpen = FindBackward(text, "<?", searchFrom, scanLimit);
        if (piOpen >= 0
            && (commentOpen < 0 || piOpen > commentOpen)
            && (cdataOpen < 0 || piOpen > cdataOpen))
        {
            // Make sure this is not part of "<!--".
            if (piOpen + 1 < text.Length && text[piOpen + 1] == '?')
            {
                int piClose = FindForward(text, "?>", piOpen + 2);
                if (piClose < 0 || piClose >= position)
                    return new XmlContext { Type = XmlContextType.None };
            }
        }

        return null;
    }

    #endregion

    #region Build context: inside a tag (after '<')

    private static XmlContext BuildTagContext(
        string text, int scanStart, int tagOpenPos, int position)
    {
        int afterLt = tagOpenPos + 1;

        if (afterLt >= position)
        {
            // Cursor right after '<' with nothing typed yet.
            return new XmlContext
            {
                Type = XmlContextType.TagName,
                PartialInput = string.Empty,
                PartialInputStart = afterLt,
                ParentTag = FindParentTag(text, scanStart, tagOpenPos)
            };
        }

        char firstChar = text[afterLt];

        // Closing tag: </
        if (firstChar == '/')
        {
            string partial = ExtractIdentifier(text, afterLt + 1, position);
            return new XmlContext
            {
                Type = XmlContextType.ClosingTag,
                PartialInput = StripPrefix(partial),
                PartialInputStart = afterLt + 1,
                ParentTag = FindParentTag(text, scanStart, tagOpenPos)
            };
        }

        // Comment/CDATA/PI starts that user is typing
        if (firstChar == '!' || firstChar == '?')
        {
            string partial = text.Substring(afterLt, position - afterLt);
            return new XmlContext
            {
                Type = XmlContextType.TagName,
                PartialInput = partial,
                PartialInputStart = afterLt,
                ParentTag = FindParentTag(text, scanStart, tagOpenPos)
            };
        }

        // Normal opening tag: <TagName ...
        int tagNameEnd = afterLt;
        while (tagNameEnd < position && !IsTagNameTerminator(text[tagNameEnd]))
            tagNameEnd++;

        string tagNameRaw = text.Substring(afterLt, tagNameEnd - afterLt);

        // Element property: <Tag.Property or <ns:Tag.Property
        int dotPos = tagNameRaw.IndexOf('.');
        if (dotPos >= 0 && tagNameEnd >= position)
        {
            string beforeDot = tagNameRaw.Substring(0, dotPos);
            string afterDot = tagNameRaw.Substring(dotPos + 1);

            return new XmlContext
            {
                Type = XmlContextType.ElementProperty,
                CurrentTag = StripPrefix(beforeDot),
                PartialInput = afterDot,
                PartialInputStart = afterLt + dotPos + 1,
                ParentTag = FindParentTag(text, scanStart, tagOpenPos)
            };
        }

        string tagName = StripPrefix(tagNameRaw);

        // Still typing the tag name (no terminator reached yet).
        if (tagNameEnd >= position)
        {
            return new XmlContext
            {
                Type = XmlContextType.TagName,
                PartialInput = tagName,
                PartialInputStart = afterLt,
                ParentTag = FindParentTag(text, scanStart, tagOpenPos)
            };
        }

        // Cursor is past the tag name — in the attributes area.
        return BuildAttributeContext(
            text, scanStart, tagOpenPos, tagName, tagNameEnd, position);
    }

    private static XmlContext BuildAttributeContext(
        string text, int scanStart, int tagOpenPos,
        string tagName, int attrsStart, int position)
    {
        // Forward scan from attrsStart to position to determine local context.
        int i = attrsStart;
        string currentAttrName = null;
        bool inQuote = false;
        char quoteChar = '\0';
        int quoteStart = -1;
        bool afterEquals = false;
        var existingAttrs = new HashSet<string>(StringComparer.Ordinal);

        while (i < position)
        {
            char ch = text[i];

            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                    if (currentAttrName != null)
                        existingAttrs.Add(StripPrefix(currentAttrName));
                    currentAttrName = null;
                    afterEquals = false;
                }
                i++;
                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quoteChar = ch;
                quoteStart = i;
                i++;
                continue;
            }

            if (ch == '=')
            {
                afterEquals = true;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!afterEquals && currentAttrName != null)
                {
                    // Attribute name without value (standalone attribute like "Required").
                    // This is valid in some XML dialects but not typical in XAML.
                    // Don't add to existing attrs since it has no '='.
                    currentAttrName = null;
                }
                i++;
                continue;
            }

            if (IsNameStartChar(ch))
            {
                int nameStart = i;
                while (i < position && IsNameChar(text[i]))
                    i++;

                string name = text.Substring(nameStart, i - nameStart);

                if (!afterEquals)
                    currentAttrName = name;
                else
                    afterEquals = false;

                continue;
            }

            // Skip '/' or other chars.
            i++;
        }

        // Determine the context at cursor position.
        if (inQuote)
        {
            // Cursor is inside a quoted attribute value.
            string partial = text.Substring(quoteStart + 1, position - quoteStart - 1);
            return new XmlContext
            {
                Type = XmlContextType.AttributeValue,
                CurrentTag = tagName,
                CurrentAttribute = StripPrefix(currentAttrName ?? string.Empty),
                PartialInput = partial,
                PartialInputStart = quoteStart + 1,
                ExistingAttributes = existingAttrs.ToImmutableHashSet(StringComparer.Ordinal),
                ParentTag = FindParentTag(text, scanStart, tagOpenPos)
            };
        }

        // Not in a quote — typing an attribute name or in whitespace.
        string attrPartial = string.Empty;
        int attrPartialStart = position;
        if (position > attrsStart)
        {
            int back = position - 1;
            while (back >= attrsStart && IsNameChar(text[back]))
                back--;
            back++;
            if (back < position)
            {
                attrPartial = text.Substring(back, position - back);
                attrPartialStart = back;
            }
        }

        return new XmlContext
        {
            Type = XmlContextType.AttributeName,
            CurrentTag = tagName,
            PartialInput = StripPrefix(attrPartial),
            PartialInputStart = attrPartialStart,
            ExistingAttributes = existingAttrs.ToImmutableHashSet(StringComparer.Ordinal),
            ParentTag = FindParentTag(text, scanStart, tagOpenPos)
        };
    }

    #endregion

    #region Build context: content (between tags)

    private static XmlContext BuildContentContext(
        string text, int scanStart, int position)
    {
        // Scan backward to find partial text the user is typing
        // between tags. Only letters/digits/underscore — not '.', ':'
        // which could cross attribute boundaries.
        int partialStart = position;
        while (partialStart > scanStart
            && partialStart > 0)
        {
            char ch = text[partialStart - 1];
            if (char.IsLetterOrDigit(ch) || ch == '_')
                partialStart--;
            else
                break;
        }

        string partial = partialStart < position
            ? text.Substring(partialStart, position - partialStart)
            : string.Empty;

        return new XmlContext
        {
            Type = XmlContextType.Content,
            PartialInput = partial,
            PartialInputStart = partialStart,
            ParentTag = FindParentTag(text, scanStart, position)
        };
    }

    #endregion

    #region Tag stack / ParentTag

    /// <summary>
    /// Scans backward from position building a tag stack to find the
    /// nearest unclosed parent tag.
    /// Closing tags push onto the stack; opening tags either pop
    /// a matching close or are returned as the parent.
    /// Self-closing tags are skipped.
    /// </summary>
    private static string FindParentTag(string text, int scanStart, int position)
    {
        var stack = new Stack<string>();
        int i = position - 1;

        while (i >= scanStart)
        {
            // Find the nearest '>' scanning backward.
            while (i >= scanStart && text[i] != '>')
                i--;

            if (i < scanStart)
                break;

            int closeAngle = i;

            // Skip comment end "-->"
            if (closeAngle >= 2
                && text[closeAngle - 1] == '-'
                && text[closeAngle - 2] == '-')
            {
                int cStart = FindBackward(text, "<!--", closeAngle - 3, scanStart);
                i = cStart >= 0 ? cStart - 1 : scanStart - 1;
                continue;
            }

            // Skip CDATA end "]]>"
            if (closeAngle >= 2
                && text[closeAngle - 1] == ']'
                && text[closeAngle - 2] == ']')
            {
                int cStart = FindBackward(
                    text, "<![CDATA[", closeAngle - 3, scanStart);
                i = cStart >= 0 ? cStart - 1 : scanStart - 1;
                continue;
            }

            // Skip PI end "?>"
            if (closeAngle >= 1 && text[closeAngle - 1] == '?')
            {
                int cStart = FindBackward(text, "<?", closeAngle - 2, scanStart);
                i = cStart >= 0 ? cStart - 1 : scanStart - 1;
                continue;
            }

            // Find corresponding '<'.
            int openAngle = FindTagOpenBackward(text, scanStart, closeAngle - 1);
            if (openAngle < 0)
                break;

            bool selfClosing = text[closeAngle - 1] == '/';
            int afterLt = openAngle + 1;

            if (afterLt > closeAngle)
            {
                i = openAngle - 1;
                continue;
            }

            bool isClosingTag = afterLt < text.Length && text[afterLt] == '/';

            if (isClosingTag)
            {
                string name = ExtractTagNameAfterOpen(text, openAngle, closeAngle);
                if (!string.IsNullOrEmpty(name))
                    stack.Push(StripPrefix(name));
            }
            else if (!selfClosing)
            {
                string name = ExtractTagNameAfterOpen(text, openAngle, closeAngle);
                string stripped = StripPrefix(name);
                if (!string.IsNullOrEmpty(stripped))
                {
                    if (stack.Count > 0 && stack.Peek() == stripped)
                        stack.Pop();
                    else
                        return stripped;
                }
            }
            // Self-closing tags (<Tag .../>) are silently skipped.

            i = openAngle - 1;
        }

        return null;
    }

    #endregion

    #region Xmlns detection

    private static string DetectA2v10Prefix(string text, int limit)
    {
        // Scan the first `limit` characters for xmlns declarations.
        // xmlns="clr-namespace:A2v10.Xaml..."       → default ns → return null
        // xmlns:prefix="clr-namespace:A2v10.Xaml..." → return prefix
        int end = Math.Min(text.Length, limit);
        int searchPos = 0;

        while (searchPos < end)
        {
            int xmlnsPos = text.IndexOf("xmlns", searchPos, StringComparison.Ordinal);
            if (xmlnsPos < 0 || xmlnsPos >= end)
                break;

            int afterXmlns = xmlnsPos + 5;
            if (afterXmlns >= end)
                break;

            string prefix = null;
            if (text[afterXmlns] == ':')
            {
                int prefixStart = afterXmlns + 1;
                int prefixEnd = prefixStart;
                while (prefixEnd < end && IsNameChar(text[prefixEnd]))
                    prefixEnd++;
                if (prefixEnd > prefixStart)
                    prefix = text.Substring(prefixStart, prefixEnd - prefixStart);
                afterXmlns = prefixEnd;
            }

            // Skip optional whitespace around '='
            while (afterXmlns < end && char.IsWhiteSpace(text[afterXmlns]))
                afterXmlns++;

            if (afterXmlns >= end || text[afterXmlns] != '=')
            {
                searchPos = afterXmlns + 1;
                continue;
            }
            afterXmlns++;

            while (afterXmlns < end && char.IsWhiteSpace(text[afterXmlns]))
                afterXmlns++;

            if (afterXmlns >= end)
                break;

            char q = text[afterXmlns];
            if (q != '"' && q != '\'')
            {
                searchPos = afterXmlns + 1;
                continue;
            }

            int valStart = afterXmlns + 1;
            int valEnd = text.IndexOf(q, valStart);
            if (valEnd < 0 || valEnd >= end)
            {
                searchPos = valStart;
                continue;
            }

            string value = text.Substring(valStart, valEnd - valStart);
            if (value.IndexOf(A2v10NamespaceMarker, StringComparison.Ordinal) >= 0)
                return prefix;

            searchPos = valEnd + 1;
        }

        return null;
    }

    #endregion

    #region Helpers: forward / backward search

    private static int FindBackward(
        string text, string pattern, int from, int limit)
    {
        if (from < limit || from >= text.Length)
            return -1;
        int searchLen = from - limit + 1;
        if (searchLen < pattern.Length)
            return -1;
        return text.LastIndexOf(pattern, from, searchLen, StringComparison.Ordinal);
    }

    private static int FindForward(string text, string pattern, int from)
    {
        if (from < 0 || from >= text.Length)
            return -1;
        return text.IndexOf(pattern, from, StringComparison.Ordinal);
    }

    #endregion

    #region Helpers: tag open, tag name, attributes

    /// <summary>
    /// Scans backward from <paramref name="from"/> to find '&lt;',
    /// skipping quoted regions.
    /// </summary>
    private static int FindTagOpenBackward(string text, int scanStart, int from)
    {
        int i = from;
        while (i >= scanStart)
        {
            char ch = text[i];

            if (ch == '"' || ch == '\'')
            {
                char quote = ch;
                i--;
                while (i >= scanStart && text[i] != quote)
                    i--;
                i--;
                continue;
            }

            if (ch == '<')
                return i;

            i--;
        }
        return -1;
    }

    /// <summary>
    /// Extracts the tag name from after '&lt;' up to the first terminator.
    /// For closing tags (&lt;/Tag&gt;), skips the '/'.
    /// For element properties (&lt;Tag.Prop&gt;), returns only the tag part.
    /// </summary>
    private static string ExtractTagNameAfterOpen(
        string text, int tagOpenPos, int limit)
    {
        int start = tagOpenPos + 1;
        if (start >= text.Length)
            return string.Empty;

        if (text[start] == '/')
            start++;

        int end = start;
        int bound = Math.Min(text.Length, limit);
        while (end < bound && !IsTagNameTerminator(text[end]))
            end++;

        if (end <= start)
            return string.Empty;

        string raw = text.Substring(start, end - start);

        int dot = raw.IndexOf('.');
        if (dot >= 0)
            raw = raw.Substring(0, dot);

        return raw;
    }

    #endregion

    #region Helpers: character classification

    private static string ExtractIdentifier(string text, int from, int to)
    {
        int end = Math.Min(to, text.Length);
        if (from >= end)
            return string.Empty;
        return text.Substring(from, end - from);
    }

    private static string StripPrefix(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        int colon = name.IndexOf(':');
        if (colon >= 0 && colon < name.Length - 1)
            return name.Substring(colon + 1);
        if (colon == name.Length - 1)
            return string.Empty;
        return name;
    }

    private static bool IsTagNameTerminator(char ch)
    {
        return char.IsWhiteSpace(ch)
            || ch == '>' || ch == '/' || ch == '"' || ch == '\'';
    }

    private static bool IsNameStartChar(char ch)
    {
        return char.IsLetter(ch) || ch == '_';
    }

    private static bool IsNameChar(char ch)
    {
        return char.IsLetterOrDigit(ch)
            || ch == '_' || ch == '-' || ch == '.' || ch == ':';
    }

    #endregion
}
