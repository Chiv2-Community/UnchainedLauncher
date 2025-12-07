using log4net;
using Markdig;
using System;
using System.Windows;
using System.Windows.Media;

namespace UnchainedLauncher.GUI.Services {
    public static class MarkdownRenderer {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(MarkdownRenderer));

        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        public static string RenderHtml(string markdown, string? appendHtml = null) {
            // Resolve theme colors from WPF resources (fallbacks are from Colors.xaml dark theme)
            var bg = BrushHex("Brush.Background", "#121214");
            var text = BrushHex("Brush.TextPrimary", "#EDEDEF");
            var textSecondary = BrushHex("Brush.TextSecondary", "#B0B3B8");
            var border = BrushHex("Brush.Border", "#2A2A2A");
            var inputBg = BrushHex("Brush.InputBackground", "#1E1E22");
            var primary = BrushHex("Brush.Primary", "#4C8DFF");
            var primaryHover = BrushHex("Brush.PrimaryHover", "#3B78E7");

            var hasContent = !string.IsNullOrWhiteSpace(markdown);
            var html = hasContent ? Markdown.ToHtml(markdown, Pipeline) : "<p style='opacity:0.7'>No release notes provided.</p>";
            appendHtml ??= "";
            
            return $@"
            <html>
                <head>
                    <meta http-equiv='X-UA-Compatible' content='IE=edge' />
                    <meta charset='UTF-8' />
                    <style>
                        :root {{
                            --bg: {bg};
                            --text: {text};
                            --text-secondary: {textSecondary};
                            --border: {border};
                            --input-bg: {inputBg};
                            --primary: {primary};
                            --primary-hover: {primaryHover};
                        }}

                        html, body {{
                            background-color: var(--bg);
                            color: var(--text);
                            font-family: Segoe UI, sans-serif;
                            margin: 0;
                            padding: 0 8px;
                        }}
                        p {{ color: var(--text); }}
                        strong {{ color: var(--text); }}
                        em {{ color: var(--text); }}
                        small, .muted {{ color: var(--text-secondary); }}
                        a {{ color: var(--primary); text-decoration: none; }}
                        a:hover {{ color: var(--primary-hover); text-decoration: underline; }}
                        img {{ max-width: 100%; }}
                        pre {{ 
                            background-color: var(--input-bg);
                            padding: 16px;
                            border-radius: 6px;
                            overflow-x: auto;
                            border: 1px solid var(--border);
                        }}
                        code {{ 
                            font-family: Consolas, monospace;
                            background-color: var(--input-bg);
                            padding: 0.2em 0.4em;
                            border-radius: 3px;
                            border: 1px solid var(--border);
                        }}
                        h1, h2, h3 {{ margin-top: 0.6em; color: var(--text); }}
                        hr {{ border: none; border-top: 1px solid var(--border); }}
                        blockquote {{ border-left: 3px solid var(--primary); margin: 0; padding: 8px 12px; background: rgba(76,141,255,0.06); }}
                        table {{ border-collapse: collapse; width: 100%; }}
                        th, td {{ border: 1px solid var(--border); padding: 8px; }}
                    </style>
                </head>
                <body>
                    {html}
                    {appendHtml}
                </body>
            </html>";
        }

        private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static string BrushHex(string brushKey, string fallback) {
            try {
                var res = Application.Current?.TryFindResource(brushKey);
                if (res is SolidColorBrush scb) {
                    return ColorToHex(scb.Color);
                }
                if (res is Color color) {
                    return ColorToHex(color);
                }
            }
            catch (Exception e) {
                Logger.Error($"Failed to resolve color resource {brushKey}.", e);
            }
            return fallback;
        }
    }


}