using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Adk.Gui.Core;
using VRageMath;

namespace Adk.Gui.Styling
{
    public sealed class Setter
    {
        public Setter(string property, object value)
        {
            if (string.IsNullOrWhiteSpace(property))
                throw new ArgumentException("A setter requires a property name.", nameof(property));
            Property = property.Trim();
            Value = value ?? string.Empty;
        }

        public string Property { get; }
        public object Value { get; }
    }

    public sealed class Style
    {
        readonly List<Setter> _setters = new List<Setter>();

        public Style(string selector)
        {
            if (string.IsNullOrWhiteSpace(selector))
                throw new ArgumentException("A style requires a selector.", nameof(selector));
            Selector = selector.Trim();
        }

        public string Selector { get; }
        public IList<Setter> Setters => _setters;
    }

    /// <summary>
    /// Generic runtime-parsed styles. Entries whose control metadata has not been
    /// registered remain cached and resolve when StyledProperty.Register publishes it.
    /// </summary>
    public sealed class StyleSheet
    {
        static readonly StyleSheet EmptySheet = new StyleSheet(
            StylePropertyRegistry.Global,
            new ResourceDictionary(),
            new List<Style>());
        static StyleSheet _application = EmptySheet;

        readonly StylePropertyRegistry _registry;
        readonly ResourceDictionary _resources;
        readonly List<StyleEntry> _styles;
        bool _attached;
        int _version;

        StyleSheet(
            StylePropertyRegistry registry,
            ResourceDictionary resources,
            List<Style> styles)
        {
            _registry = registry;
            _resources = resources;
            _styles = new List<StyleEntry>(styles.Count);
            foreach (var style in styles)
            {
                Selector selector = Selector.Parse(style.Selector);
                _styles.Add(new StyleEntry(style, selector));
            }
            ResolvePending(null);
        }

        public static StyleSheet Empty => EmptySheet;

        public static StyleSheet Application
        {
            get { return _application; }
            set
            {
                StyleSheet next = value ?? EmptySheet;
                if (ReferenceEquals(_application, next))
                    return;
                _application.Detach();
                _application = next;
                _application.Attach();
            }
        }

        internal int Version => _version;

