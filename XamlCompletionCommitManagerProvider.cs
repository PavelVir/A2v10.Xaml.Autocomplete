// Copyright © 2026 Virich Pavlo. All rights reserved.

using System.Collections.Concurrent;
using System.ComponentModel.Composition;

using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace A2v10XamlAutocomplete;

[Export(typeof(IAsyncCompletionCommitManagerProvider))]
[Name("A2v10 XAML element commit manager provider")]
[ContentType("xml")]
internal class XamlCompletionCommitManagerProvider : IAsyncCompletionCommitManagerProvider
{
    private readonly ConcurrentDictionary<ITextView, IAsyncCompletionCommitManager> _cache = new();

    public IAsyncCompletionCommitManager GetOrCreate(ITextView textView)
    {
        return _cache.GetOrAdd(textView, tv =>
        {
            var manager = new XamlCompletionCommitManager();
            tv.Closed += (o, e) => _cache.TryRemove(tv, out _);
            return manager;
        });
    }
}
