using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace MyPlayer_NET9.ViewModels
{
    /// <summary>
    /// ObservableCollection, которая умеет заменить ВСЁ содержимое одним уведомлением.
    /// Обычные Clear() + N раз Add() шлют N+1 уведомлений, и UI на каждое пересчитывает список —
    /// именно из-за этого поиск и сортировка тормозили на больших библиотеках.
    /// </summary>
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        /// <summary>Заменяет содержимое целиком, UI получает одно уведомление Reset.</summary>
        public void ReplaceAll(IEnumerable<T> items)
        {
            // Материализуем до очистки: источником может быть сама эта коллекция.
            var snapshot = items.ToList();

            CheckReentrancy();
            Items.Clear();
            foreach (var item in snapshot)
                Items.Add(item);

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
