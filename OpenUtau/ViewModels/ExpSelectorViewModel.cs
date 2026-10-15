using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using Avalonia.Media;
using OpenUtau.App.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public partial class ExpSelectorViewModel : ViewModelBase, ICmdSubscriber {
        [Reactive] public partial int Index { get; set; }
        [Reactive] public partial int SelectedIndex { get; set; }
        [Reactive] public partial ExpDisMode DisplayMode { get; set; }
        [Reactive] public partial UExpressionDescriptor? Descriptor { get; set; }
        public string Abbr {
            get{
                if (Descriptor == null) {
                    return "";
                }
                return Descriptor.abbr;
            }
        }
        public ObservableCollection<UExpressionDescriptor> Descriptors => descriptors;
        public string Header => header.Value;
        [Reactive] public partial IBrush TagBrush { get; set; }
        [Reactive] public partial IBrush Background { get; set; }

        ObservableCollection<UExpressionDescriptor> descriptors = new ObservableCollection<UExpressionDescriptor>();
        ObservableAsPropertyHelper<string> header;

        public ExpSelectorViewModel() {
            DocManager.Inst.AddSubscriber(this);
            this.WhenAnyValue(x => x.DisplayMode)
                .Subscribe(_ => RefreshBrushes());
            this.WhenAnyValue(x => x.Descriptor)
                .Select(descriptor => descriptor == null ? string.Empty : descriptor.abbr.ToUpperInvariant())
                .ToProperty(this, x => x.Header, out header);
            this.WhenAnyValue(x => x.Descriptor)
                .OfType<UExpressionDescriptor>()
                .Subscribe(SelectionChanged);
            this.WhenAnyValue(x => x.Index, x => x.Descriptors)
                .Subscribe(tuple => {
                    SetExp(DocManager.Inst.Project.expSelectors[tuple.Item1]);
                });
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(_ => RefreshBrushes());
            TagBrush = ThemeManager.ExpNameBrush;
            Background = ThemeManager.ExpBrush;
            OnListChange();
        }

        public bool SetExp(string abbr) {
            if(Descriptors.Any(d => d.abbr == abbr)) {
                Descriptor = Descriptors.First(d => d.abbr == abbr);
                return true;
            } else {
                if (Descriptors != null && Descriptors.Count > Index) {
                    Descriptor = Descriptors[Index];
                }
                return false;
            }
        }

        public void OnSelected(bool store) {
            if (DisplayMode != ExpDisMode.Visible && Descriptor != null) {
                DocManager.Inst.ExecuteCmd(new SelectExpressionNotification(Descriptor.abbr, Index, true));
            }
            if(store) {
                var project = DocManager.Inst.Project;
                project.expSecondary = project.expPrimary;
                project.expPrimary = Index;
            }
        }

        void SelectionChanged(UExpressionDescriptor? descriptor) {
            if (descriptor != null) {
                DocManager.Inst.ExecuteCmd(new SelectExpressionNotification(descriptor.abbr, Index, DisplayMode != ExpDisMode.Visible));
            }
            if (!string.IsNullOrEmpty(Abbr)) {
                DocManager.Inst.Project.expSelectors[Index] = Abbr;
            }
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is LoadPartNotification loadPart) {
                activeTrackNo = loadPart.part.trackNo;
                OnListChange(allowSuggestions: true);
            } else if (cmd is LoadProjectNotification) {
                activeTrackNo = null;
                OnListChange(allowSuggestions: true);
            } else if (cmd is ConfigureExpressionsCommand || cmd is ValidateProjectNotification) {
                // Undoing the automatic "add suggestions" commit must not redo it right away.
                OnListChange(allowSuggestions: !isUndo || cmd is not ConfigureExpressionsCommand);
            } else if (cmd is SelectExpressionNotification) {
                OnSelectExp((SelectExpressionNotification)cmd);
            }
        }

        // The track of the part currently shown in the piano roll, shared by all selector instances.
        static int? activeTrackNo;
        bool refreshing;

        private void OnListChange(bool allowSuggestions = true) {
            // Adding suggestions executes ConfigureExpressionsCommand, which calls back into here.
            if (refreshing) {
                return;
            }
            refreshing = true;
            try {
                OnListChangeInner(allowSuggestions);
            } finally {
                refreshing = false;
            }
        }

        private void OnListChangeInner(bool allowSuggestions) {
            var project = DocManager.Inst.Project;
            var selectedIndex = SelectedIndex;
            var activeAbbr = Descriptor?.abbr;
            Descriptors.Clear();
            UTrack? track = activeTrackNo.HasValue && activeTrackNo.Value < project.tracks.Count
                ? project.tracks[activeTrackNo.Value]
                : null;
            var renderer = track?.RendererSettings?.Renderer;
            List<UExpressionDescriptor> exps;
            if (track != null && renderer != null) {
                if (allowSuggestions) {
                    AddRendererSuggestions(project, track, renderer);
                }
                exps = track.GetSupportedExps(project);
                // Masked curves feed expression graphs whatever the renderer is.
                exps.AddRange(project.expressions.Values.Where(
                    exp => exp.type == UExpressionType.MaskedCurve && !exps.Contains(exp)));
            } else {
                exps = project.expressions.Values.ToList();
            }
            exps.ForEach(Descriptors.Add);
            // Re-resolve the selected descriptor: the rebuilt list holds fresh instances
            // (e.g. the track's voice color clone), or may no longer support the abbr at all.
            if (activeAbbr != null) {
                SetExp(activeAbbr);
            }
            if (selectedIndex >= descriptors.Count) {
                selectedIndex = Index;
            }
            SelectedIndex = selectedIndex;
        }

        // Merge the renderer's suggested expressions into the project, as the
        // expressions dialog's "Get suggestions" does, but automatically.
        private static void AddRendererSuggestions(UProject project, UTrack track, IRenderer renderer) {
            UExpressionDescriptor[]? suggestions = null;
            try {
                suggestions = renderer.GetSuggestedExpressions(track.Singer, track.RendererSettings);
            } catch {
                return;
            }
            if (suggestions == null) {
                return;
            }
            var missing = suggestions
                .Where(exp => !project.expressions.ContainsKey(exp.abbr))
                .ToArray();
            if (missing.Length == 0) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.project.exp");
            DocManager.Inst.ExecuteCmd(new ConfigureExpressionsCommand(
                project, project.expressions.Values.Concat(missing).ToArray()));
            DocManager.Inst.EndUndoGroup();
        }

        private void OnSelectExp(SelectExpressionNotification cmd) {
            if (Descriptors.Count == 0) {
                return;
            }
            if (cmd.SelectorIndex == Index) {
                // The selector list is renderer-filtered and may not contain the exp.
                var descriptor = Descriptors.FirstOrDefault(d => d.abbr == cmd.ExpKey);
                if (descriptor != null) {
                    SelectedIndex = Descriptors.IndexOf(descriptor);
                }
                DisplayMode = ExpDisMode.Visible;
            } else if (cmd.UpdateShadow) {
                DisplayMode = DisplayMode == ExpDisMode.Visible ? ExpDisMode.Shadow : ExpDisMode.Hidden;
            }
        }

        private void RefreshBrushes() {
            TagBrush = DisplayMode == ExpDisMode.Visible
                    ? ThemeManager.ExpActiveNameBrush
                    : DisplayMode == ExpDisMode.Shadow
                    ? ThemeManager.ExpShadowNameBrush
                    : ThemeManager.ExpNameBrush;
            Background = DisplayMode == ExpDisMode.Visible
                    ? ThemeManager.ExpActiveBrush
                    : DisplayMode == ExpDisMode.Shadow
                    ? ThemeManager.ExpShadowBrush
                    : ThemeManager.ExpBrush;
        }
    }
}
