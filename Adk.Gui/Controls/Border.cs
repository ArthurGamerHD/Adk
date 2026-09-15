using System;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Layout;
using Adk.Gui.Rendering;
using Adk.Gui.Styling;
using VRageMath;

namespace Adk.Gui.Controls
{
    /// <summary>
    /// A single-child decorator with optional rounded background/border styling and
    /// pointer-state visuals. Standard generated textures are shared and reference
    /// counted; square borders bypass generated textures entirely.
    /// </summary>
    public sealed class Border : Decorator
    {
        static Border() { }

        static int _nextTextureId;

        readonly int _textureId = ++_nextTextureId;
        BorderTextureCache.Lease _sharedTexture;
        BorderTextureCache _sharedTextureOwner;
        IDynamicTexture _ownedTexture;
        IDynamicTextureFactory _ownedTextureFactory;
        float _cornerRadius;
        bool _useGeneratedTexture;
        Action<PixelCanvas> _customTexturePainter;
        string _generatedTextureName;
        bool _textureDirty = true;

        public static readonly StyledProperty<BackgroundBrush> BackgroundProperty =
            StyledProperty.Register<Border, BackgroundBrush>(
                "Background", new SolidColorBrush(Color.Transparent),
                UiInvalidation.Render);
        public static readonly StyledProperty<Color> BorderProperty =
            StyledProperty.Register<Border, Color>(
                "Border", Color.Transparent, UiInvalidation.Render);
        public static readonly StyledProperty<int> BorderThicknessProperty =
            StyledProperty.Register<Border, int>(
                nameof(BorderThickness), 0, UiInvalidation.Measure);

        public BackgroundBrush Background
        {
            get { return GetValue(BackgroundProperty); }
            set { SetValue(BackgroundProperty, value); }
        }
        public Color BackgroundColor
        {
            get
            {
                BackgroundBrush brush = GetValue(BackgroundProperty);
                return brush == null ? Color.Transparent : brush.FallbackColor;
            }
            set { SetValue(BackgroundProperty, new SolidColorBrush(value)); }
        }
        public Func<Color> BackgroundColorProvider { get; set; }

        public Color BorderColor
        {
            get { return GetValue(BorderProperty); }
            set { SetValue(BorderProperty, value); }
        }
        public Func<Color> BorderColorProvider { get; set; }
        public int BorderThickness
        {
            get { return GetValue(BorderThicknessProperty); }
            set { SetValue(BorderThicknessProperty, Math.Max(0, value)); }
        }

        public Color ShadowColor { get; set; } = Color.Transparent;
        public Vector2 ShadowOffset { get; set; }

        public bool Interactive { get; set; }
        public Action Clicked { get; set; }
        public Action SecondaryClicked { get; set; }

        public float CornerRadius
        {
            get { return _cornerRadius; }
            set
            {
                float next = Math.Max(0f, value);
                if (Math.Abs(_cornerRadius - next) < 0.001f)
                    return;
                _cornerRadius = next;
                ReleaseSharedTexture();
                _textureDirty = true;
            }
        }

        public bool UseGeneratedTexture
        {
            get { return _useGeneratedTexture; }
            set
            {
                if (_useGeneratedTexture == value)
                    return;
                _useGeneratedTexture = value;
                _textureDirty = true;
            }
        }

        public Action<PixelCanvas> CustomTexturePainter
        {
            get { return _customTexturePainter; }
            set
            {
                if (ReferenceEquals(_customTexturePainter, value))
                    return;
                bool ownershipModeChanged = (_customTexturePainter == null) != (value == null);
                _customTexturePainter = value;
                if (ownershipModeChanged)
                    DisposeTexture();
                _textureDirty = true;
            }
        }

        public string GeneratedTextureName
        {
            get { return _generatedTextureName; }
            set
            {
                if (string.Equals(_generatedTextureName, value, StringComparison.Ordinal))
                    return;
                _generatedTextureName = value;
                DisposeOwnedTexture();
                _textureDirty = true;
            }
        }


