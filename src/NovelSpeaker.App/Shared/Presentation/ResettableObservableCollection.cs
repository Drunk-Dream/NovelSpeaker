using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace NovelSpeaker.App.Shared.Presentation;

/// <summary>
/// Allows an owner to publish one reset after mutating several existing items without
/// exposing an item-by-item collection notification sequence to the UI.
/// </summary>
internal sealed class ResettableObservableCollection<T> : ObservableCollection<T>
{
    public void NotifyReset()
    {
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
