using System;
using System.Collections.Generic;
using System.IO;
using TrueCreation.Host;
using UnityEngine;
using UnityEngine.UIElements;

namespace TrueCreation.App
{
    /// <summary>
    /// The exe's Reference tab: Reference.txt (StreamingAssets/Reference), the end-user reference, shown as plain text.
    /// The build also copies it next to the exe, so it can be read without the program. The editor keeps the 1.0.4
    /// export browser (ReferencePanel) in this tab; edit Assets/StreamingAssets/Reference/Reference.txt to change what
    /// users read.
    /// </summary>
    public sealed class ReferenceDocPanel : VisualElement
    {
        public const string RelativePath = "Reference/Reference.txt";

        private static readonly Color TitleColor = new Color(0.93f, 0.93f, 0.95f);
        private static readonly Color HeadingColor = new Color(0.62f, 0.78f, 1f);
        private static readonly Color BodyColor = new Color(0.82f, 0.82f, 0.84f);
        private static readonly Color VerbatimColor = new Color(0.78f, 0.86f, 0.78f);
        private static readonly Color ErrorColor = new Color(0.95f, 0.5f, 0.5f);

        public ReferenceDocPanel()
        {
            style.flexGrow = 1;
            var path = AppPaths.Shipped(RelativePath);

            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row; head.style.alignItems = Align.Center; head.style.flexShrink = 0;
            head.style.paddingLeft = 10; head.style.paddingRight = 10; head.style.paddingTop = 8; head.style.paddingBottom = 6;
            head.style.borderBottomWidth = 1; head.style.borderBottomColor = new Color(0.07f, 0.07f, 0.08f);
            var title = new Label("Reference");
            title.style.fontSize = 16; title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.flexGrow = 1;
            head.Add(title);
            var open = new Button(() => AppHost.Current.OpenExternal(path)) { text = "Open as a text file", tooltip = path };
            open.style.height = 24;
            open.SetEnabled(File.Exists(path));
            head.Add(open);
            Add(head);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            var body = scroll.contentContainer;
            body.style.paddingLeft = 14; body.style.paddingRight = 14; body.style.paddingTop = 8; body.style.paddingBottom = 16;
            body.style.maxWidth = 980;
            Add(scroll);

            string text = null, problem = null;
            try { if (File.Exists(path)) text = File.ReadAllText(path); }
            catch (Exception e) { problem = e.Message; }
            if (text == null)
            {
                scroll.Add(Text(problem != null
                        ? "Could not read " + path + ": " + problem
                        : "Reference.txt is missing from " + path + ". It ships with True Creation: reinstall True Creation to restore it.",
                    12, FontStyle.Normal, ErrorColor, 0, 0, 0));
                return;
            }

            foreach (var block in ReferenceDocText.Parse(text))
            {
                switch (block.Kind)
                {
                    case ReferenceDocText.BlockKind.Title:
                        scroll.Add(Text(block.Text, 18, FontStyle.Bold, TitleColor, 0, 6, 0));
                        break;
                    case ReferenceDocText.BlockKind.Heading:
                        scroll.Add(Text(block.Text, 14, FontStyle.Bold, HeadingColor, 14, 4, 0));
                        break;
                    case ReferenceDocText.BlockKind.Subheading:
                        scroll.Add(Text(block.Text, 12, FontStyle.Bold, TitleColor, 8, 2, 0));
                        break;
                    case ReferenceDocText.BlockKind.Verbatim:
                        scroll.Add(Text(block.Text, 12, FontStyle.Normal, VerbatimColor, 1, 1, block.Indent));
                        break;
                    default:
                        scroll.Add(Text(block.Text, 12, FontStyle.Normal, BodyColor, 2, 2, block.Indent));
                        break;
                }
            }
        }

        private static Label Text(string text, int size, FontStyle weight, Color color, float top, float bottom, int indent)
        {
            var label = new Label(text);
            label.style.fontSize = size; label.style.unityFontStyleAndWeight = weight; label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = top; label.style.marginBottom = bottom; label.style.paddingLeft = indent * 16;
            label.selection.isSelectable = true; // a path or a mod name can be copied straight out of the text
            return label;
        }
    }

    /// <summary>
    /// Reference.txt's plain-text layout: a title underlined with '=', section headings underlined with '-', smaller
    /// headings underlined with '~', paragraphs separated by blank lines, "- " bullets, and lines indented by 4 or more
    /// spaces kept as they are (paths). Other continuation lines are joined, so the text rewraps to the panel's width.
    /// No Unity types: it can be checked on its own.
    /// </summary>
    public static class ReferenceDocText
    {
        public enum BlockKind { Title, Heading, Subheading, Paragraph, Bullet, Verbatim }

        public sealed class Block
        {
            public BlockKind Kind;
            public string Text;
            public int Indent;
        }

        public static List<Block> Parse(string text)
        {
            var blocks = new List<Block>();
            var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            Block current = null;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd();
                if (line.Length == 0) { current = null; continue; }
                var next = i + 1 < lines.Length ? lines[i + 1].Trim() : "";
                if (IsRule(next, '=') || IsRule(next, '-') || IsRule(next, '~'))
                {
                    var kind = next[0] == '=' ? BlockKind.Title : next[0] == '-' ? BlockKind.Heading : BlockKind.Subheading;
                    blocks.Add(new Block { Kind = kind, Text = line.Trim() });
                    i++;
                    current = null;
                    continue;
                }
                var trimmed = line.TrimStart();
                var lead = line.Length - trimmed.Length;
                if (lead >= 4)
                {
                    blocks.Add(new Block { Kind = BlockKind.Verbatim, Text = trimmed, Indent = 2 });
                    current = null;
                    continue;
                }
                if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    current = new Block { Kind = BlockKind.Bullet, Text = "- " + trimmed.Substring(2).Trim(), Indent = 1 + lead / 2 };
                    blocks.Add(current);
                    continue;
                }
                if (current != null)
                {
                    current.Text += " " + trimmed;
                    continue;
                }
                current = new Block { Kind = BlockKind.Paragraph, Text = trimmed, Indent = lead / 2 };
                blocks.Add(current);
            }
            return blocks;
        }

        private static bool IsRule(string s, char c)
        {
            if (s.Length < 3) return false;
            foreach (var ch in s)
                if (ch != c) return false;
            return true;
        }
    }
}