        protected override void UpdatePseudoClasses()
        {
            base.UpdatePseudoClasses();
            PseudoClasses.Set(PseudoClassNames.Pressed, Interactive && IsPressed);
        }

        protected override Vector4 GetLayoutPadding()
        {
            float thickness = Math.Max(0, BorderThickness);
            return new Vector4(
                Padding.X + thickness,
                Padding.Y + thickness,
                Padding.Z + thickness,
                Padding.W + thickness);
        }

        protected override bool HitTestSelf(Vector2 point)
        {
            return Interactive;
        }

        protected override void DrawSelf(RenderContext context)
        {
            BackgroundBrush background = ResolveBackground();
            Color border = ResolveBorder();

            if (background != null && !(background is SolidColorBrush))
            {
                ReleaseSharedTexture();
                DisposeOwnedTexture();
                DrawBrush(context, background, border);
                return;
            }

            Color backgroundColor = background == null ? Color.Transparent : background.FallbackColor;

            if (CustomTexturePainter != null)
            {
                EnsureCustomTexture(context);
                DrawCustom(context, backgroundColor, border);
                return;
            }

            // A square Border is just rectangles. Do not consume a generated-texture
            if (CornerRadius <= 0f)
            {
                ReleaseSharedTexture();
                DrawSquare(context, backgroundColor, border);
                return;
            }

            EnsureStandardTexture(context, backgroundColor, border);
            DrawRounded(context, backgroundColor, border);
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            if (clicked && Clicked != null)
                Clicked();
        }

        internal override void SecondaryPointerReleased(Vector2 point, bool clicked)
        {
            if (clicked && SecondaryClicked != null)
                SecondaryClicked();
        }

        BackgroundBrush ResolveBackground()
        {
            return BackgroundColorProvider == null
                ? Background
                : new SolidColorBrush(BackgroundColorProvider());
        }

        Color ResolveBorder()
        {
            return BorderColorProvider?.Invoke() ?? BorderColor;
        }

        void DrawBrush(
            RenderContext context,
            BackgroundBrush background,
            Color border)
        {
            if (ShadowColor.A > 0)
            {
                context.Fill(
                    new RectangleF(Bounds.Position + ShadowOffset, Bounds.Size),
                    ShadowColor);
            }
            context.Fill(Bounds, background);
            if (BorderThickness > 0 && border.A > 0)
                context.Border(Bounds, border, BorderThickness);
        }

        protected override void OnPropertyChanged(IStyledProperty property)
        {
            base.OnPropertyChanged(property);
            if (ReferenceEquals(property, BackgroundProperty) ||
                ReferenceEquals(property, BorderProperty) ||
                ReferenceEquals(property, BorderThicknessProperty))
                _textureDirty = true;
        }

        void DrawSquare(RenderContext context, Color background, Color border)
        {
            if (ShadowColor.A > 0)
                context.Fill(new RectangleF(Bounds.Position + ShadowOffset, Bounds.Size), ShadowColor);

            if (background.A > 0)
                context.Fill(Bounds, background);
            if (BorderThickness > 0 && border.A > 0 && border != background)
                context.Border(Bounds, border, BorderThickness);
        }

        void DrawRounded(RenderContext context, Color background, Color border)
        {
            IDynamicTexture texture = ActiveTexture;
            if (texture == null || !texture.IsAvailable)
            {
                DrawSquare(context, background, border);
                return;
            }

            bool tintable = UsesTintableMask(background, border);
            if (ShadowColor.A > 0)
            {
                context.DrawTexture(
                    texture.Source,
                    new RectangleF(Bounds.Position + ShadowOffset, Bounds.Size),
                    ShadowColor);
            }

            if (tintable)
            {
                Color tint = BorderThickness <= 0 ? background : border;
                if (tint.A > 0)
                    context.DrawTexture(texture.Source, Bounds, tint);
            }
            else
            {
                context.DrawTexture(texture.Source, Bounds, Color.White);
            }
        }

