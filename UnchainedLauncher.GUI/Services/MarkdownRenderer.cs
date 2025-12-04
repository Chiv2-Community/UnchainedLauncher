using Markdig;

namespace UnchainedLauncher.GUI.Services {
    public static class MarkdownRenderer {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        public static string RenderHtml(string markdown) {
            if (string.IsNullOrEmpty(markdown)) return "";

            var html = Markdown.ToHtml(markdown, Pipeline);
            return $@"
            <html>
                <head>
                    <meta http-equiv='X-UA-Compatible' content='IE=edge' />
                    <meta charset='UTF-8' />
                    <style>
                        body {{ 
                            font-family: Segoe UI, sans-serif;
                            margin: 0;
                            padding: 0 8px;
                        }}
                        img {{ max-width: 100%; }}
                        pre {{ 
                            background-color: #f6f8fa;
                            padding: 16px;
                            border-radius: 6px;
                            overflow-x: auto;
                        }}
                        code {{ 
                            font-family: Consolas, monospace;
                            background-color: #f6f8fa;
                            padding: 0.2em 0.4em;
                            border-radius: 3px;
                        }}
                        h1, h2, h3 {{ margin-top: 0.6em; }}
                    </style>
                </head>
                <body>
                    {html}
                </body>
            </html>";
        }
    }
}
