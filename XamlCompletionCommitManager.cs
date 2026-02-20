// Copyright © 2026 Virich Pavlo. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;

namespace A2v10XamlAutocomplete;

internal class XamlCompletionCommitManager : IAsyncCompletionCommitManager
{
    static readonly ImmutableArray<char> _commitChars =
        ImmutableArray.Create(' ', '>', '/', '=', '"', '\'');

    public IEnumerable<char> PotentialCommitCharacters => _commitChars;

    public bool ShouldCommitCompletion(
        IAsyncCompletionSession session,
        SnapshotPoint location,
        char typedChar,
        CancellationToken token)
    {
        switch (typedChar)
        {
            case ' ':
            case '>':
            case '/':
            case '"':
                return true;
            default:
                return false;
        }
    }

    public CommitResult TryCommit(
        IAsyncCompletionSession session,
        ITextBuffer buffer,
        CompletionItem item,
        char typedChar,
        CancellationToken token)
    {
        if (!item.Properties.TryGetProperty<Element>(nameof(Element), out var elem))
            return CommitResult.Unhandled;

        switch (elem.Kind)
        {
            case Element.ElemKind.Tag:
                return CommitTag(session, buffer, item, typedChar);
            case Element.ElemKind.ClosingTag:
                return CommitClosingTag(session, buffer, item, typedChar);
            case Element.ElemKind.Property:
            case Element.ElemKind.AttachedProperty:
                return CommitAttribute(session, buffer, item, typedChar);
            case Element.ElemKind.EnumValue:
            case Element.ElemKind.Boolean:
                return CommitValue(session, buffer, item, typedChar);
            case Element.ElemKind.Comment:
                return CommitComment(session, buffer, item);
            case Element.ElemKind.CData:
                return CommitCData(session, buffer, item);
            default:
                return CommitResult.Unhandled;
        }
    }

    static CommitResult CommitTag(
        IAsyncCompletionSession session,
        ITextBuffer buffer,
        CompletionItem item,
        char typedChar)
    {
        // typedChar == '\0' means Tab or Enter
        if (typedChar == '\0')
        {
            var span = session.ApplicableToSpan
                .GetSpan(buffer.CurrentSnapshot);
            using (var edit = buffer.CreateEdit())
            {
                edit.Replace(span, item.DisplayText + " ");
                edit.Apply();
            }
            return new CommitResult(
                true, CommitBehavior.SuppressFurtherTypeCharCommandHandlers);
        }

        if (typedChar == '>' || typedChar == '/')
        {
            var span = session.ApplicableToSpan
                .GetSpan(buffer.CurrentSnapshot);
            using (var edit = buffer.CreateEdit())
            {
                edit.Replace(span, item.DisplayText + typedChar);
                edit.Apply();
            }
            return new CommitResult(
                true, CommitBehavior.SuppressFurtherTypeCharCommandHandlers);
        }

        if (typedChar == ' ')
        {
            // User typed space; VS will insert the space after commit.
            // We just insert the tag name without trailing space.
            return CommitResult.Unhandled;
        }

        return CommitResult.Unhandled;
    }

    static CommitResult CommitClosingTag(
        IAsyncCompletionSession session,
        ITextBuffer buffer,
        CompletionItem item,
        char typedChar)
    {
        var span = session.ApplicableToSpan
            .GetSpan(buffer.CurrentSnapshot);

        // If user typed '>', don't duplicate it
        String suffix = typedChar == '>' ? "" : ">";

        using (var edit = buffer.CreateEdit())
        {
            edit.Replace(span, item.DisplayText + suffix);
            edit.Apply();
        }

        return new CommitResult(
            true, CommitBehavior.SuppressFurtherTypeCharCommandHandlers);
    }

    static CommitResult CommitAttribute(
        IAsyncCompletionSession session,
        ITextBuffer buffer,
        CompletionItem item,
        char typedChar)
    {
        var span = session.ApplicableToSpan
            .GetSpan(buffer.CurrentSnapshot);
        int spanStart = span.Start.Position;
        String insertText = item.DisplayText + "=\"\"";

        using (var edit = buffer.CreateEdit())
        {
            edit.Replace(span, insertText);
            edit.Apply();
        }

        // Caret between the quotes: spanStart + AttrName="[here]"
        // spanStart is stable — our edit only replaces text starting at this position.
        int caretPosition = spanStart + item.DisplayText.Length + 2;
        MoveCaretSafe(session, buffer, caretPosition);

        return new CommitResult(
            true, CommitBehavior.SuppressFurtherTypeCharCommandHandlers);
    }

    static CommitResult CommitValue(
        IAsyncCompletionSession session,
        ITextBuffer buffer,
        CompletionItem item,
        char typedChar)
    {
        if (typedChar == '"')
        {
            // User typed closing quote; let VS do the standard insert.
            return CommitResult.Unhandled;
        }

        // Tab/Enter: insert value + closing quote
        var span = session.ApplicableToSpan
            .GetSpan(buffer.CurrentSnapshot);

        using (var edit = buffer.CreateEdit())
        {
            edit.Replace(span, item.DisplayText + "\"");
            edit.Apply();
        }

        return new CommitResult(
            true, CommitBehavior.SuppressFurtherTypeCharCommandHandlers);
    }

    static CommitResult CommitComment(
        IAsyncCompletionSession session,
        ITextBuffer buffer,
        CompletionItem item)
    {
        var span = session.ApplicableToSpan
            .GetSpan(buffer.CurrentSnapshot);
        int spanStart = span.Start.Position;
        String insertText = "!--  -->";

        using (var edit = buffer.CreateEdit())
        {
            edit.Replace(span, insertText);
            edit.Apply();
        }

        int caretPosition = spanStart + 4;
        MoveCaretSafe(session, buffer, caretPosition);

        return new CommitResult(
            true, CommitBehavior.SuppressFurtherTypeCharCommandHandlers);
    }

    static CommitResult CommitCData(
        IAsyncCompletionSession session,
        ITextBuffer buffer,
        CompletionItem item)
    {
        var span = session.ApplicableToSpan
            .GetSpan(buffer.CurrentSnapshot);
        int spanStart = span.Start.Position;
        String insertText = "![CDATA[]]>";

        using (var edit = buffer.CreateEdit())
        {
            edit.Replace(span, insertText);
            edit.Apply();
        }

        int caretPosition = spanStart + 8;
        MoveCaretSafe(session, buffer, caretPosition);

        return new CommitResult(
            true, CommitBehavior.SuppressFurtherTypeCharCommandHandlers);
    }

    static void MoveCaretSafe(
        IAsyncCompletionSession session,
        ITextBuffer buffer,
        int caretPosition)
    {
        var newSnapshot = buffer.CurrentSnapshot;
        if (caretPosition < 0 || caretPosition > newSnapshot.Length)
            return;
        var caretPoint = new SnapshotPoint(newSnapshot, caretPosition);
        session.TextView.Caret.MoveTo(caretPoint);
    }
}
