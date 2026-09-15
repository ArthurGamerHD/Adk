using System;
using System.Collections.Generic;
using System.Globalization;
using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Layout;
using Adk.Gui.Markup;
using VRage.Utils;
using VRageMath;

namespace Adk.Gui.Styling
{
    public sealed class ControlTemplateInstance
    {
        internal ControlTemplateInstance(
            VisualElement root,
            IDictionary<string, Control> names)
        {
            Root = root;
            Names = names;
        }

        public VisualElement Root { get; }
        public IDictionary<string, Control> Names { get; }
    }

    /// <summary>An immutable factory for one control's style-owned visual tree.</summary>
    public sealed class ControlTemplate
    {
        static readonly TypeRegistry TemplateTypes = CreateTypeRegistry();
        readonly UiXmlLoader.Node _root;
        readonly Func<TemplatedControl, VisualElement> _factory;

        public ControlTemplate(Func<TemplatedControl, VisualElement> factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            _factory = factory;
        }

        internal ControlTemplate(UiXmlLoader.Node root)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            _root = root;
        }

        public static ControlTemplate Parse(string xml)
        {
            UiXmlLoader.Node wrapper = UiXmlLoader.ParseNode(xml);
            if (!string.Equals(wrapper.Name, "ControlTemplate", StringComparison.Ordinal))
                throw new FormatException("Expected ControlTemplate.");
            if (wrapper.Attributes.Count != 0)
                throw new FormatException("ControlTemplate does not accept attributes.");
            if (wrapper.Children.Count != 1)
                throw new FormatException("ControlTemplate requires exactly one visual root.");
            return new ControlTemplate(wrapper.Children[0]);
        }

        internal ControlTemplateInstance BuildInstance(TemplatedControl owner)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            var names = new Dictionary<string, Control>(StringComparer.Ordinal);
            VisualElement root;
            if (_factory != null)
            {
                root = _factory(owner);
                RegisterTree(root, owner, names);
            }
            else
            {
                root = new UiXmlLoader(TemplateTypes).Build(
                    _root, owner, names) as VisualElement;
            }
            if (root == null)
                throw new InvalidOperationException(
                    "A ControlTemplate root must be a VisualElement.");
            return new ControlTemplateInstance(root, names);
        }

        static void RegisterTree(
            VisualElement element,
            TemplatedControl owner,
            IDictionary<string, Control> names)
        {
            if (element == null)
                return;
            element.TemplatedParent = owner;
            if (!string.IsNullOrWhiteSpace(element.Name))
            {
                if (names.ContainsKey(element.Name))
                    throw new InvalidOperationException(
                        "Duplicate template name '" + element.Name + "'.");
                names.Add(element.Name, element);
            }
            IReadOnlyList<VisualElement> children = element.LogicalChildren;
            for (int i = 0; i < children.Count; i++)
                RegisterTree(children[i], owner, names);
        }

        static TypeRegistry CreateTypeRegistry()
        {
            var registry = new TypeRegistry();

            registry.Register<Grid>("Grid", () => new Grid());
            registry.RegisterProperty<Grid, string>("Grid", "Columns",
                (grid, value) => grid.SetColumns(ParseSegments(value)));
            registry.RegisterProperty<Grid, string>("Grid", "Rows",
                (grid, value) => grid.SetRows(ParseSegments(value)));

            registry.Register<Border>("Border", () => new Border());
            registry.RegisterProperty<Border, BackgroundBrush>("Border", "Background",
                (border, value) => border.Background = value);
            registry.RegisterProperty<Border, Color>("Border", "Border",
                (border, value) => border.BorderColor = value);
            registry.RegisterProperty<Border, int>("Border", "BorderThickness",
                (border, value) => border.BorderThickness = value);
            registry.RegisterProperty<Border, Vector4>("Border", "Padding",
                (border, value) => border.Padding = value);
            registry.RegisterProperty<Border, float>("Border", "CornerRadius",
                (border, value) => border.CornerRadius = value);
            registry.RegisterProperty<Border, bool>("Border", "UseGeneratedTexture",
                (border, value) => border.UseGeneratedTexture = value);
            registry.RegisterProperty<Border, bool>("Border", "Interactive",
                (border, value) => border.Interactive = value);
            registry.RegisterProperty<Border, Color>("Border", "Shadow",
                (border, value) => border.ShadowColor = value);
            registry.RegisterProperty<Border, Vector2>("Border", "ShadowOffset",
                (border, value) => border.ShadowOffset = value);

            registry.Register<TextBlock>("TextBlock", () => new TextBlock());
            registry.RegisterProperty<TextBlock, string>("TextBlock", "Text",
                (text, value) => text.Text = value);
            registry.RegisterProperty<TextBlock, float>("TextBlock", "Scale",
                (text, value) => text.Scale = value);
            registry.RegisterProperty<TextBlock, Color>("TextBlock", "Color",
                (text, value) => text.Color = value);
            registry.RegisterProperty<TextBlock, TextAlignment>("TextBlock", "TextAlignment",
                (text, value) => text.TextAlignment = value);
            registry.RegisterProperty<TextBlock, MyGuiDrawAlignEnum>("TextBlock", "Alignment",
                (text, value) => text.Alignment = value);

            registry.Register<TextPresenter>("TextPresenter", () => new TextPresenter());

            registry.Register<TriangleIcon>("TriangleIcon", () => new TriangleIcon());
            registry.RegisterProperty<TriangleIcon, TriangleDirection>(
                "TriangleIcon", "Direction", (icon, value) => icon.Direction = value);
            registry.RegisterProperty<TriangleIcon, Color>(
                "TriangleIcon", "Color", (icon, value) => icon.Color = value);
            registry.RegisterProperty<TriangleIcon, float>(
                "TriangleIcon", "MaxSize", (icon, value) => icon.MaxSize = value);
            registry.RegisterProperty<TriangleIcon, int>(
                "TriangleIcon", "Steps", (icon, value) => icon.Steps = value);

            registry.Register<ScrollViewer>("ScrollViewer", () => new ScrollViewer());
            registry.RegisterProperty<ScrollViewer, bool>(
                "ScrollViewer", "HorizontalScrollEnabled",
                (viewer, value) => viewer.HorizontalScrollEnabled = value);
            registry.RegisterProperty<ScrollViewer, bool>(
                "ScrollViewer", "VerticalScrollEnabled",
                (viewer, value) => viewer.VerticalScrollEnabled = value);
            registry.RegisterProperty<ScrollViewer, bool>(
                "ScrollViewer", "InputEnabled",
                (viewer, value) => viewer.InputEnabled = value);

            registry.Register<Button.ButtonPresenter>(
                "ButtonPresenter", () => new Button.ButtonPresenter());
            registry.Register<ScrollViewer.ScrollViewerPresenter>(
                "ScrollViewerPresenter", () => new ScrollViewer.ScrollViewerPresenter());
            return registry;
        }

        static float[] ParseSegments(string value)
        {
            string[] parts = (value ?? string.Empty).Split(
                new[] { ',', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return new[] { 1f };
            var segments = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                segments[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
            return segments;
        }
    }

    /// <summary>Strongly typed code-template compatibility wrapper.</summary>
    public sealed class ControlTemplate<TControl> where TControl : TemplatedControl
    {
        readonly Func<TControl, VisualElement> _factory;

        public ControlTemplate(Func<TControl, VisualElement> factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            _factory = factory;
        }

        public VisualElement Build(TControl owner)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            return _factory(owner);
        }

        public ControlTemplate ToUntyped()
        {
            return new ControlTemplate(owner => _factory((TControl)owner));
        }
    }
}
