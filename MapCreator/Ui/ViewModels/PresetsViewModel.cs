using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapCreator.Classes;

namespace MapCreator.Ui.ViewModels
{
    /// <summary>
    /// The preset dropdown and its actions; selecting a preset copies it into the options
    /// </summary>
    public sealed partial class PresetsViewModel : ViewModelBase
    {
        private readonly OptionsViewModel options;
        private readonly AppSettings settings;
        private readonly PresetStore store;
        private string selectedName;
        private bool isRenaming;

        [ObservableProperty]
        private bool isModified;

        [ObservableProperty]
        private bool isEditing;

        [ObservableProperty]
        private string editText = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasEditError))]
        private string editError = "";

        internal PresetsViewModel(OptionsViewModel options, AppSettings settings, PresetStore store)
        {
            this.options = options;
            this.settings = settings;
            this.store = store;
            this.Names = new ObservableCollection<string>(store.Presets.Select(p => p.Name));
            this.selectedName = store.Find(settings.ActivePreset)?.Name;
            settings.ActivePreset = this.selectedName ?? "";
            this.isModified = this.ComputeModified();
            options.PropertyChanged += this.OnOptionsChanged;
        }

        public ObservableCollection<string> Names { get; }

        public bool IsEditable => this.options.IsEditable;

        public bool HasEditError => !string.IsNullOrEmpty(this.EditError);

        /// <summary>
        /// The active preset, null for none; setting a name loads that preset
        /// </summary>
        public string SelectedName
        {
            get => this.selectedName;
            set
            {
                if (value == null || value == this.selectedName)
                {
                    return;
                }

                if (!this.IsEditable)
                {
                    this.OnPropertyChanged();
                    return;
                }

                this.Load(value);
            }
        }

        private bool CanChange => this.IsEditable && this.selectedName != null;

        [RelayCommand(CanExecute = nameof(CanChange))]
        private void Save()
        {
            this.store.Overwrite(this.selectedName, this.settings);
            this.Persist();
            this.RefreshState();
        }

        [RelayCommand(CanExecute = nameof(IsEditable))]
        private void SaveAs()
        {
            this.BeginEdit("", false);
        }

        [RelayCommand(CanExecute = nameof(CanChange))]
        private void Rename()
        {
            this.BeginEdit(this.selectedName, true);
        }

        [RelayCommand(CanExecute = nameof(CanChange))]
        private void Delete()
        {
            this.CancelEdit();
            this.store.Delete(this.selectedName);
            this.Names.Remove(this.selectedName);
            this.selectedName = null;
            this.settings.ActivePreset = "";
            this.Persist();
            SettingsSaver.Request();
            this.OnPropertyChanged(nameof(this.SelectedName));
            this.RefreshState();
        }

        [RelayCommand]
        private void ConfirmEdit()
        {
            if (!this.IsEditable)
            {
                return;
            }

            var name = (this.EditText ?? "").Trim();
            if (this.isRenaming)
            {
                this.ConfirmRename(name);
            }
            else
            {
                this.ConfirmSaveAs(name);
            }
        }

        [RelayCommand]
        private void CancelEdit()
        {
            this.IsEditing = false;
            this.EditError = "";
        }

        private void ConfirmSaveAs(string name)
        {
            var error = this.store.Add(name, this.settings);
            if (error != null)
            {
                this.EditError = error;
                return;
            }

            this.Names.Add(name);
            this.selectedName = name;
            this.settings.ActivePreset = name;
            this.Persist();
            SettingsSaver.Request();
            this.CancelEdit();
            this.OnPropertyChanged(nameof(this.SelectedName));
            this.RefreshState();
        }

        private void ConfirmRename(string name)
        {
            var oldName = this.selectedName;
            var error = this.store.Rename(oldName, name);
            if (error != null)
            {
                this.EditError = error;
                return;
            }

            this.selectedName = name;
            this.settings.ActivePreset = name;
            this.Names[this.Names.IndexOf(oldName)] = name;
            this.Persist();
            SettingsSaver.Request();
            this.CancelEdit();
            this.OnPropertyChanged(nameof(this.SelectedName));
            this.RefreshState();
        }

        private void Load(string name)
        {
            var preset = this.store.Find(name);
            if (preset == null)
            {
                return;
            }

            this.CancelEdit();
            preset.ApplyTo(this.settings);
            this.settings.ActivePreset = preset.Name;
            this.selectedName = preset.Name;
            this.options.ReloadFromSettings();
            this.OnPropertyChanged(nameof(this.SelectedName));
            this.RefreshState();
        }

        private void BeginEdit(string text, bool rename)
        {
            this.isRenaming = rename;
            this.EditText = text;
            this.EditError = "";
            this.IsEditing = true;
        }

        private void Persist()
        {
            try
            {
                this.store.Save();
            }
            catch (Exception ex)
            {
                // Runs from UI handlers, where any exception would end the program
                AppLog.Log(string.Format("Presets not saved ({0})", ex.Message), LogLevel.Warning);
            }
        }

        private bool ComputeModified()
        {
            var active = this.store.Find(this.selectedName);
            return active != null && !active.HasSameOptions(this.settings);
        }

        private void OnOptionsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(OptionsViewModel.IsEditable))
            {
                this.OnPropertyChanged(nameof(this.IsEditable));
            }

            this.RefreshState();
        }

        private void RefreshState()
        {
            this.IsModified = this.ComputeModified();
            this.SaveCommand.NotifyCanExecuteChanged();
            this.SaveAsCommand.NotifyCanExecuteChanged();
            this.RenameCommand.NotifyCanExecuteChanged();
            this.DeleteCommand.NotifyCanExecuteChanged();
        }
    }
}
