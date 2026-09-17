using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using WebOptimizer;

namespace Apilane.Portal.Extensions
{
    public static class AssetsDependencyInjection
    {
        public static IServiceCollection AddAssets(this IServiceCollection services)
        {
            services
                .AddWebOptimizer(pipeline =>
                {
                    Main(pipeline);
                    Data(pipeline);
                    Account(pipeline);
                    CodeMirror(pipeline);
                });

            return services;
        }

        private static void Account(IAssetPipeline pipeline)
        {
            // js
            var jsList = new List<string>()
            {
                "assets/vendor/jquery/jquery-3.7.1.min.js"
            };
            pipeline.MinifyJsFiles(jsList.ToArray());
            pipeline.AddJavaScriptBundle("account.min.js", jsList.ToArray());

            // css
            // Tailwind is NOT bundled here: it is loaded raw via a <link> tag, like GridStack/Chart.js
            // below — WebOptimizer's NUglify re-minifying an already-minified build corrupts it.
            var cssList = new List<string>()
            {
                "assets/account.css"
            };
            pipeline.MinifyCssFiles(cssList.ToArray());
            pipeline.AddCssBundle("account.min.css", cssList.ToArray());
        }

        private static void CodeMirror(IAssetPipeline pipeline)
        {
            // js
            var jsList = new List<string>()
            {
                "assets/vendor/codemirror-5.40.2/codemirror.js",
                "assets/vendor/codemirror-5.40.2/mode/sql/sql.js",
                "assets/vendor/codemirror-5.40.2/edit/matchbrackets.js",
                "assets/vendor/codemirror-5.40.2/show-hint.js",
                "assets/vendor/codemirror-5.40.2/sql-hint.js"
            };
            pipeline.MinifyJsFiles(jsList.ToArray());
            pipeline.AddJavaScriptBundle("codemirror.min.js", jsList.ToArray());

            // css
            var cssList = new List<string>()
            {
                "assets/vendor/codemirror-5.40.2/codemirror.css",
                "assets/vendor/codemirror-5.40.2/material.css"
            };
            pipeline.MinifyCssFiles(cssList.ToArray());
            pipeline.AddCssBundle("codemirror.min.css", cssList.ToArray());
        }

        private static void Data(IAssetPipeline pipeline)
        {
            // css
            // SweetAlert2's dark theme is loaded raw via a <link> tag on _LayoutData.cshtml, like
            // on _Layout.cshtml — same reasoning as Tailwind: NUglify re-minifying an already
            // vendor-optimized build corrupts it.
            var cssList = new List<string>()
            {
                "assets/vendor/bootstrap-icons/font/bootstrap-icons.css",
                "assets/data.css"
            };
            pipeline.MinifyCssFiles(cssList.ToArray());
            pipeline.AddCssBundle("data.min.css", cssList.ToArray());

            // js
            var jsList = new List<string>()
            {
                "assets/vendor/jquery/jquery-3.7.1.min.js",
                "assets/vendor/sweetalert/sweetalert2.min.js",
                "assets/vendor/moment/moment.js",
                "assets/custom.js",
                "assets/vendor/magnific-popup/jquery.magnific-popup.min.js",
                "assets/vendor/jquery-file-upload/jquery.ui.widget.js",
                "assets/vendor/jquery-file-upload/jquery.iframe-transport.js",
                "assets/vendor/jquery-file-upload/jquery.fileupload.js"
            };
            pipeline.MinifyJsFiles(jsList.ToArray());
            pipeline.AddJavaScriptBundle("data.min.js", jsList.ToArray());
        }

        private static void Main(IAssetPipeline pipeline)
        {
            // css
            // Tailwind is NOT bundled here: it is loaded raw via a <link> tag on every layout —
            // re-minifying its already-minified/optimized build with NUglify corrupts it, same
            // reason Chart.js/GridStack are loaded raw below instead of through this bundle.
            var cssList = new List<string>()
            {
                "assets/vendor/bootstrap-icons/font/bootstrap-icons.css",
                "assets/vendor/magnific-popup/magnific-popup.css",
                "assets/custom.css"
            };
            pipeline.MinifyCssFiles(cssList.ToArray());
            pipeline.AddCssBundle("site.min.css", cssList.ToArray());

            // js
            // Alpine.js (+ its Collapse plugin) replaces Bootstrap's JS bundle for all interactive
            // components (dropdowns, collapses/accordions, modals, tabs) and is loaded raw via
            // <script> tags on every layout, in the required load order — see _Layout.cshtml.
            var jsList = new List<string>()
            {
                "assets/vendor/jquery/jquery-3.7.1.min.js",
                "assets/vendor/magnific-popup/jquery.magnific-popup.min.js",
                "assets/vendor/sweetalert/sweetalert2.min.js",
                "assets/vendor/jquery-ui/jquery-ui.js",
                "assets/custom.js",
                // Chart.js is NOT bundled here: re-minifying its already-minified ES build with
                // NUglify breaks the Filler plugin. It is loaded raw via a <script> tag on the
                // Reports page (the only page that draws charts), like GridStack.
                "assets/vendor/jquery-file-upload/jquery.ui.widget.js",
                "assets/vendor/jquery-file-upload/jquery.iframe-transport.js",
                "assets/vendor/jquery-file-upload/jquery.fileupload.js"
            };
            pipeline.MinifyJsFiles(jsList.ToArray());
            pipeline.AddJavaScriptBundle("site.min.js", jsList.ToArray());
        }
    }
}