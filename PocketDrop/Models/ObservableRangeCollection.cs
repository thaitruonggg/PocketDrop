using System;
using System.Collections.Generic;
using System.Text;

namespace PocketDrop
{
    public class ObservableRangeCollection<T> : System.Collections.ObjectModel.ObservableCollection<T>
    {
        public void AddRange(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            foreach (var item in collection)
            {
                Items.Add(item);
            }

            OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        }
    }
}
