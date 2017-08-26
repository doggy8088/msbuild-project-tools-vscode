using Microsoft.Language.Xml;
using System;
using System.Collections.Generic;

namespace MSBuildProjectTools.LanguageServer.SemanticModel
{
    using Utilities;

    // AF: Does not currently work correctly.

    /// <summary>
    ///     A facility for looking up XML by textual location.
    /// </summary>
    public class XmlLocator
    {
        /// <summary>
        ///     The ranges for all XML nodes in the document
        /// </summary>
        /// <remarks>
        ///     Sorted by range comparison (effectively, this means document order).
        /// </remarks>
        readonly List<Range> _nodeRanges = new List<Range>();

        /// <summary>
        ///     All nodes XML, keyed by starting position.
        /// </summary>
        /// <remarks>
        ///     Sorted by position comparison.
        /// </remarks>
        readonly SortedDictionary<Position, XSNode> _nodesByStartPosition = new SortedDictionary<Position, XSNode>();

        /// <summary>
        ///     The position-lookup for the underlying XML document text.
        /// </summary>
        readonly TextPositions _documentPositions;

        /// <summary>
        ///     Create a new <see cref="XmlLocator"/>.
        /// </summary>
        /// <param name="document">
        ///     The underlying XML document.
        /// </param>
        /// <param name="documentPositions">
        ///     The position-lookup for the underlying XML document text.
        /// </param>
        public XmlLocator(XmlDocumentSyntax document, TextPositions documentPositions)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            
            if (documentPositions == null)
                throw new ArgumentNullException(nameof(documentPositions));
            
            _documentPositions = documentPositions;

            List<XSNode> allNodes = document.GetSemanticModel(_documentPositions);
            foreach (XSNode node in allNodes)
            {
                _nodeRanges.Add(node.Range);
                _nodesByStartPosition.Add(node.Range.Start, node);
            }

            _nodeRanges.Sort();
        }

        /// <summary>
        ///     Inspect the specified position in the XML.
        /// </summary>
        /// <param name="position">
        ///     The target position.
        /// </param>
        /// <returns>
        ///     An <see cref="XmlPosition"/> representing the result of the inspection.
        /// </returns>
        public XmlPosition Inspect(Position position)
        {
            if (position == null)
                throw new ArgumentNullException(nameof(position));

            // Internally, we always use 1-based indexing because this is what the System.Xml APIs (and I'd rather keep things simple).
            position = position.ToOneBased();

            XSNode nodeAtPosition = FindNode(position);
            if (nodeAtPosition == null)
                return null;

            int absolutePosition = _documentPositions.GetAbsolutePosition(position);
            XmlPositionFlags flags = GetPositionFlags(position, absolutePosition, nodeAtPosition);

            return new XmlPosition(position, absolutePosition, nodeAtPosition, flags);
        }

        /// <summary>
        ///     Inspect the specified position in the XML.
        /// </summary>
        /// <param name="absolutePosition">
        ///     The target position (0-based).
        /// </param>
        /// <returns>
        ///     An <see cref="XmlPosition"/> representing the result of the inspection.
        /// </returns>
        public XmlPosition Inspect(int absolutePosition)
        {
            if (absolutePosition < 0)
                throw new ArgumentOutOfRangeException(nameof(absolutePosition), absolutePosition, "Absolute position cannot be less than 0.");

            return Inspect(
                _documentPositions.GetPosition(absolutePosition)
            );
        }

        /// <summary>
        ///     Find the node (if any) at the specified position.
        /// </summary>
        /// <param name="position">
        ///     The target position.
        /// </param>
        /// <returns>
        ///     The node, or <c>null</c> if no node was found at the specified position.
        /// </returns>
        public XSNode FindNode(Position position)
        {
            if (position == null)
                throw new ArgumentNullException(nameof(position));

            // TODO: Use binary search.

            Range lastMatchingRange = null;
            foreach (Range objectRange in _nodeRanges)
            {
                if (position < objectRange)
                    break;

                if (lastMatchingRange != null && objectRange > lastMatchingRange)
                {
                    lastMatchingRange = null;

                    break; // No match.
                }

                if (objectRange.Contains(position))
                    lastMatchingRange = objectRange;
            }
            if (lastMatchingRange == null)
                return null;

            return _nodesByStartPosition[lastMatchingRange.Start];
        }

        /// <summary>
        ///     Determine <see cref="XmlPositionFlags"/> for the specified position.
        /// </summary>
        /// <param name="position">
        ///     The (1-based) target line and column.
        /// </param>
        /// <param name="absolutePosition">
        ///     The (0-based) absolute position.
        /// </param>
        /// <param name="nearestNode">
        ///     The target position's nearest node.
        /// </param>
        /// <returns>
        ///     <see cref="XmlPositionFlags"/> describing the position.
        /// </returns>
        XmlPositionFlags GetPositionFlags(Position position, int absolutePosition, XSNode nearestNode)
        {
            XmlPositionFlags flags = XmlPositionFlags.None;

            switch (nearestNode)
            {
                case XSEmptyElement element:
                {
                    flags |= XmlPositionFlags.Element | XmlPositionFlags.Empty;

                    XmlEmptyElementSyntax syntaxNode = element.ElementNode;

                    TextSpan nameSpan = syntaxNode.NameNode?.Span ?? new TextSpan();
                    if (nameSpan.Contains(absolutePosition))
                        flags |= XmlPositionFlags.Name;

                    break;
                }
                case XSElementWithContent elementWithContent:
                {
                    flags |= XmlPositionFlags.Element;

                    XmlElementSyntax syntaxNode = elementWithContent.ElementNode;

                    TextSpan nameSpan = syntaxNode.NameNode?.Span ?? new TextSpan();
                    if (nameSpan.Contains(absolutePosition))
                        flags |= XmlPositionFlags.Name;

                    TextSpan startTagSpan = syntaxNode.StartTag?.Span ?? new TextSpan();
                    if (startTagSpan.Contains(absolutePosition))
                        flags |= XmlPositionFlags.OpeningTag;

                    TextSpan endTagSpan = syntaxNode.EndTag?.Span ?? new TextSpan();
                    if (endTagSpan.Contains(absolutePosition))
                        flags |= XmlPositionFlags.ClosingTag;

                    if (absolutePosition >= startTagSpan.End && absolutePosition <= endTagSpan.Start)
                        flags |= XmlPositionFlags.Value;

                    break;
                }
                case XSAttribute attribute:
                {
                    flags |= XmlPositionFlags.Attribute;

                    XmlAttributeSyntax syntaxNode = attribute.AttributeNode;

                    TextSpan nameSpan = syntaxNode.NameNode?.Span ?? new TextSpan();
                    if (nameSpan.Contains(absolutePosition))
                        flags |= XmlPositionFlags.Name;

                    TextSpan valueSpan = syntaxNode.ValueNode?.Span ?? new TextSpan();
                    if (valueSpan.Contains(absolutePosition))
                        flags |= XmlPositionFlags.Value;

                    break;
                }
                case XSElementText text:
                {
                    flags |= XmlPositionFlags.Text | XmlPositionFlags.Element | XmlPositionFlags.Value;

                    break;
                }
                case XSWhitespace whitespace:
                {
                    flags |= XmlPositionFlags.Whitespace | XmlPositionFlags.Element | XmlPositionFlags.Value;

                    break;
                }
            }

            return flags;
        }
    }
}
