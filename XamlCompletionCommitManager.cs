// Copyright © 2026 Virich Pavlo. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

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
            MoveCaretDeferred(session.TextView, buffer, spanStart + result.CaretOffset);

        return new CommitResult(
            true, CommitBehavior.SuppressFurtherTypeCharCommandHandlers);
    }

    static void MoveCaretDeferred(
        ITextView textView,
        ITextBuffer buffer,
        int caretPosition)
    {
        var snapshot = buffer.CurrentSnapshot;
        if (caretPosition < 0 || caretPosition > snapshot.Length)
            return;

        // ITrackingPoint survives subsequent snapshot changes (e.g. auto-formatting)
        // so the position stays correct even if VS re-indents the line.
        var trackingPoint = snapshot.CreateTrackingPoint(
            caretPosition, PointTrackingMode.Negative);

        // VS completion framework repositions the caret after TryCommit returns,
        // overriding any synchronous MoveTo. Wait for the next LayoutChanged event
        // which fires after VS finishes all post-commit processing and layout.
        EventHandler<TextViewLayoutChangedEventArgs> handler = null;
        handler = (s, e) =>
        {
            textView.LayoutChanged -= handler;
            var point = trackingPoint.GetPoint(textView.TextSnapshot);
            textView.Caret.MoveTo(point);
        };
        textView.LayoutChanged += handler;
    }
}
