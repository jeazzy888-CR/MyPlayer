using System.Collections.Generic;

namespace MyPlayer_NET9.ViewModels
{
    /// <summary>
    /// Нарезает плитки (альбомы, плейлисты) на ряды по <c>columns</c> штук для виртуализированной сетки.
    /// Каждый ряд ВСЕГДА содержит ровно <c>columns</c> ячеек — недостающие в последнем ряду это null.
    /// Благодаря этому ряд в XAML — это фиксированные ячейки, а не вложенный ItemsControl:
    /// при прокрутке переиспользуется уже построенное дерево элементов вместо создания нового.
    /// </summary>
    public static class TileRows
    {
        public static List<List<T?>> Build<T>(IReadOnlyList<T> items, int columns) where T : class
        {
            var rows = new List<List<T?>>((items.Count + columns - 1) / columns);

            for (int start = 0; start < items.Count; start += columns)
            {
                var row = new List<T?>(columns);
                for (int i = 0; i < columns; i++)
                    row.Add(start + i < items.Count ? items[start + i] : null);
                rows.Add(row);
            }

            return rows;
        }
    }
}
