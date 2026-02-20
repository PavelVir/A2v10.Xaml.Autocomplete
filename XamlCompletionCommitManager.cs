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

        InsertionResult result;
        switch (elem.Kind)
        {
            case Element.ElemKind.Tag:
                result = CommitCalculator.Tag(item.DisplayText, typedChar);
                break;
            case Element.ElemKind.ClosingTag:
                result = CommitCalculator.ClosingTag(item.DisplayText, typedChar);
                break;
            case Element.ElemKind.Property:
            case Element.ElemKind.AttachedProperty:
                result = CommitCalculator.Attribute(item.DisplayText);
                break;
            case Element.ElemKind.EnumValue:
            case Element.ElemKind.Boolean:
                result = CommitCalculator.Value(item.DisplayText, typedChar);
                break;
            case Element.ElemKind.Comment:
                result = CommitCalculator.Comment();
                break;
            case Element.ElemKind.CData:
                result = CommitCalculator.CData();
                break;
            default:
                return CommitResult.Unhandled;
        }

        if (!result.Handled)
            return CommitResult.Unhandled;

        var span = session.ApplicableToSpan
            .GetSpan(buffer.CurrentSnapshot);
        int spanStart = span.Start.Position;

        using (var edit = buffer.CreateEdit())
        {
            edit.Replace(span, result.Text);
            edit.Apply();
        }

        if (result.CaretOffset >= 0)
            MoveCaretSafe(session, buffer, spanStart + result.CaretOffset);

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
