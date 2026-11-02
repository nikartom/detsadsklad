using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using DetsadSklad.Views;

namespace DetsadSklad.ViewModels
{
    public class ChoiceItem
    {
        public ChoiceItem(string title, string value, bool isAdd = false)
        {
            Title = title;
            Value = value;
            IsAdd = isAdd;
        }

        public string Title { get; }
        public string Value { get; }
        public bool IsAdd { get; }
        public override string ToString() => Title;
    }

    /// <summary>Выпадающий список: первым идёт «Добавить…», затем «не указано» и варианты.</summary>
    public class ChoiceList : ObservableObject
    {
        private const string AddTitle = "Добавить…";
        private const int FirstValueIndex = 2;

        private readonly string _addDialogTitle;
        private readonly string _addDialogHint;
        private readonly Action _changed;
        private ChoiceItem _selected;
        private ChoiceItem _beforeAdd;

        public ObservableCollection<ChoiceItem> Items { get; } = new ObservableCollection<ChoiceItem>();

        public ChoiceList(IEnumerable<string> values, string current, string emptyTitle, string addDialogTitle, string addDialogHint, Action changed)
        {
            _addDialogTitle = addDialogTitle;
            _addDialogHint = addDialogHint;
            _changed = changed;

            Items.Add(new ChoiceItem(AddTitle, null, isAdd: true));
            var empty = new ChoiceItem(emptyTitle, "");
            Items.Add(empty);
            foreach (var value in values)
                Items.Add(new ChoiceItem(value, value));
            _selected = string.IsNullOrWhiteSpace(current) ? empty : Find(current) ?? InsertValue(current.Trim());
        }

        public string Value => (_selected != null && _selected.IsAdd ? _beforeAdd : _selected)?.Value ?? "";

        public ChoiceItem Selected
        {
            get => _selected;
            set
            {
                // null приходит от самого списка, когда форму меняют на другую, — это не выбор пользователя.
                if (value == null || value == _selected)
                    return;
                if (value.IsAdd)
                {
                    // Окно открываем, когда список закроется. Если пользователь передумает, вернём прежний выбор.
                    _beforeAdd = _selected;
                    _selected = value;
                    Application.Current.Dispatcher.BeginInvoke(new Action(AskForNewValue), DispatcherPriority.Background);
                    return;
                }
                Select(value);
            }
        }

        private void AskForNewValue()
        {
            var text = Ask.Text(_addDialogTitle, _addDialogHint);
            if (text != null)
            {
                Select(Find(text) ?? InsertValue(text));
                return;
            }
            _selected = _beforeAdd;
            OnPropertyChanged(nameof(Selected));
        }

        private void Select(ChoiceItem item)
        {
            _selected = item;
            OnPropertyChanged(nameof(Selected));
            _changed?.Invoke();
        }

        private ChoiceItem Find(string value) =>
            Items.FirstOrDefault(i => !i.IsAdd && string.Equals(i.Value, value.Trim(), StringComparison.OrdinalIgnoreCase));

        private ChoiceItem InsertValue(string value)
        {
            var item = new ChoiceItem(value, value);
            Items.Insert(FirstValueIndex, item);
            return item;
        }
    }
}