        void DrawCustom(RenderContext context, Color background, Color border)
        {
            IDynamicTexture texture = ActiveTexture;
            if (texture == null || !texture.IsAvailable)
            {
                DrawSquare(context, background, border);
                return;
            }

            if (ShadowColor.A > 0)
            {
                context.DrawTexture(
                    texture.Source,
                    new RectangleF(Bounds.Position + ShadowOffset, Bounds.Size),
                    ShadowColor);
            }
            if (background.A > 0)
                context.DrawTexture(texture.Source, Bounds, background);
            if (BorderThickness > 0 && border.A > 0)
                context.Border(Bounds, border, BorderThickness);
        }

        IDynamicTexture ActiveTexture
        {
            get
            {
                if (_sharedTexture != null)
                    return _sharedTexture.Texture;
                return _ownedTexture;
            }
        }

        void EnsureStandardTexture(RenderContext context, Color background, Color border)
        {
            DisposeOwnedTexture();

            Vector2I supported = GetSupportedSize(context);
            if (supported.X <= 0 || supported.Y <= 0)
                return;

            float radius = Math.Min(CornerRadius, Math.Min(supported.X, supported.Y) * 0.5f);
            bool tintable = UsesTintableMask(background, border);
            BorderTextureCache.Key desired = tintable
                ? new BorderTextureCache.Key(supported, radius, 0, Color.White, Color.White, false)
                : new BorderTextureCache.Key(
                    supported,
                    radius,
                    BorderThickness,
                    border,
                    background,
                    true);

            if (_sharedTexture != null &&
                ReferenceEquals(_sharedTextureOwner, context.BorderTextures) &&
                _sharedTexture.CacheKey.Equals(desired))
                return;

            ReleaseSharedTexture();
            _sharedTextureOwner = context.BorderTextures;
            _sharedTexture = tintable
                ? context.BorderTextures.AcquireMask(supported, radius)
                : context.BorderTextures.AcquireStyled(
                    supported,
                    radius,
                    BorderThickness,
                    border,
                    background);
            _textureDirty = false;
        }

        bool UsesTintableMask(Color background, Color border)
        {
            return BorderThickness <= 0 || border == background;
        }

        void EnsureCustomTexture(RenderContext context)
        {
            ReleaseSharedTexture();

            Vector2I requested = new Vector2I(
                Math.Max(1, (int)Math.Round(Bounds.Width)),
                Math.Max(1, (int)Math.Round(Bounds.Height)));
            Vector2I supported = context.DynamicTextures.FitToSupportedSize(requested);
            if (supported.X <= 0 || supported.Y <= 0)
                return;

            if (_ownedTexture == null ||
                !ReferenceEquals(_ownedTextureFactory, context.DynamicTextures) ||
                _ownedTexture.Size != supported)
            {
                DisposeOwnedTexture();
                string prefix = string.IsNullOrWhiteSpace(GeneratedTextureName)
                    ? "ADKBorder"
                    : GeneratedTextureName;
                _ownedTexture = context.DynamicTextures.Create(prefix + "_" + _textureId, requested);
                _ownedTextureFactory = context.DynamicTextures;
                _textureDirty = true;
            }

            if (!_textureDirty || _ownedTexture == null || !_ownedTexture.IsAvailable)
                return;

            _ownedTexture.Update(CustomTexturePainter);
            _textureDirty = false;
        }

        Vector2I GetSupportedSize(RenderContext context)
        {
            Vector2I requested = new Vector2I(
                Math.Max(1, (int)Math.Round(Bounds.Width)),
                Math.Max(1, (int)Math.Round(Bounds.Height)));
            return context.DynamicTextures.FitToSupportedSize(requested);
        }

        void DisposeTexture()
        {
            ReleaseSharedTexture();
            DisposeOwnedTexture();
        }

        void ReleaseSharedTexture()
        {
            if (_sharedTexture != null)
                _sharedTexture.Dispose();
            _sharedTexture = null;
            _sharedTextureOwner = null;
        }

        void DisposeOwnedTexture()
        {
            if (_ownedTexture != null)
                _ownedTexture.Dispose();
            _ownedTexture = null;
            _ownedTextureFactory = null;
        }

        public override void Dispose()
        {
            DisposeTexture();
            base.Dispose();
        }
    }
}
