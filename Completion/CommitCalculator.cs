// Copyright © 2026 Virich Pavlo. All rights reserved.

using System;

namespace A2v10XamlAutocomplete;

internal readonly struct InsertionResult
{
    public readonly string Text;
    public readonly int CaretOffset; // from span start; -1 = don't move
    public readonly bool Handled;

    public static InsertionResult Unhandled => default;

    public InsertionResult(string text, int caretOffset)
    {
        Text = text;
        CaretOffset = caretOffset;
        Handled = true;
    }
}

internal static class CommitCalculator
{
    public static InsertionResult Tag(string displayText, char typedChar)
    {
        if (typedChar == '\0')
            return new InsertionResult(displayText + " ", caretOffset: -1);

        if (typedChar == '>' || typedChar == '/')
            return new InsertionResult(displayText + typedChar, caretOffset: -1);

        // Space and other chars: let VS handle the default commit behavior.
        // The typed char itself acts as the tag-to-attribute separator.
        return InsertionResult.Unhandled;
    }

    public static InsertionResult ClosingTag(string displayText, char typedChar)
    {
        string suffix = typedChar == '>' ? "" : ">";
        return new InsertionResult(displayText + suffix, caretOffset: -1);
    }

    public static InsertionResult Attribute(string displayText)
    {
        string text = displayText + "=\"\"";
        int caretOffset = displayText.Length + 2; // between quotes
        return new InsertionResult(text, caretOffset);
    }

    public static InsertionResult Value(string displayText, char typedChar)
    {
        if (typedChar == '"')
            return InsertionResult.Unhandled;

        return new InsertionResult(displayText + "\"", caretOffset: -1);
    }

    public static InsertionResult Comment()
    {
        return new InsertionResult("!--  -->", caretOffset: 4);
    }

    public static InsertionResult CData()
    {
        return new InsertionResult("![CDATA[]]>", caretOffset: 8);
    }
}
