using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Windows.Forms;

namespace SharpOcarina.Shared
{
    /// <summary>
    /// Shared Archarina64 desktop treatment for the three WinForms editors.
    /// The palette is intentionally restrained: N64-era stone colors and
    /// pixel-scale texture detail, rendered at modern control resolutions.
    /// </summary>
    public static class ArcharinaUiTheme
    {
        private static readonly Dictionary<Control, bool> HookedControls = new Dictionary<Control, bool>();
        private static readonly Dictionary<Form, bool> AppliedForms = new Dictionary<Form, bool>();
        private static readonly Dictionary<Control, float> FontBaselines = new Dictionary<Control, float>();
        private static readonly Dictionary<Button, string> ButtonTexts = new Dictionary<Button, string>();
        private static readonly Dictionary<Button, Color> ButtonBackColors = new Dictionary<Button, Color>();
        private static readonly Dictionary<Label, string> LabelTexts = new Dictionary<Label, string>();
        private static readonly Dictionary<Control, bool> UpdatingText = new Dictionary<Control, bool>();
        private static readonly Dictionary<Form, bool> ResizeHooks = new Dictionary<Form, bool>();
        private static readonly PropertyInfo DoubleBufferedProperty = typeof(Control).GetProperty(
            "DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
        private const int WM_SETREDRAW = 0x000B;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
        private static Bitmap stoneTexture;

        public static readonly Color FormBack = Color.FromArgb(18, 21, 20);
        public static readonly Color PanelBack = Color.FromArgb(150, 15, 20, 22);
        // Native WinForms controls such as TreeView reject alpha colors;
        // translucent treatment is reserved for custom-painted panels.
        public static readonly Color SurfaceBack = Color.FromArgb(24, 29, 31);
        public static readonly Color InputBack = Color.FromArgb(12, 17, 19);
        public static readonly Color TextColor = Color.White;
        public static readonly Color MutedTextColor = Color.FromArgb(218, 224, 210);
        public static readonly Color StoneEdge = Color.FromArgb(166, 158, 116);
        public static readonly Color StoneShadow = Color.FromArgb(9, 11, 10);
        public static readonly Color GoldAccent = Color.FromArgb(224, 184, 69);
        public static readonly Color GreenAccent = Color.FromArgb(91, 143, 92);

        public static void ApplyOpenForms()
        {
            for (int i = Application.OpenForms.Count - 1; i >= 0; i--)
            {
                Form form = Application.OpenForms[i];
                if (form == null || form.IsDisposed) continue;
                Apply(form);
            }
        }

        public static void Apply(Form form)
        {
            if (form == null || form.IsDisposed) return;
            form.SuspendLayout();
            form.BackColor = FormBack;
            form.ForeColor = TextColor;
            form.Font = GetReadableFont(form, 9f, FontStyle.Regular);
            EnableBufferedSurfaces(form);
            form.Paint -= PaintFormBackground;
            form.Paint += PaintFormBackground;
            HookResizeTransaction(form);
            ApplyControl(form);
            form.ResumeLayout(true);
            AppliedForms[form] = true;
        }

        private static void HookResizeTransaction(Form form)
        {
            if (ResizeHooks.ContainsKey(form)) return;
            ResizeHooks[form] = true;
            form.ResizeBegin += delegate
            {
                if (form.IsDisposed || !form.IsHandleCreated) return;
                form.SuspendLayout();
                SendMessage(form.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            };
            form.ResizeEnd += delegate
            {
                if (form.IsDisposed || !form.IsHandleCreated) return;
                SendMessage(form.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                form.ResumeLayout(true);
                form.Invalidate(true);
                form.Update();
            };
        }

        private static void EnableBufferedSurfaces(Control control)
        {
            if (control == null) return;

            // OpenTK owns its own swap chain. GDI buffering on a GLControl
            // can replace a presented frame with a blank GDI frame.
            if (!IsOpenGlControl(control))
            {
                try
                {
                    if (DoubleBufferedProperty != null)
                        DoubleBufferedProperty.SetValue(control, true, null);
                }
                catch (Exception)
                {
                    // Some native controls reject the protected property.
                }
            }

            foreach (Control child in control.Controls)
                EnableBufferedSurfaces(child);
        }

        private static bool IsOpenGlControl(Control control)
        {
            for (Type type = control.GetType(); type != null; type = type.BaseType)
                if (String.Equals(type.FullName, "OpenTK.GLControl", StringComparison.Ordinal))
                    return true;
            return false;
        }

        public static Font GetUiFont(float size, FontStyle style)
        {
            float readableSize = Math.Max(8f, size);
            string[] candidates = { "Segoe UI", "Tahoma", "Arial" };
            for (int i = 0; i < candidates.Length; i++)
            {
                try { return new Font(candidates[i], readableSize, style, GraphicsUnit.Point); }
                catch (ArgumentException) { }
            }
            return new Font(SystemFonts.MessageBoxFont.FontFamily, readableSize, style, GraphicsUnit.Point);
        }

        private static Font GetReadableFont(Control control, float minimum, FontStyle style)
        {
            float baseline;
            if (!FontBaselines.TryGetValue(control, out baseline))
            {
                baseline = control == null || control.Font == null ? minimum : control.Font.Size;
                FontBaselines[control] = baseline;
            }
            return GetUiFont(Math.Max(minimum, baseline + 2f), style);
        }

        private static void ApplyControl(Control control)
        {
            if (control == null) return;
            if (IsOpenGlControl(control)) return;

            ScrollableControl scrollable = control as ScrollableControl;
            if (scrollable != null)
            {
                string scrollPolicy = Convert.ToString(control.Tag);
                if (String.Equals(scrollPolicy, "NoScrollBars", StringComparison.Ordinal))
                {
                    scrollable.AutoScroll = false;
                    scrollable.HorizontalScroll.Visible = false;
                    scrollable.VerticalScroll.Visible = false;
                }
                else if (String.Equals(scrollPolicy, "HiddenScrollBars", StringComparison.Ordinal))
                {
                    // Keep wheel scrolling and keyboard navigation functional,
                    // but do not expose a second visual rail in compact editor
                    // shelves and property hosts.
                    scrollable.AutoScroll = true;
                    scrollable.HorizontalScroll.Visible = false;
                    scrollable.VerticalScroll.Visible = false;
                }
                else if (String.Equals(scrollPolicy, "SlimScrollBars", StringComparison.Ordinal))
                {
                    scrollable.HorizontalScroll.Visible = false;
                }
                else if (!String.Equals(scrollPolicy, "KeepScrollBars", StringComparison.Ordinal))
                {
                    // Native WinForms rails ignore BackColor and can remain
                    // bright white on otherwise dark editor surfaces. Keep
                    // scrolling available through the wheel and keyboard while
                    // removing the unthemed rail from every workspace.
                    scrollable.HorizontalScroll.Visible = false;
                    scrollable.VerticalScroll.Visible = false;
                }
                if (!HookedControls.ContainsKey(scrollable))
                {
                    HookedControls[scrollable] = true;
                    scrollable.Layout += delegate
                    {
                        string policy = Convert.ToString(scrollable.Tag);
                        if (String.Equals(policy, "HiddenScrollBars", StringComparison.Ordinal) ||
                            String.Equals(policy, "NoScrollBars", StringComparison.Ordinal) ||
                            String.IsNullOrEmpty(policy))
                        {
                            scrollable.HorizontalScroll.Visible = false;
                            scrollable.VerticalScroll.Visible = false;
                        }
                    };
                }
            }

            Button button = control as Button;
            if (button != null)
            {
                if (!ButtonBackColors.ContainsKey(button) && button.BackColor != Color.Transparent &&
                    button.BackColor != SystemColors.Control && button.BackColor != Color.Empty)
                    ButtonBackColors[button] = button.BackColor;
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                button.BackColor = Color.Transparent;
                button.ForeColor = TextColor;
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseDownBackColor = Color.Transparent;
                button.FlatAppearance.MouseOverBackColor = Color.Transparent;
                button.Font = GetReadableFont(button, 9f, FontStyle.Bold);
                CaptureButtonText(button);
                HookButton(button);
            }
            else if (control is CheckBox || control is RadioButton)
            {
                ButtonBase toggle = (ButtonBase)control;
                toggle.BackColor = Color.Transparent;
                toggle.ForeColor = TextColor;
                toggle.Font = GetReadableFont(toggle, 8f, FontStyle.Regular);
            }
            else if (control is TextBoxBase || control is ComboBox || control is NumericUpDown ||
                     control is ListBox || control is ListView || control is TreeView)
            {
                control.BackColor = InputBack;
                control.ForeColor = TextColor;
                control.Font = GetReadableFont(control, 8f, FontStyle.Regular);
                TextBoxBase textBox = control as TextBoxBase;
                if (textBox != null) textBox.BorderStyle = BorderStyle.FixedSingle;
                ListBox listBox = control as ListBox;
                if (listBox != null) listBox.BorderStyle = BorderStyle.FixedSingle;
                ListView listView = control as ListView;
                if (listView != null) listView.BorderStyle = BorderStyle.FixedSingle;
                TreeView treeView = control as TreeView;
                if (treeView != null) treeView.BorderStyle = BorderStyle.FixedSingle;
                ComboBox combo = control as ComboBox;
                if (combo != null)
                {
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.BackColor = InputBack;
                    combo.ForeColor = TextColor;
                    combo.DrawMode = DrawMode.OwnerDrawFixed;
                    combo.ItemHeight = Math.Max(18, combo.Font.Height + 4);
                    HookComboBox(combo);
                }
            }
            else if (control is DataGridView)
            {
                DataGridView grid = (DataGridView)control;
                grid.BackgroundColor = InputBack;
                grid.GridColor = Color.FromArgb(72, 78, 68);
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceBack;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = TextColor;
                grid.DefaultCellStyle.BackColor = InputBack;
                grid.DefaultCellStyle.ForeColor = TextColor;
                grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(78, 111, 75);
                grid.DefaultCellStyle.SelectionForeColor = TextColor;
            }
            else if (control is PropertyGrid)
            {
                PropertyGrid propertyGrid = (PropertyGrid)control;
                propertyGrid.ViewBackColor = InputBack;
                propertyGrid.ViewForeColor = TextColor;
                propertyGrid.HelpBackColor = SurfaceBack;
                propertyGrid.HelpForeColor = TextColor;
                propertyGrid.CategoryForeColor = TextColor;
                propertyGrid.CategorySplitterColor = Color.FromArgb(95, 103, 86);
                propertyGrid.CommandsBackColor = SurfaceBack;
                propertyGrid.CommandsForeColor = TextColor;
                propertyGrid.LineColor = Color.FromArgb(71, 77, 67);
                propertyGrid.DisabledItemForeColor = MutedTextColor;
            }
            else if (control is Label && !(control is LinkLabel))
            {
                Label label = (Label)control;
                label.Font = GetReadableFont(label, 8f, label.Font.Style == FontStyle.Bold ? FontStyle.Bold : FontStyle.Regular);
                if (String.Equals(Convert.ToString(label.Tag), "LandingText", StringComparison.Ordinal))
                {
                    // Landing copy uses native solid text so descriptions remain
                    // attached to their editor cards and never disappear behind
                    // the shared owner-drawn label pass.
                    if (label.ForeColor == Color.Transparent) label.ForeColor = TextColor;
                }
                else
                {
                    label.ForeColor = Color.Transparent;
                    CaptureLabelText(label);
                    HookLabel(label);
                }
            }
            else if (control is ToolStrip)
            {
                ApplyToolStrip((ToolStrip)control);
            }
            else if (control is TabControl)
            {
                TabControl tabs = (TabControl)control;
                tabs.BackColor = PanelBack;
                tabs.ForeColor = TextColor;
                HookTabControl(tabs);
            }
            else if (control is TabPage || control is GroupBox ||
                     control is Panel || control is SplitContainer)
            {
                if (!IsTransparentSurface(control))
                    control.BackColor = PanelBack;
                control.ForeColor = TextColor;
                Panel panel = control as Panel;
                if (panel != null && !String.Equals(Convert.ToString(panel.Tag), "FramedSurface", StringComparison.Ordinal))
                    panel.BorderStyle = BorderStyle.None;
                HookFrame(control);
            }
            else
            {
                control.ForeColor = TextColor;
            }

            foreach (Control child in control.Controls)
                ApplyControl(child);

            if (control.ContextMenuStrip != null)
                ApplyToolStrip(control.ContextMenuStrip);
        }

        private static void ApplyToolStrip(ToolStrip strip)
        {
            if (strip == null) return;
            strip.BackColor = SurfaceBack;
            strip.ForeColor = TextColor;
            strip.Renderer = new ArcharinaToolStripRenderer();
            strip.Font = GetReadableFont(strip, 8f, FontStyle.Regular);
            foreach (ToolStripItem item in strip.Items)
                ApplyToolStripItem(item, strip.Font);
        }

        private static void ApplyToolStripItem(ToolStripItem item, Font font)
        {
            if (item == null) return;
            item.BackColor = SurfaceBack;
            item.ForeColor = TextColor;
            item.Font = font;
            ToolStripMenuItem menuItem = item as ToolStripMenuItem;
            if (menuItem == null) return;
            foreach (ToolStripItem child in menuItem.DropDownItems)
                ApplyToolStripItem(child, font);
        }

        private static bool IsTransparentSurface(Control control)
        {
            if (control == null) return false;
            string tag = Convert.ToString(control.Tag);
            return String.Equals(tag, "LandingTransparent", StringComparison.Ordinal) ||
                String.Equals(tag, "ShelfActionItem", StringComparison.Ordinal) ||
                String.Equals(tag, "NoFrame", StringComparison.Ordinal);
        }

        private static void HookButton(Button button)
        {
            if (HookedControls.ContainsKey(button)) return;
            HookedControls[button] = true;
            button.Resize += delegate { button.Region = CreateRoundedRegion(button.ClientRectangle, 6); button.Invalidate(); };
            button.MouseEnter += delegate { button.Invalidate(); };
            button.MouseLeave += delegate { button.Invalidate(); };
            button.MouseDown += delegate { button.Invalidate(); };
            button.MouseUp += delegate { button.Invalidate(); };
            button.Paint += PaintStoneButton;
            button.Region = CreateRoundedRegion(button.ClientRectangle, 6);
        }

        private static void HookComboBox(ComboBox combo)
        {
            if (HookedControls.ContainsKey(combo)) return;
            HookedControls[combo] = true;
            combo.DrawItem += PaintComboBoxItem;
        }

        private static void PaintComboBoxItem(object sender, DrawItemEventArgs e)
        {
            ComboBox combo = (ComboBox)sender;
            if (e.Index < 0 && String.IsNullOrEmpty(combo.Text)) return;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color background = selected ? Color.FromArgb(58, 83, 67) : InputBack;
            using (SolidBrush fill = new SolidBrush(background))
                e.Graphics.FillRectangle(fill, e.Bounds);
            string text = e.Index >= 0 ? combo.GetItemText(combo.Items[e.Index]) : combo.Text;
            Rectangle textBounds = e.Bounds;
            textBounds.Inflate(-6, 0);
            TextRenderer.DrawText(e.Graphics, text, combo.Font, textBounds, TextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            // Owner-draw the closed-field glyph in white so it remains visible
            // on the dark surface instead of inheriting a black native arrow.
            if (e.Bounds.Height >= combo.Height - 2 && combo.DropDownStyle == ComboBoxStyle.DropDownList)
            {
                int centerX = e.Bounds.Right - 10;
                int centerY = e.Bounds.Top + (e.Bounds.Height / 2);
                using (SolidBrush arrow = new SolidBrush(Color.White))
                {
                    e.Graphics.FillPolygon(arrow, new Point[]
                    {
                        new Point(centerX - 4, centerY - 2),
                        new Point(centerX + 4, centerY - 2),
                        new Point(centerX, centerY + 3)
                    });
                }
            }
            using (Pen edge = new Pen(selected ? GoldAccent : StoneEdge, 1f))
                e.Graphics.DrawRectangle(edge, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
        }

        private static void HookLabel(Label label)
        {
            if (HookedControls.ContainsKey(label)) return;
            HookedControls[label] = true;
            label.Paint += PaintOutlinedLabel;
        }

        private static void HookFrame(Control control)
        {
            if (String.Equals(Convert.ToString(control.Tag), "ShelfActionItem", StringComparison.Ordinal) ||
                String.Equals(Convert.ToString(control.Tag), "NoFrame", StringComparison.Ordinal))
                return;
            if (control is TabPage || control is GroupBox ||
                String.Equals(Convert.ToString(control.Tag), "FramedSurface", StringComparison.Ordinal))
            {
                if (HookedControls.ContainsKey(control)) return;
                HookedControls[control] = true;
                control.Paint += PaintStoneFrame;
            }
        }

        private static void HookTabControl(TabControl tabs)
        {
            if (HookedControls.ContainsKey(tabs)) return;
            HookedControls[tabs] = true;
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.DrawItem += PaintTabControlItem;
            tabs.Invalidate();
        }

        private static void PaintTabControlItem(object sender, DrawItemEventArgs e)
        {
            TabControl tabs = (TabControl)sender;
            if (e.Index < 0 || e.Index >= tabs.TabPages.Count) return;
            TabPage page = tabs.TabPages[e.Index];
            bool active = e.Index == tabs.SelectedIndex;
            Color background = active ? Color.FromArgb(72, 87, 65) : SurfaceBack;
            using (SolidBrush fill = new SolidBrush(background))
                e.Graphics.FillRectangle(fill, e.Bounds);
            using (Pen edge = new Pen(active ? GoldAccent : StoneEdge, 1f))
                e.Graphics.DrawRectangle(edge, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            if (tabs.Alignment == TabAlignment.Left || tabs.Alignment == TabAlignment.Right)
            {
                GraphicsState state = e.Graphics.Save();
                bool left = tabs.Alignment == TabAlignment.Left;
                e.Graphics.TranslateTransform(left ? e.Bounds.Left : e.Bounds.Right,
                    left ? e.Bounds.Bottom : e.Bounds.Top);
                e.Graphics.RotateTransform(left ? -90f : 90f);
                Rectangle verticalBounds = new Rectangle(0, 0, e.Bounds.Height, e.Bounds.Width);
                verticalBounds.Inflate(-6, 0);
                TextRenderer.DrawText(e.Graphics, page.Text, tabs.Font, verticalBounds, TextColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                e.Graphics.Restore(state);
            }
            else
            {
                Rectangle textBounds = e.Bounds;
                textBounds.Inflate(-6, 0);
                TextRenderer.DrawText(e.Graphics, page.Text, tabs.Font, textBounds, TextColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private static void PaintFormBackground(object sender, PaintEventArgs e)
        {
            using (TextureBrush brush = new TextureBrush(GetStoneTexture(), WrapMode.Tile))
            {
                e.Graphics.FillRectangle(brush, ((Control)sender).ClientRectangle);
            }
            using (SolidBrush shade = new SolidBrush(Color.FromArgb(215, FormBack)))
                e.Graphics.FillRectangle(shade, ((Control)sender).ClientRectangle);
        }

        private static void PaintStoneFrame(object sender, PaintEventArgs e)
        {
            Control control = (Control)sender;
            Rectangle bounds = new Rectangle(0, 0, control.ClientSize.Width - 1, control.ClientSize.Height - 1);
            if (bounds.Width < 2 || bounds.Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath edgePath = CreateRoundedPath(bounds, 4))
            using (Pen edge = new Pen(Color.FromArgb(92, 166, 158, 116), 1f))
                e.Graphics.DrawPath(edge, edgePath);
        }

        private static void PaintStoneButton(object sender, PaintEventArgs e)
        {
            Button button = (Button)sender;
            Rectangle bounds = new Rectangle(0, 0, button.ClientSize.Width - 1, button.ClientSize.Height - 1);
            if (bounds.Width < 2 || bounds.Height < 2) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (GraphicsPath path = CreateRoundedPath(bounds, 6))
            {
                Color baseColor;
                if (!ButtonBackColors.TryGetValue(button, out baseColor) || baseColor == Color.Transparent)
                    baseColor = SurfaceBack;
                using (SolidBrush fill = new SolidBrush(baseColor))
                    e.Graphics.FillPath(fill, path);

                bool hovered = button.ClientRectangle.Contains(button.PointToClient(Cursor.Position));
                bool active = button.Tag is bool && (bool)button.Tag;
                Color overlayColor = !button.Enabled ? Color.FromArgb(135, 14, 16, 15) :
                    active ? Color.FromArgb(76, 92, 145, 86) :
                    button.Focused ? Color.FromArgb(36, 235, 205, 102) :
                    hovered ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0);
                using (SolidBrush overlay = new SolidBrush(overlayColor))
                    e.Graphics.FillPath(overlay, path);
                using (Pen shadow = new Pen(StoneShadow, 2f))
                    e.Graphics.DrawPath(shadow, path);
                bounds.Inflate(-1, -1);
                using (Pen edge = new Pen(active || button.Focused ? GoldAccent : StoneEdge, 1f))
                    e.Graphics.DrawPath(edge, CreateRoundedPath(bounds, 5));
            }
            string buttonText;
            if (!ButtonTexts.TryGetValue(button, out buttonText)) buttonText = button.Text;
            bool iconOnly = String.Equals(button.AccessibleDescription, "icon-only", StringComparison.Ordinal) ||
                String.Equals(button.AccessibleDescription, "icon-label", StringComparison.Ordinal);
            if (iconOnly) buttonText = String.Empty;
            if (button.Image != null && String.IsNullOrEmpty(buttonText))
            {
                // Icon sources are authored at different resolutions. Keep the
                // visual footprint stable so one toolbar button cannot dominate
                // its neighbors just because its PNG has larger dimensions.
                bool showIconLabel = String.Equals(button.AccessibleDescription, "icon-label", StringComparison.Ordinal);
                int iconSize = showIconLabel
                    ? Math.Min(22, Math.Max(16, Math.Min(button.ClientSize.Width, button.ClientSize.Height - 18)))
                    : Math.Min(24, Math.Max(16, Math.Min(button.ClientSize.Width, button.ClientSize.Height) - 8));
                Rectangle iconBounds = new Rectangle(
                    (button.ClientSize.Width - iconSize) / 2,
                    showIconLabel ? 3 : (button.ClientSize.Height - iconSize) / 2,
                    iconSize,
                    iconSize);
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                e.Graphics.DrawImage(button.Image, iconBounds);
                if (showIconLabel)
                {
                    string caption = GetIconCaption(button.AccessibleName);
                    Rectangle captionBounds = new Rectangle(2, iconBounds.Bottom + 1, button.ClientSize.Width - 4,
                        Math.Max(10, button.ClientSize.Height - iconBounds.Bottom - 2));
                    DrawOutlinedText(e.Graphics, caption, GetUiFont(7f, FontStyle.Regular), captionBounds,
                        button.Enabled ? TextColor : MutedTextColor, ContentAlignment.MiddleCenter);
                }
            }
            DrawOutlinedText(e.Graphics, buttonText, button.Font, button.ClientRectangle, button.Enabled ? TextColor : MutedTextColor, button.TextAlign);
        }

        private static void PaintOutlinedLabel(object sender, PaintEventArgs e)
        {
            Label label = (Label)sender;
            string labelText;
            if (!LabelTexts.TryGetValue(label, out labelText)) labelText = label.Text;
            if (String.IsNullOrEmpty(labelText)) return;
            DrawOutlinedText(e.Graphics, labelText, label.Font, label.ClientRectangle, label.Enabled ? TextColor : MutedTextColor, label.TextAlign, true);
        }

        private static void CaptureButtonText(Button button)
        {
            if (ButtonTexts.ContainsKey(button)) return;
            ButtonTexts[button] = button.Text;
            button.TextChanged += delegate
            {
                if (UpdatingText.ContainsKey(button)) return;
                ButtonTexts[button] = button.Text;
                UpdatingText[button] = true;
                button.Text = String.Empty;
                UpdatingText.Remove(button);
                button.Invalidate();
            };
            UpdatingText[button] = true;
            button.Text = String.Empty;
            UpdatingText.Remove(button);
        }

        private static void CaptureLabelText(Label label)
        {
            if (LabelTexts.ContainsKey(label)) return;
            LabelTexts[label] = label.Text;
            label.TextChanged += delegate
            {
                if (UpdatingText.ContainsKey(label)) return;
                LabelTexts[label] = label.Text;
                UpdatingText[label] = true;
                label.Text = String.Empty;
                UpdatingText.Remove(label);
                label.Invalidate();
            };
            UpdatingText[label] = true;
            label.Text = String.Empty;
            UpdatingText.Remove(label);
        }

        private static string GetIconCaption(string accessibleName)
        {
            string value = accessibleName ?? String.Empty;
            string lower = value.ToLowerInvariant();
            if (lower.Contains("object mode")) return "Object";
            if (lower.Contains("edit mode")) return "Edit";
            if (lower.Contains("vertex paint")) return "Paint";
            if (lower.Contains("uv editing")) return "UV";
            if (lower.Contains("move gizmo")) return "Move";
            if (lower.Contains("rotate gizmo")) return "Rotate";
            if (lower.Contains("scale gizmo")) return "Scale";
            if (lower.Contains("perspective")) return "Perspective";
            if (lower.Contains("wireframe")) return "Wireframe";
            if (lower.Contains("unlit")) return "Unlit";
            if (lower.Contains("texture")) return "Textures";
            if (lower.Contains("overlay")) return "Overlays";
            if (lower.Contains("undo")) return "Undo";
            if (lower.Contains("redo")) return "Redo";
            if (lower.Contains("save")) return "Save";
            if (lower.Contains("view")) return "View";
            if (lower.Contains("workspace")) return "Workspace";
            if (lower.Contains("vertex")) return "Vertex";
            if (lower.Contains("edge")) return "Edge";
            if (lower.Contains("face")) return "Face";
            if (lower == "content") return "Content";
            if (lower == "details") return "Details";
            if (lower == "refresh") return "Refresh";
            if (lower.Contains("apply geometry")) return "Apply";
            if (lower == "focus") return "Focus";
            if (lower.Contains("classic editors")) return "Classic";
            if (lower == "paths") return "Paths";
            return value;
        }

        private static void DrawOutlinedText(Graphics graphics, string text, Font font, Rectangle bounds, Color color, ContentAlignment alignment)
        {
            DrawOutlinedText(graphics, text, font, bounds, color, alignment, false);
        }

        private static void DrawOutlinedText(Graphics graphics, string text, Font font, Rectangle bounds, Color color, ContentAlignment alignment, bool wrap)
        {
            if (String.IsNullOrEmpty(text)) return;
            StringFormat format = new StringFormat(StringFormat.GenericDefault)
            {
                Alignment = GetHorizontalAlignment(alignment),
                LineAlignment = GetVerticalAlignment(alignment),
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = wrap ? StringFormatFlags.LineLimit : StringFormatFlags.NoWrap
            };
            using (GraphicsPath path = new GraphicsPath())
            {
                float emSize = graphics.DpiY * font.Size / 72f;
                path.AddString(text, font.FontFamily, (int)font.Style, emSize, bounds, format);
                // Dense inspector labels and legacy controls become visually
                // doubled when a heavy outline is applied at small sizes. The
                // stone surfaces already provide contrast, so use one clean
                // solid glyph layer throughout the editor chrome.
                using (SolidBrush fill = new SolidBrush(color))
                    graphics.FillPath(fill, path);
            }
            format.Dispose();
        }

        private static StringAlignment GetHorizontalAlignment(ContentAlignment alignment)
        {
            if (alignment == ContentAlignment.TopCenter || alignment == ContentAlignment.MiddleCenter || alignment == ContentAlignment.BottomCenter) return StringAlignment.Center;
            if (alignment == ContentAlignment.TopRight || alignment == ContentAlignment.MiddleRight || alignment == ContentAlignment.BottomRight) return StringAlignment.Far;
            return StringAlignment.Near;
        }

        private static StringAlignment GetVerticalAlignment(ContentAlignment alignment)
        {
            if (alignment == ContentAlignment.MiddleLeft || alignment == ContentAlignment.MiddleCenter || alignment == ContentAlignment.MiddleRight) return StringAlignment.Center;
            if (alignment == ContentAlignment.BottomLeft || alignment == ContentAlignment.BottomCenter || alignment == ContentAlignment.BottomRight) return StringAlignment.Far;
            return StringAlignment.Near;
        }

        private static Region CreateRoundedRegion(Rectangle bounds, int radius)
        {
            if (bounds.Width < 2 || bounds.Height < 2) return null;
            return new Region(CreateRoundedPath(new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1), radius));
        }

        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(2, radius * 2);
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Bitmap GetStoneTexture()
        {
            if (stoneTexture != null) return stoneTexture;
            stoneTexture = new Bitmap(96, 96, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Random random = new Random(0xA64164);
            using (Graphics graphics = Graphics.FromImage(stoneTexture))
                graphics.Clear(Color.FromArgb(72, 75, 65));
            using (Graphics graphics = Graphics.FromImage(stoneTexture))
            {
                graphics.SmoothingMode = SmoothingMode.None;
                int rowHeight = 16;
                for (int row = 0; row < 6; row++)
                {
                    int offset = (row % 2) * -14;
                    for (int x = offset; x < 112; x += 28)
                    {
                        int width = 26 + random.Next(-2, 3);
                        int y = row * rowHeight;
                        int shade = 62 + random.Next(-8, 12);
                        using (SolidBrush fill = new SolidBrush(Color.FromArgb(255, shade + 7, shade + 8, shade - 2)))
                            graphics.FillRectangle(fill, x + 1, y + 1, width - 2, rowHeight - 2);
                        using (Pen shadow = new Pen(Color.FromArgb(170, 22, 25, 22), 2f))
                            graphics.DrawLine(shadow, x, y + rowHeight - 1, x + width, y + rowHeight - 1);
                        using (Pen highlight = new Pen(Color.FromArgb(100, 153, 147, 104), 1f))
                            graphics.DrawLine(highlight, x + 2, y + 1, x + width - 2, y + 1);
                    }
                }
                for (int i = 0; i < 80; i++)
                {
                    int x = random.Next(0, 96);
                    int y = random.Next(0, 96);
                    using (SolidBrush moss = new SolidBrush(Color.FromArgb(random.Next(18, 46), 74, 102, 58)))
                        graphics.FillRectangle(moss, x, y, random.Next(1, 4), random.Next(1, 3));
                }
            }
            return stoneTexture;
        }

        private sealed class ArcharinaToolStripRenderer : ToolStripProfessionalRenderer
        {
            public ArcharinaToolStripRenderer() : base(new ArcharinaColorTable()) { }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                // Menus are intentionally flatter than editor surfaces: keep
                // popup panels legible and free of the textured chrome.
                using (SolidBrush shade = new SolidBrush(SurfaceBack))
                    e.Graphics.FillRectangle(shade, e.AffectedBounds);
            }

            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected && !e.Item.Pressed) return;
                Rectangle bounds = new Rectangle(1, 1, Math.Max(2, e.Item.Width - 3), Math.Max(2, e.Item.Height - 3));
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = CreateRoundedPath(bounds, 4))
                using (SolidBrush fill = new SolidBrush(e.Item.Pressed ? Color.FromArgb(130, StoneShadow) : Color.FromArgb(80, GoldAccent)))
                    e.Graphics.FillPath(fill, path);
            }
        }

        private sealed class ArcharinaColorTable : ProfessionalColorTable
        {
            public override Color MenuStripGradientBegin { get { return SurfaceBack; } }
            public override Color MenuStripGradientEnd { get { return SurfaceBack; } }
            public override Color ToolStripDropDownBackground { get { return SurfaceBack; } }
            public override Color ImageMarginGradientBegin { get { return SurfaceBack; } }
            public override Color ImageMarginGradientMiddle { get { return SurfaceBack; } }
            public override Color ImageMarginGradientEnd { get { return SurfaceBack; } }
            public override Color MenuItemSelected { get { return Color.FromArgb(75, 101, 70); } }
            public override Color MenuItemBorder { get { return StoneEdge; } }
            public override Color SeparatorDark { get { return Color.FromArgb(85, 90, 75); } }
            public override Color SeparatorLight { get { return Color.FromArgb(35, 39, 34); } }
        }
    }
}