        public int PendingStyleCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _styles.Count; i++)
                    if (!_styles[i].IsResolved)
                        count++;
                return count;
            }
        }

        public static StyleSheet Load(
            string xml,
            ResourceDictionary resources = null)
        {
            return Load(xml, StylePropertyRegistry.Global, resources);
        }

        public static StyleSheet Load(
            string xml,
            StylePropertyRegistry registry,
            ResourceDictionary resources = null)
        {
            if (string.IsNullOrWhiteSpace(xml))
                throw new ArgumentException("Style XML cannot be empty.", nameof(xml));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            return new StyleSheet(
                registry,
                resources ?? new ResourceDictionary(),
                new StyleParser(xml).Parse());
        }

        public static StyleSheet Load(
            IEnumerable<string> xmlDocuments,
            ResourceDictionary resources = null)
        {
            return Load(xmlDocuments, StylePropertyRegistry.Global, resources);
        }

        public static StyleSheet Load(
            IEnumerable<string> xmlDocuments,
            StylePropertyRegistry registry,
            ResourceDictionary resources = null)
        {
            if (xmlDocuments == null)
                throw new ArgumentNullException(nameof(xmlDocuments));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            var styles = new List<Style>();
            foreach (string xml in xmlDocuments)
            {
                if (string.IsNullOrWhiteSpace(xml))
                    continue;
                styles.AddRange(new StyleParser(xml).Parse());
            }
            if (styles.Count == 0)
                throw new ArgumentException(
                    "At least one style XML document is required.", nameof(xmlDocuments));
            return new StyleSheet(
                registry,
                resources ?? new ResourceDictionary(),
                styles);
        }

        /// <summary>
        /// Loads a style document and recursively expands StyleInclude elements.
        /// Relative include sources are resolved beside the document that contains them.
        /// </summary>
        public static StyleSheet LoadFromSource(
            string source,
            Func<string, string> sourceLoader,
            ResourceDictionary resources = null)
        {
            return LoadFromSource(
                source,
                sourceLoader,
                StylePropertyRegistry.Global,
                resources);
        }

        /// <summary>
        /// Loads a style document and recursively expands StyleInclude elements.
        /// Relative include sources are resolved beside the document that contains them.
        /// </summary>
        public static StyleSheet LoadFromSource(
            string source,
            Func<string, string> sourceLoader,
            StylePropertyRegistry registry,
            ResourceDictionary resources = null)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("A style source is required.", nameof(source));
            if (sourceLoader == null)
                throw new ArgumentNullException(nameof(sourceLoader));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            var activeSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Style> styles = LoadSource(
                NormalizeSource(source),
                sourceLoader,
                activeSources);
            if (styles.Count == 0)
                throw new ArgumentException(
                    "At least one style is required.", nameof(source));
            return new StyleSheet(
                registry,
                resources ?? new ResourceDictionary(),
                styles);
        }

        static List<Style> LoadSource(
            string source,
            Func<string, string> sourceLoader,
            ISet<string> activeSources)
        {
            if (!activeSources.Add(source))
                throw new FormatException(
                    "Circular StyleInclude reference to '" + source + "'.");

            try
            {
                string xml;
                try
                {
                    xml = sourceLoader(source);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        "Could not load style source '" + source + "'.",
                        exception);
                }
                if (string.IsNullOrWhiteSpace(xml))
                    throw new FormatException(
                        "Style source '" + source + "' is empty.");

                try
                {
                    return new StyleParser(xml).Parse(includeSource => LoadSource(
                        ResolveSource(source, includeSource),
                        sourceLoader,
                        activeSources));
                }
                catch (FormatException exception)
                {
                    throw new FormatException(
                        "Could not parse style source '" + source + "': " +
                        exception.Message,
                        exception);
                }
            }
            finally
            {
                activeSources.Remove(source);
            }
        }

        static string ResolveSource(string parentSource, string includeSource)
        {
            string include = (includeSource ?? string.Empty).Trim().Replace('\\', '/');
            if (include.Length == 0)
                throw new FormatException("StyleInclude requires a 'Source' attribute.");
            if (include[0] == '/')
                return NormalizeSource(include.Substring(1));

            int separator = parentSource.LastIndexOf('/');
            return NormalizeSource(separator < 0
                ? include
                : parentSource.Substring(0, separator + 1) + include);
        }

        static string NormalizeSource(string source)
        {
            string value = source.Trim().Replace('\\', '/');
            string[] parts = value.Split('/');
            var normalized = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0 || part == ".")
                    continue;
                if (part == "..")
                {
                    if (normalized.Count == 0)
                        throw new FormatException(
                            "Style source escapes its root: '" + source + "'.");
                    normalized.RemoveAt(normalized.Count - 1);
                    continue;
                }
                normalized.Add(part);
            }
            return string.Join("/", normalized.ToArray());
        }

        public void Apply(Control control)
        {
            if (control == null)
                throw new ArgumentNullException(nameof(control));
            ResolvePending(null);
            _registry.Reset(control);
            var context = new StyleSetterContext(_resources);
            for (int i = 0; i < _styles.Count; i++)
            {
                StyleEntry entry = _styles[i];
                if (!entry.IsResolved)
                    continue;
                if (!entry.Selector.Matches(control, _registry))
                    continue;
                for (int setterIndex = 0; setterIndex < entry.Style.Setters.Count; setterIndex++)
                {
                    Setter setter = entry.Style.Setters[setterIndex];
                    _registry.Apply(
                        entry.Selector.TypeName,
                        setter.Property,
                        control,
                        setter.Value,
                        context);
                }
            }
        }

        internal void Reset(Control control)
        {
            if (control != null)
                _registry.Reset(control);
        }

        void Attach()
        {
            if (_attached)
                return;
            _registry.MetadataChanged += ResolvePending;
            _attached = true;
            ResolvePending(null);
        }

        void Detach()
        {
            if (!_attached)
                return;
            _registry.MetadataChanged -= ResolvePending;
            _attached = false;
        }

        void ResolvePending(string selectorName)
        {
            bool changed = false;
            for (int i = 0; i < _styles.Count; i++)
            {
                StyleEntry entry = _styles[i];
                if (entry.IsResolved || selectorName != null && !string.Equals(
                        entry.Selector.TypeName, selectorName, StringComparison.Ordinal))
                    continue;
                if (!_registry.ContainsType(entry.Selector.TypeName))
                    continue;
                bool resolved = true;
                for (int setterIndex = 0;
                     setterIndex < entry.Style.Setters.Count;
                     setterIndex++)
                {
                    if (_registry.ContainsProperty(
                            entry.Selector.TypeName,
                            entry.Style.Setters[setterIndex].Property))
                        continue;
                    resolved = false;
                    break;
                }
                if (!resolved)
                    continue;
                entry.IsResolved = true;
                changed = true;
            }
            if (changed)
                _version++;
        }

        sealed class StyleEntry
        {
            public StyleEntry(Style style, Selector selector)
            {
                Style = style;
                Selector = selector;
            }

            public Style Style { get; }
            public Selector Selector { get; }
            public bool IsResolved { get; set; }
        }

        struct Selector
        {
            public string TypeName;
            public string ClassName;
            public string[] PseudoClasses;

            public static Selector Parse(string text)
            {
                string selector = (text ?? string.Empty).Trim();
                int pseudoSeparator = selector.IndexOf(':');
                string head = pseudoSeparator < 0
                    ? selector
                    : selector.Substring(0, pseudoSeparator);
                string pseudoText = pseudoSeparator < 0
                    ? string.Empty
                    : selector.Substring(pseudoSeparator + 1);

                int classSeparator = head.IndexOf('.');
                string typeName = classSeparator < 0
                    ? head.Trim()
                    : head.Substring(0, classSeparator).Trim();
                string className = classSeparator < 0
                    ? null
                    : head.Substring(classSeparator + 1).Trim();
                if (string.IsNullOrEmpty(typeName) ||
                    classSeparator >= 0 && string.IsNullOrEmpty(className))
                    throw new FormatException("Unsupported style selector '" + text + "'.");

                string[] pseudos = new string[0];
                if (pseudoSeparator >= 0)
                {
                    string[] raw = pseudoText.Split(':');
                    pseudos = new string[raw.Length];
                    for (int i = 0; i < raw.Length; i++)
                    {
                        string pseudo = raw[i].Trim();
                        if (pseudo.Length == 0)
                            throw new FormatException("Unsupported style selector '" + text + "'.");
                        pseudos[i] = ":" + pseudo;
                    }
                }

                return new Selector
                {
                    TypeName = typeName,
                    ClassName = className,
                    PseudoClasses = pseudos
                };
            }

            public bool Matches(Control control, StylePropertyRegistry registry)
            {
                if (!registry.Matches(TypeName, control) ||
                    ClassName != null && !string.Equals(
                        ClassName,
                        control.StyleClass,
                        StringComparison.OrdinalIgnoreCase))
                    return false;
                for (int i = 0; i < PseudoClasses.Length; i++)
                    if (!control.HasPseudoClass(PseudoClasses[i]))
                        return false;
                return true;
            }
        }

        sealed class StyleParser
        {
            readonly string _xml;
            int _position;

            public StyleParser(string xml)
            {
                _xml = xml;
            }

            public List<Style> Parse()
            {
                return Parse(null);
            }

            public List<Style> Parse(
                Func<string, IEnumerable<Style>> includeResolver)
            {
                var result = new List<Style>();
                string elementName;
                while (MoveToStyleElement(out elementName))
                {
                    if (string.Equals(
                            elementName, "StyleInclude", StringComparison.Ordinal))
                    {
                        if (includeResolver == null)
                            Fail("StyleInclude requires a source loader.");
                        var includeAttributes =
                            new Dictionary<string, string>(StringComparer.Ordinal);
                        ParseStartTag("StyleInclude", includeAttributes, true);
                        IEnumerable<Style> includedStyles = includeResolver(
                            GetRequired(includeAttributes, "Source"));
                        if (includedStyles != null)
                            result.AddRange(includedStyles);
                        continue;
                    }

                    var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
                    ParseStartTag("Style", attributes, false);
                    var style = new Style(GetRequired(attributes, "Selector"));
                    while (true)
                    {
                        SkipTrivia();
                        if (Consume("</Style>"))
                            break;
                        if (!PeekElement("Setter"))
                            Fail("Only Setter elements are supported inside Style.");
                        var setterAttributes =
                            new Dictionary<string, string>(StringComparer.Ordinal);
                        bool selfClosing = ParseStartTag(
                            "Setter", setterAttributes, false, true);
                        object setterValue;
                        if (selfClosing)
                        {
                            setterValue = GetRequired(setterAttributes, "Value");
                        }
                        else
                        {
                            if (setterAttributes.ContainsKey("Value"))
                                Fail("A Setter cannot have both Value and a child value.");
                            setterValue = ParseSetterValue();
                            SkipTrivia();
                            if (!Consume("</Setter>"))
                                Fail("Expected </Setter>.");
                        }
                        style.Setters.Add(new Setter(
                            GetRequired(setterAttributes, "Property"),
                            setterValue));
                    }
                    result.Add(style);
                }
                if (result.Count == 0)
                    Fail("No Style elements were found.");
                return result;
            }

            bool MoveToStyleElement(out string name)
            {
                while (_position < _xml.Length)
                {
                    SkipTrivia();
                    if (PeekElement("StyleInclude"))
                    {
                        name = "StyleInclude";
                        return true;
                    }
                    if (PeekElement("Style"))
                    {
                        name = "Style";
                        return true;
                    }
                    int next = _xml.IndexOf('<', _position + 1);
                    if (next < 0)
                        break;
                    _position = next;
                }
                name = null;
                return false;
            }

            object ParseSetterValue()
            {
                SkipTrivia();
                if (PeekElement("ControlTemplate"))
                    return ControlTemplate.Parse(ReadElementXml("ControlTemplate"));
                if (PeekElement("SolidColorBrush"))
                {
                    var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
                    ParseStartTag("SolidColorBrush", attributes, true);
                    return new SolidColorBrush(ParseColor(
                        GetRequired(attributes, "Color")));
                }
                if (PeekElement("TextureBrush"))
                {
                    var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
                    ParseStartTag("TextureBrush", attributes, true);
                    string fallback;
                    string tint;
                    return new TextureBrush(
                        GetRequired(attributes, "Source"),
                        attributes.TryGetValue("Fallback", out fallback) ? ParseColor(fallback) : Color.Transparent,
                        attributes.TryGetValue("Tint", out tint)
                            ? ParseColor(tint)
                            : Color.White);
                }
                if (PeekElement("CompositeTextureBrush"))
                {
                    var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
                    ParseStartTag("CompositeTextureBrush", attributes, true);
                    CompositeBrushOrientation orientation;
                    if (!Enum.TryParse(
                            GetRequired(attributes, "Orientation"), true, out orientation))
                        Fail("CompositeTextureBrush Orientation must be Horizontal or Vertical.");
                    string fallback;
                    string tint;
                    return new CompositeTextureBrush(
                        GetRequired(attributes, "StartSource"),
                        GetRequired(attributes, "CenterSource"),
                        GetRequired(attributes, "EndSource"),
                        orientation,
                        ParseBrushFloat(GetRequired(attributes, "StartLength")),
                        ParseBrushFloat(GetRequired(attributes, "EndLength")),
                        ParseBrushFloat(GetRequired(attributes, "ReferenceCrossLength")),
                        attributes.TryGetValue("Fallback", out fallback) ? ParseColor(fallback) : Color.Transparent,
                        attributes.TryGetValue("Tint", out tint)
                            ? ParseColor(tint)
                            : Color.White);
                }
                if (PeekElement("NineSliceTextureBrush"))
                {
                    var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
                    ParseStartTag("NineSliceTextureBrush", attributes, true);
                    string fallback;
                    string tint;
                    return new NineSliceTextureBrush(
                        GetRequired(attributes, "LeftTopSource"),
                        GetRequired(attributes, "CenterTopSource"),
                        GetRequired(attributes, "RightTopSource"),
                        GetRequired(attributes, "LeftCenterSource"),
                        GetRequired(attributes, "CenterSource"),
                        GetRequired(attributes, "RightCenterSource"),
                        GetRequired(attributes, "LeftBottomSource"),
                        GetRequired(attributes, "CenterBottomSource"),
                        GetRequired(attributes, "RightBottomSource"),
                        ParseBrushFloat(GetRequired(attributes, "LeftWidth")),
                        ParseBrushFloat(GetRequired(attributes, "RightWidth")),
                        ParseBrushFloat(GetRequired(attributes, "TopHeight")),
                        ParseBrushFloat(GetRequired(attributes, "BottomHeight")),
                        ParseBrushFloat(GetRequired(attributes, "ReferenceWidth")),
                        attributes.TryGetValue("Fallback", out fallback) ? ParseColor(fallback) : Color.Transparent,
                        attributes.TryGetValue("Tint", out tint) ? ParseColor(tint) : Color.White);
                }
                Fail("Only ControlTemplate, SolidColorBrush, TextureBrush, CompositeTextureBrush, or NineSliceTextureBrush is supported as a Setter value.");
                return null;
            }

            string ReadElementXml(string expectedName)
            {
                int start = _position;
                int depth = 0;
                while (_position < _xml.Length)
                {
                    int open = _xml.IndexOf('<', _position);
                    if (open < 0)
                        Fail("Unterminated " + expectedName + ".");
                    _position = open;

                    if (Consume("<!--"))
                    {
                        SkipUntil("-->");
                        continue;
                    }
                    if (Consume("<?"))
                    {
                        SkipUntil("?>");
                        continue;
                    }

                    bool closing = Consume("</");
                    if (!closing)
                        Expect('<');
                    string name = ParseName();

                    bool quoted = false;
                    char quote = '\0';
                    bool selfClosing = false;
                    while (_position < _xml.Length)
                    {
                        char character = _xml[_position++];
                        if (quoted)
                        {
                            if (character == quote)
                                quoted = false;
                            continue;
                        }
                        if (character == '\'' || character == '"')
                        {
                            quoted = true;
                            quote = character;
                            continue;
                        }
                        if (character == '>')
                        {
                            int before = _position - 2;
                            while (before >= 0 && char.IsWhiteSpace(_xml[before]))
                                before--;
                            selfClosing = before >= 0 && _xml[before] == '/';
                            break;
                        }
                    }

                    if (string.Equals(name, expectedName, StringComparison.Ordinal))
                    {
                        if (closing)
                            depth--;
                        else if (!selfClosing)
                            depth++;
                        if (depth == 0)
                            return _xml.Substring(start, _position - start);
                    }
                }
                Fail("Unterminated " + expectedName + ".");
                return null;
            }

            static float ParseBrushFloat(string value)
            {
                return float.Parse(value, CultureInfo.InvariantCulture);
            }

            static Color ParseColor(string value)
            {
                return (Color)new Adk.Gui.Markup.PropertyConverter().Convert(
                    value, typeof(Color));
            }

            bool ParseStartTag(
                string expectedName,
                IDictionary<string, string> attributes,
                bool requireSelfClosing,
                bool allowSelfClosing = false)
            {
                Expect('<');
                string name = ParseName();
                if (!string.Equals(name, expectedName, StringComparison.Ordinal))
                    Fail("Expected " + expectedName + ".");
                while (true)
                {
                    SkipWhitespace();
                    if (Consume("/>"))
                    {
                        if (!requireSelfClosing && !allowSelfClosing)
                            Fail(expectedName + " must contain Setter elements.");
                        return true;
                    }
                    if (Consume(">"))
                    {
                        if (requireSelfClosing)
                            Fail(expectedName + " must be self-closing.");
                        return false;
                    }
                    string attributeName = ParseName();
                    SkipWhitespace();
                    Expect('=');
                    SkipWhitespace();
                    attributes[attributeName] = ParseQuotedValue();
                }
            }

            bool PeekElement(string name)
            {
                if (_position >= _xml.Length || _xml[_position] != '<' ||
                    _position + name.Length + 1 > _xml.Length ||
                    string.CompareOrdinal(_xml, _position + 1, name, 0, name.Length) != 0)
                    return false;
                int after = _position + name.Length + 1;
                return after < _xml.Length &&
                       (char.IsWhiteSpace(_xml[after]) || _xml[after] == '>' ||
                        _xml[after] == '/');
            }

            string ParseName()
            {
                int start = _position;
                while (_position < _xml.Length)
                {
                    char value = _xml[_position];
                    if (!(char.IsLetterOrDigit(value) || value == '_' || value == '-' ||
                          value == '.' || value == ':'))
                        break;
                    _position++;
                }
                if (_position == start)
                    Fail("Expected an XML name.");
                return _xml.Substring(start, _position - start);
            }

            string ParseQuotedValue()
            {
                if (_position >= _xml.Length ||
                    (_xml[_position] != '\'' && _xml[_position] != '"'))
                    Fail("Expected a quoted attribute value.");
                char quote = _xml[_position++];
                int start = _position;
                while (_position < _xml.Length && _xml[_position] != quote)
                    _position++;
                if (_position >= _xml.Length)
                    Fail("Unterminated attribute value.");
                string value = DecodeEntities(_xml.Substring(start, _position - start));
                _position++;
                return value;
            }

            void SkipTrivia()
            {
                while (true)
                {
                    SkipWhitespace();
                    if (Consume("<!--"))
                    {
                        SkipUntil("-->");
                        continue;
                    }
                    if (Consume("<?"))
                    {
                        SkipUntil("?>");
                        continue;
                    }
                    return;
                }
            }

            void SkipWhitespace()
            {
                while (_position < _xml.Length && char.IsWhiteSpace(_xml[_position]))
                    _position++;
            }

            void SkipUntil(string marker)
            {
                int found = _xml.IndexOf(marker, _position, StringComparison.Ordinal);
                if (found < 0)
                    Fail("Unterminated XML trivia.");
                _position = found + marker.Length;
            }

            bool Consume(string value)
            {
                if (_position + value.Length > _xml.Length ||
                    string.CompareOrdinal(_xml, _position, value, 0, value.Length) != 0)
                    return false;
                _position += value.Length;
                return true;
            }

            void Expect(char value)
            {
                if (_position >= _xml.Length || _xml[_position] != value)
                    Fail("Expected '" + value + "'.");
                _position++;
            }

            void Fail(string message)
            {
                throw new FormatException(message + " Position: " + _position + ".");
            }

            static string GetRequired(IDictionary<string, string> attributes, string name)
            {
                string value;
                if (!attributes.TryGetValue(name, out value) ||
                    string.IsNullOrWhiteSpace(value))
                    throw new FormatException("Element requires a '" + name + "' attribute.");
                return value.Trim();
            }

            static string DecodeEntities(string value)
            {
                if (value.IndexOf('&') < 0)
                    return value;
                var result = new StringBuilder(value);
                result.Replace("&quot;", "\"");
                result.Replace("&apos;", "'");
                result.Replace("&lt;", "<");
                result.Replace("&gt;", ">");
                result.Replace("&amp;", "&");
                return result.ToString();
            }
        }
    }
}
