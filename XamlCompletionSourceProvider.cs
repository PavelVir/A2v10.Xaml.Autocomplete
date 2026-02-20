// Copyright © 2026 Virich Pavlo. All rights reserved.

using System.Collections.Concurrent;
using System.ComponentModel.Composition;

using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace A2v10XamlAutocomplete;

[Export(typeof(IAsyncCompletionSourceProvider))]
[Name("A2v10 XAML completion provider")]
[ContentType("xml")]
internal class XamlCompletionSourceProvider : IAsyncCompletionSourceProvider
{
    private readonly ConcurrentDictionary<ITextView, IAsyncCompletionSource> cache = new();

    public IAsyncCompletionSource GetOrCreate(ITextView textView)
    {
        return cache.GetOrAdd(textView, tv =>
        {
            var source = new XamlCompletionSource();
            tv.Closed += (o, e) => cache.TryRemove(tv, out _);
            return source;
        });
    }
}
